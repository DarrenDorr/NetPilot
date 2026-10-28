using System.IO.Pipes;
using System.Security.Principal;
using NetPilot.Ipc;
using NetPilot.Security;

namespace NetPilot.Platform.Windows;

public sealed class WindowsPrivilegedServiceClient : IPrivilegedServiceClient
{
    private static readonly WindowsNamedPipePeerAuthenticator PeerAuthenticator = new();

    public Task<IpcResponse> PingAsync(CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreatePing(), cancellationToken);

    public Task<IpcResponse> RequestOperationAsync(
        PrivilegedOperation operation,
        CancellationToken cancellationToken = default) =>
        SendAsync(IpcRequest.CreateOperation(operation), cancellationToken);

    private static async Task<IpcResponse> SendAsync(
        IpcRequest request,
        CancellationToken cancellationToken)
    {
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

            return response;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IpcProtocolException(
                "RequestTimeout",
                "Служба NetPilot не ответила за допустимое время.",
                exception);
        }
    }
}
