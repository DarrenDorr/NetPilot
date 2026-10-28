using System.Text;
using NetPilot.Ipc;

namespace NetPilot.Core.Tests;

public sealed class IpcProtocolTests
{
    [Fact]
    public void Ping_without_payload_is_valid()
    {
        var result = IpcRequestValidator.Validate(IpcRequest.CreatePing());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Enrollment_accepts_only_fixed_hex_nonce()
    {
        var valid = IpcRequestValidator.Validate(
            IpcRequest.CreateEnrollment(new string('A', IpcProtocol.EnrollmentNonceCharacters)));
        var shortNonce = IpcRequestValidator.Validate(
            IpcRequest.CreateEnrollment("AA"));
        var nonHex = IpcRequestValidator.Validate(
            IpcRequest.CreateEnrollment(new string('Z', IpcProtocol.EnrollmentNonceCharacters)));

        Assert.True(valid.IsValid);
        Assert.False(shortNonce.IsValid);
        Assert.False(nonHex.IsValid);
    }

    [Fact]
    public void Install_latest_has_no_user_controlled_url_path_or_strategy_payload()
    {
        var valid = IpcRequestValidator.Validate(IpcRequest.CreateZapretInstallLatest());
        var invalid = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.ZapretInstallLatest,
            StrategyId: "ALT11");

        Assert.True(valid.IsValid);
        var rejected = IpcRequestValidator.Validate(invalid);
        Assert.False(rejected.IsValid);
        Assert.Equal("UnexpectedStrategyId", rejected.Code);
    }

    [Fact]
    public void Installation_status_request_has_no_payload()
    {
        var result = IpcRequestValidator.Validate(
            IpcRequest.CreateZapretGetInstallationStatus());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Zapret_start_with_canonical_strategy_id_is_valid()
    {
        var result = IpcRequestValidator.Validate(
            IpcRequest.CreateZapretStartStrategy("ALT11"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alt11")]
    [InlineData("ALT 11")]
    [InlineData("../ALT11")]
    [InlineData("ALT11.bat")]
    public void Zapret_start_rejects_noncanonical_strategy_id(string strategyId)
    {
        var result = IpcRequestValidator.Validate(
            IpcRequest.CreateZapretStartStrategy(strategyId));

        Assert.False(result.IsValid);
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
    public void Ping_with_strategy_id_is_rejected()
    {
        var request = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.Ping,
            "ALT11");

        var result = IpcRequestValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("UnexpectedStrategyId", result.Code);
    }

    [Fact]
    public void Ordinary_command_with_enrollment_nonce_is_rejected()
    {
        var request = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.Ping,
            EnrollmentNonce: new string('A', IpcProtocol.EnrollmentNonceCharacters));

        var result = IpcRequestValidator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Equal("UnexpectedEnrollmentNonce", result.Code);
    }

    [Fact]
    public async Task Framing_round_trip_preserves_typed_request()
    {
        var request = IpcRequest.CreateZapretStartStrategy("ALT11");
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
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":0,"strategyId":null,"enrollmentNonce":null}
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
            {"protocolVersion":{{IpcProtocol.Version}},"requestId":"{{Guid.NewGuid()}}","command":"Ping","strategyId":null,"enrollmentNonce":null,"unexpected":"value"}
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
