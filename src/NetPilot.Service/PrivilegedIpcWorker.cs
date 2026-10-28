using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;
using NetPilot.Security;

namespace NetPilot.Service;

public sealed class PrivilegedIpcWorker(
    WindowsNamedPipeServerFactory pipeFactory,
    WindowsNamedPipePeerAuthenticator peerAuthenticator,
    OperationPolicy operationPolicy,
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
                new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    false,
                    validation.Code,
                    validation.Message),
                requestToken);
            return;
        }

        if (request.Command == IpcCommand.Ping)
        {
            await SendResponseAsync(
                pipe,
                new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    true,
                    "Ok",
                    "Служба NetPilot доступна и IPC-клиент авторизован."),
                requestToken);
            return;
        }

        if (request.Operation is not { } operation || !operationPolicy.IsAllowed(operation))
        {
            await SendResponseAsync(
                pipe,
                new IpcResponse(
                    IpcProtocol.Version,
                    request.RequestId,
                    false,
                    "OperationDenied",
                    "Операция не разрешена политикой безопасности."),
                requestToken);
            return;
        }

        // В R2 идентичность клиента уже установлена из Windows-токена и не берётся из JSON.
        // Реальные привилегированные обработчики остаются отключены до отдельного аудита каждой операции.
        await SendResponseAsync(
            pipe,
            new IpcResponse(
                IpcProtocol.Version,
                request.RequestId,
                false,
                "OperationNotEnabled",
                "Привилегированные операции будут подключаться только после отдельного аудита каждой операции."),
            requestToken);
    }

    private static Task SendResponseAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken) =>
        IpcFraming.WriteAsync(stream, response, cancellationToken);
}
