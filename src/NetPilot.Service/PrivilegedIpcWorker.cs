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
    WindowsAuthorizedUserStore authorizedUserStore,
    WindowsUserProfileStore userProfileStore,
    OperationPolicy operationPolicy,
    UserResourceOwnership zapretOwnership,
    ZapretEngineController zapretController,
    ZapretUpdateOrchestrator zapretUpdater,
    ILogger<PrivilegedIpcWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        authorizedUserStore.EnsureEnrollmentNonce();
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
                "IPC-соединение отклонено: клиент не прошёл проверку Windows. Код: {Code}.",
                clientAuthentication.Code);
            return;
        }

        var clientIdentity = clientAuthentication.Identity;
        logger.LogDebug(
            "Локальный IPC-клиент проверен. PID: {ProcessId}; Session: {SessionId}.",
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
                logger.LogWarning("IPC-соединение закрыто: превышено время ожидания запроса.");
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
            await SendResponseAsync(
                pipe,
                Failure(request, validation.Code, validation.Message),
                cancellationToken);
            return;
        }

        // Первичная UAC-регистрация по-прежнему фиксирует владельца системной установки.
        // После неё обычный runtime не ограничивается одним SID: каждый проверенный
        // интерактивный Windows SID получает отдельный service-owned профиль.
        if (request.Command != IpcCommand.EnrollAuthorizedUser
            && !authorizedUserStore.IsEnrolled)
        {
            await SendResponseAsync(
                pipe,
                Failure(
                    request,
                    "EnrollmentRequired",
                    "Системная установка NetPilot ещё не завершила первичную регистрацию владельца."),
                cancellationToken);
            return;
        }

        var operationTimeoutMilliseconds = request.Command == IpcCommand.ZapretInstallLatest
            ? IpcProtocol.EngineInstallTimeoutMilliseconds
            : IpcProtocol.RequestTimeoutMilliseconds;

        IpcResponse response;
        using (var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            operationTimeout.CancelAfter(operationTimeoutMilliseconds);
            var operationToken = operationTimeout.Token;

            try
            {
                if (request.Command == IpcCommand.EnrollAuthorizedUser)
                {
                    response = DispatchEnrollment(
                        request,
                        clientIdentity.UserSid,
                        clientIdentity.SessionId);
                }
                else
                {
                    // Создание/открытие профиля выполняет только LocalSystem-служба и только
                    // для SID, который уже был подтверждён через named-pipe peer authentication.
                    userProfileStore.EnsureProfile(clientIdentity.UserSid);
                    response = await DispatchAuthorizedAsync(
                        request,
                        clientIdentity.UserSid,
                        operationToken);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                response = Failure(
                    request,
                    "OperationTimeout",
                    "Операция NetPilot превысила допустимое время выполнения и была остановлена.");
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
                    "EngineDownloadNetworkError",
                    "Не удалось получить официальный пакет Zapret с GitHub.");
            }
            catch (DirectoryNotFoundException)
            {
                response = Failure(
                    request,
                    "ZapretNotInstalled",
                    "Движок Zapret ещё не установлен в защищённый каталог NetPilot.");
            }
            catch (InvalidDataException)
            {
                response = Failure(
                    request,
                    "ProtectedConfigurationInvalid",
                    "Защищённая конфигурация NetPilot повреждена или имеет недопустимый формат.");
            }
            catch (UnauthorizedAccessException)
            {
                response = Failure(
                    request,
                    "EngineAccessDenied",
                    "Служба не получила безопасный доступ к системным файлам NetPilot.");
            }
            catch (IOException)
            {
                response = Failure(
                    request,
                    "EngineIoError",
                    "Не удалось безопасно выполнить файловую операцию NetPilot.");
            }
        }

        await SendResponseAsync(pipe, response, cancellationToken);
    }

    private IpcResponse DispatchEnrollment(
        IpcRequest request,
        string clientSid,
        uint clientSessionId)
    {
        var result = authorizedUserStore.CompleteEnrollment(
            request.EnrollmentNonce!,
            clientSid,
            clientSessionId);

        if (result.Success)
        {
            userProfileStore.EnsureProfile(clientSid);
        }

        return result.Success
            ? Success(
                request,
                result.Status == AuthorizedUserEnrollmentStatus.Enrolled
                    ? "Владелец системной установки NetPilot зарегистрирован; пользовательский SID-профиль создан."
                    : "Этот пользователь Windows уже является владельцем системной установки NetPilot.")
            : Failure(
                request,
                result.Code,
                result.Status switch
                {
                    AuthorizedUserEnrollmentStatus.InvalidNonce =>
                        "Одноразовый код регистрации NetPilot отклонён.",
                    AuthorizedUserEnrollmentStatus.InvalidSession =>
                        "Регистрация выполняется не из пользовательской сессии установщика.",
                    _ => "Регистрация владельца системной установки NetPilot больше недоступна."
                });
    }

    private async Task<IpcResponse> DispatchAuthorizedAsync(
        IpcRequest request,
        string clientSid,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case IpcCommand.Ping:
                return Success(
                    request,
                    "Служба NetPilot доступна; интерактивный пользователь работает в отдельном SID-профиле.");

            case IpcCommand.ZapretGetInstallationStatus:
            {
                var engineRoot = WindowsZapretEngineLocation.GetDefaultEngineRoot();
                if (!Directory.Exists(engineRoot))
                {
                    return new IpcResponse(
                        IpcProtocol.Version,
                        request.RequestId,
                        true,
                        "Ok",
                        ZapretInstallation: new IpcZapretInstallationStatus(false));
                }

                var manifest = await EngineInstallationManifest.ReadAsync(
                    engineRoot,
                    cancellationToken);

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    ZapretInstallation: new IpcZapretInstallationStatus(
                        true,
                        manifest.Version));
            }

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

            case IpcCommand.ZapretInstallLatest:
            {
                // Engine package является машинным ресурсом. Обычные пользователи могут
                // пользоваться установленным runtime, но provisioning остаётся за SID,
                // подтверждённым реальной UAC-регистрацией установки.
                if (!authorizedUserStore.IsAuthorized(clientSid))
                {
                    return Failure(
                        request,
                        "InstallationOwnerRequired",
                        "Установка системного сетевого движка доступна только владельцу установки NetPilot.");
                }

                if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineInstall))
                {
                    return Failure(
                        request,
                        "OperationDenied",
                        "Установка сетевого движка запрещена политикой безопасности.");
                }

                if (Directory.Exists(WindowsZapretEngineLocation.GetDefaultEngineRoot()))
                {
                    return Failure(
                        request,
                        "ZapretAlreadyInstalled",
                        "Zapret уже установлен. Обновление является отдельной операцией и здесь не выполняется.");
                }

                var result = await zapretUpdater.ApplyLatestAsync(cancellationToken);
                if (result.RuntimeRestore.Status != ZapretRuntimeRestoreStatus.NotRequired)
                {
                    return Failure(
                        request,
                        "UnexpectedRuntimeRestore",
                        "Первая установка завершилась в неожиданном состоянии движка.");
                }

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    "Официальный пакет Zapret установлен.",
                    ZapretInstall: new IpcZapretInstallResult(
                        result.Version,
                        Installed: true,
                        result.TemporaryCleanupCompleted));
            }

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
                        "NetPilot уже активен в другой пользовательской сессии.");
                }

                if (!zapretOwnership.TryAcquire(clientSid))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Системная engine-сессия NetPilot уже занята другой пользовательской сессией.");
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
                        "Нельзя остановить NetPilot, активированный другой пользовательской сессией.");
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

            case IpcCommand.EnrollAuthorizedUser:
                return Failure(
                    request,
                    "EnrollmentDispatchError",
                    "Команда регистрации не должна попадать в обычный обработчик.");

            default:
                return Failure(request, "UnknownCommand", "Указана неизвестная IPC-команда.");
        }
    }

    private void ReconcileOwnership(ZapretProcessSnapshot status)
    {
        if (status.State == ZapretProcessState.Stopped)
        {
            zapretOwnership.Clear();
        }
    }

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

    private static async Task SendResponseAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken)
    {
        using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseTimeout.CancelAfter(IpcProtocol.RequestTimeoutMilliseconds);
        await IpcFraming.WriteAsync(stream, response, responseTimeout.Token);
    }
}
