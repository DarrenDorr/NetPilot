using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;
using NetPilot.Security;
using NetPilot.Updater;

namespace NetPilot.Service;

public sealed class PrivilegedIpcWorker(
    WindowsNamedPipeServerFactory pipeFactory,
    WindowsNamedPipePeerAuthenticator peerAuthenticator,
    OperationPolicy operationPolicy,
    UserResourceOwnership zapretOwnership,
    ZapretEngineController zapretController,
    ZapretUpdateOrchestrator zapretUpdater,
    ZapretInstallationInspector installationInspector,
    ILogger<PrivilegedIpcWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Служба IPC NetPilot запущена.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = pipeFactory.Create();
                await pipe.WaitForConnectionAsync(stoppingToken);
                await HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Ошибка обработки локального IPC-соединения. Содержимое сообщения не журналируется.");

                await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
            }
        }
    }

    private async Task HandleClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        var clientAuthentication = peerAuthenticator.AuthenticateClient(pipe);
        if (!clientAuthentication.IsAuthenticated || clientAuthentication.Identity is null)
        {
            logger.LogWarning(
                "IPC-соединение отклонено: клиент не прошёл авторизацию. Код: {Code}.",
                clientAuthentication.Code);
            return;
        }

        var clientIdentity = clientAuthentication.Identity;
        logger.LogDebug(
            "IPC-клиент авторизован. PID: {ProcessId}; Session: {SessionId}.",
            clientIdentity.ProcessId,
            clientIdentity.SessionId);

        IpcRequest request;
        using (var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            readTimeout.CancelAfter(IpcProtocol.RequestTimeoutMilliseconds);
            try
            {
                request = await IpcFraming.ReadAsync<IpcRequest>(pipe, readTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("IPC-соединение закрыто: превышено допустимое время ожидания запроса.");
                return;
            }
            catch (IpcProtocolException exception)
            {
                logger.LogWarning(
                    "IPC-сообщение отклонено. Код: {Code}. Содержимое сообщения не журналируется.",
                    exception.Code);
                return;
            }
        }

        var validation = IpcRequestValidator.Validate(request);
        if (!validation.IsValid)
        {
            await SendResponseWithTimeoutAsync(
                pipe,
                Failure(request, validation.Code, validation.Message),
                cancellationToken);
            return;
        }

        IpcResponse response;
        using (var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            operationTimeout.CancelAfter(
                IpcProtocol.GetRequestTimeoutMilliseconds(request.Command));

            try
            {
                response = await DispatchAsync(
                    request,
                    clientIdentity.UserSid,
                    operationTimeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response = Failure(
                    request,
                    "OperationTimeout",
                    "Операция NetPilot превысила допустимое время и была остановлена.");
            }
            catch (ZapretStrategyParseException exception)
            {
                response = Failure(request, exception.Code, exception.Message);
            }
            catch (ZapretProcessRunnerException exception)
            {
                response = Failure(request, exception.Code, exception.Message);
            }
            catch (UpdateSecurityException exception)
            {
                response = Failure(request, exception.Code, exception.Message);
            }
            catch (HttpRequestException)
            {
                response = Failure(
                    request,
                    "UpdateNetworkError",
                    "Не удалось получить официальный релиз Zapret из доверенного источника.");
            }
            catch (DirectoryNotFoundException)
            {
                response = Failure(
                    request,
                    "ZapretNotInstalled",
                    "Движок Zapret ещё не установлен в защищённый каталог NetPilot.");
            }
            catch (UnauthorizedAccessException)
            {
                response = Failure(
                    request,
                    "EngineAccessDenied",
                    "Служба не получила безопасный доступ к файлам движка Zapret.");
            }
            catch (IOException)
            {
                response = Failure(
                    request,
                    "EngineIoError",
                    "Не удалось безопасно прочитать или изменить состояние движка Zapret.");
            }
        }

        await SendResponseWithTimeoutAsync(pipe, response, cancellationToken);
    }

    private async Task<IpcResponse> DispatchAsync(
        IpcRequest request,
        string clientSid,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case IpcCommand.Ping:
                return Success(
                    request,
                    "Служба NetPilot доступна и IPC-клиент авторизован.");

            case IpcCommand.ZapretListStrategies:
            {
                var strategies = zapretController.ListStrategies()
                    .Select(static strategy =>
                        new IpcZapretStrategy(strategy.Id, strategy.DisplayName))
                    .ToArray();

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    Strategies: strategies);
            }

            case IpcCommand.ZapretGetStatus:
            {
                var status = await zapretController.GetStatusAsync(cancellationToken);
                ReconcileOwnership(status);

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    ZapretStatus: ToIpcStatus(
                        status,
                        includeStrategy: status.State == ZapretProcessState.Running
                            && zapretOwnership.IsOwnedBy(clientSid)));
            }

            case IpcCommand.ZapretGetInstallationStatus:
            {
                var installation = await installationInspector.GetStateAsync(cancellationToken);
                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    ZapretInstallation: new IpcZapretInstallationStatus(
                        installation.IsInstalled,
                        installation.Version));
            }

            case IpcCommand.ZapretInstallOrUpdateOfficialLatest:
                return await InstallOrUpdateOfficialZapretAsync(
                    request,
                    clientSid,
                    cancellationToken);

            case IpcCommand.ZapretStartStrategy:
            {
                if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStart))
                {
                    return Failure(request, "OperationDenied", "Запуск движка запрещён политикой безопасности.");
                }

                var current = await zapretController.GetStatusAsync(cancellationToken);
                ReconcileOwnership(current);

                if (current.State == ZapretProcessState.Running
                    && !zapretOwnership.IsOwnedBy(clientSid))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Запущенный экземпляр Zapret принадлежит другой пользовательской сессии.");
                }

                if (!zapretOwnership.TryAcquire(clientSid))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Право управления Zapret уже принадлежит другой пользовательской сессии.");
                }

                try
                {
                    var started = await zapretController.StartAsync(request.StrategyId!, cancellationToken);
                    return new IpcResponse(
                        IpcProtocol.Version,
                        request.RequestId,
                        true,
                        "Ok",
                        "Zapret запущен.",
                        ZapretStatus: ToIpcStatus(started, includeStrategy: true));
                }
                catch
                {
                    var afterFailure = await zapretController.GetStatusAsync(CancellationToken.None);
                    if (afterFailure.State == ZapretProcessState.Stopped)
                    {
                        zapretOwnership.Clear();
                    }

                    throw;
                }
            }

            case IpcCommand.ZapretStop:
            {
                if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStop))
                {
                    return Failure(request, "OperationDenied", "Остановка движка запрещена политикой безопасности.");
                }

                var current = await zapretController.GetStatusAsync(cancellationToken);
                ReconcileOwnership(current);

                if (current.State == ZapretProcessState.Running
                    && !zapretOwnership.IsOwnedBy(clientSid))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Нельзя остановить экземпляр Zapret, запущенный другой пользовательской сессией.");
                }

                var stopped = await zapretController.StopAsync(cancellationToken);
                zapretOwnership.Clear();

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    "Zapret остановлен.",
                    ZapretStatus: ToIpcStatus(stopped, includeStrategy: false));
            }

            default:
                return Failure(request, "UnknownCommand", "Указана неизвестная IPC-команда.");
        }
    }

    private async Task<IpcResponse> InstallOrUpdateOfficialZapretAsync(
        IpcRequest request,
        string clientSid,
        CancellationToken cancellationToken)
    {
        var installation = await installationInspector.GetStateAsync(cancellationToken);
        var requiredOperation = installation.IsInstalled
            ? PrivilegedOperation.UpdateApply
            : PrivilegedOperation.EngineInstall;

        if (!operationPolicy.IsAllowed(requiredOperation))
        {
            return Failure(
                request,
                "OperationDenied",
                installation.IsInstalled
                    ? "Обновление движка запрещено политикой безопасности."
                    : "Установка движка запрещена политикой безопасности.");
        }

        var current = await zapretController.GetStatusAsync(cancellationToken);
        ReconcileOwnership(current);

        if (current.State == ZapretProcessState.Running
            && !zapretOwnership.IsOwnedBy(clientSid))
        {
            return Failure(
                request,
                "EngineOwnedByAnotherUser",
                "Нельзя обновить Zapret, запущенный другой пользовательской сессией.");
        }

        ZapretUpdateApplyResult result;
        try
        {
            result = await zapretUpdater.ApplyLatestAsync(cancellationToken);
        }
        catch (UpdateSecurityException exception)
            when (string.Equals(exception.Code, "VersionAlreadyInstalled", StringComparison.Ordinal))
        {
            return new IpcResponse(
                IpcProtocol.Version,
                request.RequestId,
                true,
                "AlreadyLatest",
                "Установлена актуальная официальная версия Zapret.",
                ZapretInstallation: new IpcZapretInstallationStatus(
                    true,
                    installation.Version));
        }
        finally
        {
            try
            {
                var after = await zapretController.GetStatusAsync(CancellationToken.None);
                ReconcileOwnership(after);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // Не скрываем основной результат provisioning. Следующий status-запрос заново
                // сверит фактическое состояние движка и ownership.
            }
        }

        return new IpcResponse(
            IpcProtocol.Version,
            request.RequestId,
            true,
            installation.IsInstalled ? "Updated" : "Installed",
            installation.IsInstalled
                ? "Официальный Zapret безопасно обновлён."
                : "Официальный Zapret безопасно установлен.",
            ZapretInstallation: new IpcZapretInstallationStatus(true, result.Version),
            ZapretProvisioning: new IpcZapretProvisioningResult(
                result.Version,
                result.PreviousVersionAvailable,
                ToIpcRestoreState(result.RuntimeRestore.Status),
                result.RuntimeRestore.StrategyId,
                result.RuntimeRestore.ErrorCode));
    }

    private void ReconcileOwnership(ZapretProcessSnapshot status)
    {
        if (status.State == ZapretProcessState.Stopped)
        {
            zapretOwnership.Clear();
        }
    }

    private static IpcZapretRuntimeRestoreState ToIpcRestoreState(
        ZapretRuntimeRestoreStatus status) =>
        status switch
        {
            ZapretRuntimeRestoreStatus.NotRequired => IpcZapretRuntimeRestoreState.NotRequired,
            ZapretRuntimeRestoreStatus.Restored => IpcZapretRuntimeRestoreState.Restored,
            ZapretRuntimeRestoreStatus.Failed => IpcZapretRuntimeRestoreState.Failed,
            _ => IpcZapretRuntimeRestoreState.Failed
        };

    private static IpcZapretStatus ToIpcStatus(
        ZapretProcessSnapshot snapshot,
        bool includeStrategy) =>
        new(
            snapshot.State == ZapretProcessState.Running
                ? IpcZapretProcessState.Running
                : IpcZapretProcessState.Stopped,
            includeStrategy ? snapshot.StrategyId : null);

    private static IpcResponse Success(IpcRequest request, string message) =>
        new(IpcProtocol.Version, request.RequestId, true, "Ok", message);

    private static IpcResponse Failure(
        IpcRequest request,
        string code,
        string? message) =>
        new(IpcProtocol.Version, request.RequestId, false, code, message);

    private static async Task SendResponseWithTimeoutAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken)
    {
        using var writeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        writeTimeout.CancelAfter(IpcProtocol.RequestTimeoutMilliseconds);
        await IpcFraming.WriteAsync(stream, response, writeTimeout.Token);
    }
}
