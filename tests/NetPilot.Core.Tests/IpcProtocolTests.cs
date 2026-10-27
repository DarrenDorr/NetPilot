using NetPilot.Ipc;
using NetPilot.Security;

namespace NetPilot.Core.Tests;

public sealed class IpcProtocolTests
{
    [Fact]
    public void Ping_without_operation_is_valid()
    {
        var result = IpcRequestValidator.Validate(IpcRequest.CreatePing());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Unsupported_protocol_version_is_rejected()
    {
        var request = new IpcRequest(
            IpcProtocol.Version + 1,
            Guid.NewGuid(),
            IpcCommand.Ping,
            null);

        var result = IpcRequestValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("UnsupportedProtocolVersion", result.Code);
    }

    [Fact]
    public void Ping_with_privileged_operation_is_rejected()
    {
        var request = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.Ping,
            PrivilegedOperation.EngineStart);

        var result = IpcRequestValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("UnexpectedOperation", result.Code);
    }

    [Fact]
    public async Task Framing_round_trip_preserves_request()
    {
        var request = IpcRequest.CreateOperation(PrivilegedOperation.EngineStart);
        await using var stream = new MemoryStream();

        await IpcFraming.WriteAsync(stream, request);
        stream.Position = 0;

        var restored = await IpcFraming.ReadAsync<IpcRequest>(stream);

        Assert.Equal(request, restored);
    }

    [Fact]
    public async Task Oversized_frame_is_rejected_before_allocation()
    {
        await using var stream = new MemoryStream();
        var invalidLength = BitConverter.GetBytes(IpcProtocol.MaxMessageBytes + 1);
        await stream.WriteAsync(invalidLength);
        stream.Position = 0;

        var exception = await Assert.ThrowsAsync<IpcProtocolException>(
            async () => await IpcFraming.ReadAsync<IpcRequest>(stream));

        Assert.Equal("InvalidMessageLength", exception.Code);
    }
}
