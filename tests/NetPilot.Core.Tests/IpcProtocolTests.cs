using System.Reflection;
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
            IpcCommand.Ping);

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
        Assert.Equal("UnexpectedPayload", result.Code);
    }

    [Theory]
    [InlineData("ALT11", true)]
    [InlineData("SIMPLE_FAKE", true)]
    [InlineData("FAKE_TLS_AUTO", true)]
    [InlineData("../ALT11", false)]
    [InlineData("C:\\temp\\winws.exe", false)]
    [InlineData("alt11", false)]
    [InlineData("ALT11 --new", false)]
    [InlineData("", false)]
    public void Zapret_start_strategy_id_is_strictly_validated(string strategyId, bool expected)
    {
        var result = IpcRequestValidator.Validate(IpcRequest.CreateZapretStart(strategyId));

        Assert.Equal(expected, result.IsValid);
        if (!expected)
        {
            Assert.Equal("InvalidStrategyId", result.Code);
        }
    }

    [Fact]
    public void Zapret_read_only_commands_do_not_accept_payloads()
    {
        var request = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.ZapretGetStatus,
            ZapretStart: new ZapretStartRequest("ALT11"));

        var result = IpcRequestValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("UnexpectedPayload", result.Code);
    }

    [Fact]
    public void Zapret_start_contract_does_not_expose_raw_execution_fields()
    {
        var propertyNames = typeof(ZapretStartRequest)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => property.Name)
            .ToArray();

        Assert.Equal(["StrategyId"], propertyNames);

        var forbiddenNames = new[]
        {
            "ExecutablePath",
            "Arguments",
            "ArgumentList",
            "WorkingDirectory",
            "ProcessId",
            "Pid",
            "Command",
            "BatContent",
            "EngineRoot"
        };

        Assert.DoesNotContain(propertyNames, name =>
            forbiddenNames.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Framing_round_trip_preserves_request()
    {
        var request = IpcRequest.CreateZapretStart("ALT11");
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
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":0,"operation":null,"zapretStart":null}
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
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":"Ping","operation":null,"zapretStart":null,"unexpected":"value"}
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
