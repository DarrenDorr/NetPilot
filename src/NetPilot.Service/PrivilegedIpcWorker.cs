using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;
using NetPilot.Security;

namespace NetPilot.Service;

public sealed class PrivilegedIpcWorker(
    WindowsNamedPipeServerFactory pipeFactory,
    WindowsNamedPipePeerAuthenticator peerAuthenticator,
    OperationPolicy operationPolicy,
    ZapretServiceController zapretController,
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
                Failure(request, validation.Code, validation.Message ?? "Запрос не прошёл проверку."),
                requestToken);
            return;
        }

        var response = await DispatchAsync(request, requestToken);
        await SendResponseAsync(pipe, response, requestToken);
    }

    private async Task<IpcResponse> DispatchAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return request.Command switch
            {
                IpcCommand.Ping => Success(
                    request,
                    "Ok",
                    "Служба NetPilot доступна и IPC-клиент авторизован."),

                IpcCommand.ExecutePrivilegedOperation => HandleLegacyPrivilegedOperation(request),

                IpcCommand.ZapretListStrategies => await HandleZapretListStrategiesAsync(request, cancellationToken),
                IpcCommand.ZapretGetStatus => await HandleZapretGetStatusAsync(request, cancellationToken),
                IpcCommand.ZapretStart => await HandleZapretStartAsync(request, cancellationToken),
                IpcCommand.ZapretStop => await HandleZapretStopAsync(request, cancellationToken),

                _ => Failure(request, "UnknownCommand", "Команда IPC не поддерживается.")
            };
        }
        catch (ZapretServiceException exception)
        {
            logger.LogWarning(
                "Операция Zapret отклонена. Код: {Code}. Детали конфигурации не журналируются.",
                exception.Code);
            return Failure(request, exception.Code, exception.Message);
        }
        catch (ZapretProcessRunnerException exception)
        {
            logger.LogWarning(
                "Операция процесса Zapret завершилась отказом. Код: {Code}.",
                exception.Code);
            return Failure(request, exception.Code, exception.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Внутренняя ошибка обработки типизированной IPC-команды. Параметры запроса не журналируются.");
            return Failure(
                request,
                "InternalError",
                "Внутренняя ошибка NetPilot.Service. Подробности не передаются клиенту.");
        }
    }

    private IpcResponse HandleLegacyPrivilegedOperation(IpcRequest request)
    {
        if (request.Operation is not { } operation || !operationPolicy.IsAllowed(operation))
        {
            return Failure(
                request,
                "OperationDenied",
                "Операция не разрешена политикой безопасности.");
        }

        return Failure(
            request,
            "OperationNotEnabled",
            "Общие привилегированные операции не имеют универсального обработчика.");
    }

    private async Task<IpcResponse> HandleZapretListStrategiesAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        var strategies = await zapretController.ListStrategiesAsync(cancellationToken);
        return Success(
            request,
            "Ok",
            "Каталог стратегий Zapret получен.",
            new ZapretIpcPayload(Strategies: strategies));
    }

    private async Task<IpcResponse> HandleZapretGetStatusAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await zapretController.GetStatusAsync(cancellationToken);
        return Success(
            request,
            "Ok",
            "Состояние Zapret получено.",
            ToPayload(snapshot));
    }

    private async Task<IpcResponse> HandleZapretStartAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStart))
        {
            return Failure(
                request,
                "OperationDenied",
                "Запуск Zapret пока не разрешён политикой безопасности NetPilot.");
        }

        var strategyId = request.ZapretStart?.StrategyId
            ?? throw new InvalidOperationException("Валидатор пропустил ZapretStart без StrategyId.");

        var snapshot = await zapretController.StartAsync(strategyId, cancellationToken);
        return Success(
            request,
            "Started",
            "Zapret запущен.",
            ToPayload(snapshot));
    }

    private async Task<IpcResponse> HandleZapretStopAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        if (!operationPolicy.IsAllowed(PrivilegedOperation.EngineStop))
        {
            return Failure(
                request,
                "OperationDenied",
                "Остановка Zapret пока не разрешена политикой безопасности NetPilot.");
        }

        var snapshot = await zapretController.StopAsync(cancellationToken);
        return Success(
            request,
            "Stopped",
            "Zapret остановлен.",
            ToPayload(snapshot));
    }

    private static ZapretIpcPayload ToPayload(ZapretProcessSnapshot snapshot) =>
        new(
            State: snapshot.State == ZapretProcessState.Running
                ? ZapretIpcState.Running
                : ZapretIpcState.Stopped,
            ProcessId: snapshot.ProcessId,
            StrategyId: snapshot.StrategyId);

    private static IpcResponse Success(
        IpcRequest request,
        string code,
        string message,
        ZapretIpcPayload? payload = null) =>
        new(IpcProtocol.Version, request.RequestId, true, code, message, payload);

    private static IpcResponse Failure(
        IpcRequest request,
        string code,
        string message) =>
        new(IpcProtocol.Version, request.RequestId, false, code, message);

    private static Task SendResponseAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken) =>
        IpcFraming.WriteAsync(stream, response, cancellationToken);
}
