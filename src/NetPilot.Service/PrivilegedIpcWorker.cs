using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;
using NetPilot.Security;

namespace NetPilot.Service;

public sealed class PrivilegedIpcWorker(
    WindowsNamedPipeServerFactory pipeFactory,
    WindowsNamedPipePeerAuthenticator peerAuthenticator,
    OperationPolicy operationPolicy,
    ZapretEngineController zapretController,
    ILogger<PrivilegedIpcWorker> logger) : BackgroundService
{
    private string? _zapretOwnerSid;

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

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(IpcProtocol.RequestTimeoutMilliseconds);
        var requestToken = requestTimeout.Token;

        IpcRequest request;
        try
        {
            request = await IpcFraming.ReadAsync<IpcRequest>(pipe, requestToken);
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

        var validation = IpcRequestValidator.Validate(request);
        if (!validation.IsValid)
        {
            await SendResponseAsync(
                pipe,
                Failure(request, validation.Code, validation.Message),
                requestToken);
            return;
        }

        IpcResponse response;
        try
        {
            response = await DispatchAsync(request, clientIdentity.UserSid, requestToken);
        }
        catch (ZapretStrategyParseException exception)
        {
            response = Failure(request, exception.Code, exception.Message);
        }
        catch (ZapretProcessRunnerException exception)
        {
            response = Failure(request, exception.Code, exception.Message);
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
                "Не удалось безопасно прочитать состояние движка Zapret.");
        }

        await SendResponseAsync(pipe, response, requestToken);
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
                ReconcileOwner(status);

                var canSeeStrategy = status.State == ZapretProcessState.Running
                    && string.Equals(_zapretOwnerSid, clientSid, StringComparison.Ordinal);

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    ZapretStatus: ToIpcStatus(status, canSeeStrategy));
            }

            case IpcCommand.ZapretStartStrategy:
            {
                if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStart))
                {
                    return Failure(request, "OperationDenied", "Запуск движка запрещён политикой безопасности.");
                }

                var current = await zapretController.GetStatusAsync(cancellationToken);
                ReconcileOwner(current);

                if (current.State == ZapretProcessState.Running
                    && !string.Equals(_zapretOwnerSid, clientSid, StringComparison.Ordinal))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Запущенный экземпляр Zapret принадлежит другой пользовательской сессии.");
                }

                var started = await zapretController.StartAsync(request.StrategyId!, cancellationToken);
                _zapretOwnerSid ??= clientSid;

                return new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    "Zapret запущен.",
                    ZapretStatus: ToIpcStatus(started, includeStrategy: true));
            }

            case IpcCommand.ZapretStop:
            {
                if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStop))
                {
                    return Failure(request, "OperationDenied", "Остановка движка запрещена политикой безопасности.");
                }

                var current = await zapretController.GetStatusAsync(cancellationToken);
                ReconcileOwner(current);

                if (current.State == ZapretProcessState.Running
                    && !string.Equals(_zapretOwnerSid, clientSid, StringComparison.Ordinal))
                {
                    return Failure(
                        request,
                        "EngineOwnedByAnotherUser",
                        "Нельзя остановить экземпляр Zapret, запущенный другой пользовательской сессией.");
                }

                var stopped = await zapretController.StopAsync(cancellationToken);
                _zapretOwnerSid = null;

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

    private void ReconcileOwner(ZapretProcessSnapshot status)
    {
        if (status.State == ZapretProcessState.Stopped)
        {
            _zapretOwnerSid = null;
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

    private static Task SendResponseAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken) =>
        IpcFraming.WriteAsync(stream, response, cancellationToken);
}
