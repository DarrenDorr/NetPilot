using System.IO.Pipes;
using System.Security.Principal;
using NetPilot.Ipc;
using NetPilot.Platform.Windows.Diagnostics;

namespace NetPilot.Platform.Windows;

public sealed class WindowsPrivilegedServiceClient : IPrivilegedServiceClient
{
    private static readonly WindowsNamedPipePeerAuthenticator PeerAuthenticator = new();

    public Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreatePing(), cancellationToken);

    public Task<IpcResponse> ListZapretStrategiesAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreateZapretListStrategies(), cancellationToken);

    public Task<IpcResponse> GetZapretStatusAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreateZapretGetStatus(), cancellationToken);

    public Task<IpcResponse> StartZapretStrategyAsync(
        string strategyId,
        CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreateZapretStartStrategy(strategyId), cancellationToken);

    public Task<IpcResponse> StopZapretAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreateZapretStop(), cancellationToken);

    private static async Task<IpcResponse> SendAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = request.RequestId.ToString("N");
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Command"] = request.Command.ToString()
        };

        SafeDiagnosticLog.WriteUser(
            "app",
            NetPilotDiagnosticLevel.Information,
            "NP-IPC-REQ-001",
            "Запрос к локальной службе NetPilot начат.",
            correlationId,
            properties: properties);

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(IpcProtocol.RequestTimeoutMilliseconds);
        var requestToken = requestTimeout.Token;

        using var pipe = new NamedPipeClientStream(
            ".",
            IpcProtocol.WindowsPipeName,
            PipeAccessRights.ReadWrite | PipeAccessRights.ReadPermissions,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification,
            HandleInheritability.None);

        try
        {
            await pipe.ConnectAsync(IpcProtocol.ConnectTimeoutMilliseconds, requestToken);

            var serverAuthentication = PeerAuthenticator.AuthenticateServer(pipe);
            if (!serverAuthentication.IsAuthenticated)
            {
                throw new IpcProtocolException(
                    "ServerAuthenticationFailed",
                    $"Проверка NetPilot.Service не пройдена: {serverAuthentication.Code}.");
            }

            await IpcFraming.WriteAsync(pipe, request, requestToken);

            var response = await IpcFraming.ReadAsync<IpcResponse>(pipe, requestToken);
            if (response.ProtocolVersion != IpcProtocol.Version || response.RequestId != request.RequestId)
            {
                throw new IpcProtocolException(
                    "InvalidResponse",
                    "Ответ службы NetPilot не соответствует отправленному запросу.");
            }

            var responseProperties = new Dictionary<string, string>(properties, StringComparer.Ordinal)
            {
                ["ResultCode"] = response.Code
            };

            SafeDiagnosticLog.WriteUser(
                "app",
                response.Success ? NetPilotDiagnosticLevel.Information : NetPilotDiagnosticLevel.Warning,
                response.Success ? "NP-IPC-OK-001" : "NP-IPC-FAIL-001",
                response.Success
                    ? "Локальная служба NetPilot успешно обработала запрос."
                    : "Локальная служба NetPilot вернула диагностический отказ.",
                correlationId,
                properties: responseProperties);

            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var wrapped = new IpcProtocolException(
                "RequestTimeout",
                "Служба NetPilot не ответила за допустимое время.",
                exception);
            LogFailure(request.Command, correlationId, wrapped);
            throw wrapped;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogFailure(request.Command, correlationId, exception);
            throw;
        }
    }

    private static void LogFailure(IpcCommand command, string correlationId, Exception exception)
    {
        var code = exception is IpcProtocolException protocolException
            ? protocolException.Code
            : "UnhandledClientError";

        SafeDiagnosticLog.WriteUser(
            "app",
            NetPilotDiagnosticLevel.Error,
            "NP-IPC-ERR-001",
            "Не удалось безопасно выполнить запрос к локальной службе NetPilot.",
            correlationId,
            exception,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Command"] = command.ToString(),
                ["ResultCode"] = code
            });
    }
}
