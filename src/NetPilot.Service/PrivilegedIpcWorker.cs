using System.IO.Pipes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;
using NetPilot.Security;

namespace NetPilot.Service;

public sealed class PrivilegedIpcWorker(
    WindowsNamedPipeServerFactory pipeFactory,
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
        if (!IsIdentifiedLocalClient(pipe))
        {
            logger.LogWarning("IPC-соединение отклонено: клиент не прошёл локальную идентификацию.");
            return;
        }

        IpcRequest request;
        try
        {
            request = await IpcFraming.ReadAsync<IpcRequest>(pipe, cancellationToken);
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
                cancellationToken);
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
                    "Служба NetPilot доступна."),
                cancellationToken);
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
                cancellationToken);
            return;
        }

        // В R1 привилегированные действия намеренно не выполняются.
        await SendResponseAsync(
            pipe,
            new IpcResponse(
                IpcProtocol.Version,
                request.RequestId,
                false,
                "OperationNotEnabled",
                "Привилегированные операции будут подключаться после проверки IPC-границы."),
            cancellationToken);
    }

    private static bool IsIdentifiedLocalClient(NamedPipeServerStream pipe)
    {
        try
        {
            var identity = pipe.GetImpersonationUserName();
            return !string.IsNullOrWhiteSpace(identity)
                && !identity.EndsWith(
                    "\\ANONYMOUS LOGON",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static Task SendResponseAsync(
        Stream stream,
        IpcResponse response,
        CancellationToken cancellationToken) =>
        IpcFraming.WriteAsync(stream, response, cancellationToken);
}
