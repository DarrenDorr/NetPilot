using NetPilot.Automation;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class NoAdmittedEngineProfileRuntimeTests
{
    [Fact]
    public async Task Runtime_is_inactive_and_exposes_no_profiles_or_capabilities()
    {
        var runtime = new NoAdmittedEngineProfileRuntime();

        var profiles = await runtime.ListProfilesAsync(TestContext.Current.CancellationToken);
        var state = await runtime.GetProfileStateAsync(TestContext.Current.CancellationToken);

        Assert.Equal("no-admitted-engine", runtime.EngineId);
        Assert.Equal(EngineTrafficCapability.None, runtime.Capabilities.Value);
        Assert.Empty(profiles);
        Assert.False(state.IsActive);
        Assert.Null(state.ActiveProfileId);
    }

    [Fact]
    public async Task Activation_is_always_rejected_fail_closed()
    {
        var runtime = new NoAdmittedEngineProfileRuntime();
        var profile = EngineProfileId.Parse("standard");

        var exception = await Assert.ThrowsAsync<EngineProfileRuntimeException>(
            async () => await runtime.ActivateProfileAsync(
                profile,
                TestContext.Current.CancellationToken));

        Assert.Equal("NoAdmittedEngine", exception.Code);
    }

    [Fact]
    public void Runtime_has_no_process_path_or_transport_fields()
    {
        var fields = typeof(NoAdmittedEngineProfileRuntime)
            .GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

        Assert.Empty(fields);
    }
}
