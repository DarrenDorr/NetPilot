using System.Text;
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

        await IpcFraming.WriteAsync(stream, request, TestContext.Current.CancellationToken);
        stream.Position = 0;

        var restored = await IpcFraming.ReadAsync<IpcRequest>(
            stream,
            TestContext.Current.CancellationToken);

        Assert.Equal(request, restored);
    }

    [Fact]
    public async Task Oversized_frame_is_rejected_before_allocation()
    {
        await using var stream = new MemoryStream();
        var invalidLength = BitConverter.GetBytes(IpcProtocol.MaxMessageBytes + 1);
        await stream.WriteAsync(invalidLength, TestContext.Current.CancellationToken);
        stream.Position = 0;

        var exception = await Assert.ThrowsAsync<IpcProtocolException>(
            async () => await IpcFraming.ReadAsync<IpcRequest>(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Equal("InvalidMessageLength", exception.Code);
    }

    [Fact]
    public async Task Numeric_enum_values_are_rejected()
    {
        var json = $$"""
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":0,"operation":null}
            """;

        await using var stream = CreateFramedJson(json);

        var exception = await Assert.ThrowsAsync<IpcProtocolException>(
            async () => await IpcFraming.ReadAsync<IpcRequest>(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Equal("InvalidJson", exception.Code);
    }

    [Fact]
    public async Task Unknown_json_fields_are_rejected()
    {
        var json = $$"""
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":"Ping","operation":null,"unexpected":"value"}
            """;

        await using var stream = CreateFramedJson(json);

        var exception = await Assert.ThrowsAsync<IpcProtocolException>(
            async () => await IpcFraming.ReadAsync<IpcRequest>(
                stream,
                TestContext.Current.CancellationToken));

        Assert.Equal("InvalidJson", exception.Code);
    }

    private static MemoryStream CreateFramedJson(string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        stream.Write(BitConverter.GetBytes(payload.Length));
        stream.Write(payload);
        stream.Position = 0;
        return stream;
    }
}
