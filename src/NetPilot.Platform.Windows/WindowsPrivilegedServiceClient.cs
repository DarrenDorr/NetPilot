using System.IO.Pipes;
using System.Security.Principal;
using NetPilot.Ipc;
using NetPilot.Security;

namespace NetPilot.Platform.Windows;

public sealed class WindowsPrivilegedServiceClient : IPrivilegedServiceClient
{
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
        using var pipe = new NamedPipeClientStream(
            ".",
            IpcProtocol.WindowsPipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);

        await pipe.ConnectAsync(IpcProtocol.ConnectTimeoutMilliseconds, cancellationToken);
        await IpcFraming.WriteAsync(pipe, request, cancellationToken);

        var response = await IpcFraming.ReadAsync<IpcResponse>(pipe, cancellationToken);
        if (response.ProtocolVersion != IpcProtocol.Version || response.RequestId != request.RequestId)
        {
            throw new IpcProtocolException(
                "InvalidResponse",
                "Ответ службы NetPilot не соответствует отправленному запросу.");
        }

        return response;
    }
}
