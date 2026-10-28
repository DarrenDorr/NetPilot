using NetPilot.Ipc;

namespace NetPilot.Core.Tests;

public sealed class IpcProvisioningTests
{
    [Fact]
    public void Official_provisioning_request_has_no_user_payload()
    {
        var request = IpcRequest.CreateZapretInstallOrUpdateOfficialLatest();
        var validation = IpcRequestValidator.Validate(request);

        Assert.True(validation.IsValid);
        Assert.Null(request.StrategyId);
        Assert.Equal(IpcCommand.ZapretInstallOrUpdateOfficialLatest, request.Command);
    }

    [Fact]
    public void Provisioning_rejects_injected_strategy_or_other_string_payload()
    {
        var request = new IpcRequest(
            IpcProtocol.Version,
            Guid.NewGuid(),
            IpcCommand.ZapretInstallOrUpdateOfficialLatest,
            "https://example.invalid/payload.zip");

        var validation = IpcRequestValidator.Validate(request);

        Assert.False(validation.IsValid);
        Assert.Equal("UnexpectedStrategyId", validation.Code);
    }

    [Fact]
    public void Only_provisioning_command_gets_extended_timeout()
    {
        Assert.Equal(
            IpcProtocol.ProvisioningRequestTimeoutMilliseconds,
            IpcProtocol.GetRequestTimeoutMilliseconds(
                IpcCommand.ZapretInstallOrUpdateOfficialLatest));

        Assert.Equal(
            IpcProtocol.RequestTimeoutMilliseconds,
            IpcProtocol.GetRequestTimeoutMilliseconds(IpcCommand.Ping));
        Assert.Equal(
            IpcProtocol.RequestTimeoutMilliseconds,
            IpcProtocol.GetRequestTimeoutMilliseconds(IpcCommand.ZapretStartStrategy));
    }

    [Fact]
    public void Installation_status_query_is_read_only_and_payload_free()
    {
        var request = IpcRequest.CreateZapretGetInstallationStatus();
        var validation = IpcRequestValidator.Validate(request);

        Assert.True(validation.IsValid);
        Assert.Null(request.StrategyId);
        Assert.Equal(IpcCommand.ZapretGetInstallationStatus, request.Command);
    }
}
