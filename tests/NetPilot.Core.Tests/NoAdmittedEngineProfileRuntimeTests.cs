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
    public void Runtime_does_not_expose_trusted_backend_declaration()
    {
        var runtime = new NoAdmittedEngineProfileRuntime();

        Assert.IsAssignableFrom<IEngineCapabilityProvider>(runtime);
        Assert.False(runtime is IEngineBackendCapabilityProvider);
    }

    [Fact]
    public async Task DenyAllAdapter_keeps_no_admitted_runtime_empty_and_stopped()
    {
        var runtime = new NoAdmittedEngineProfileRuntime();
        var adapter = new EngineProfileStrategyRuntimeAdapter(
            runtime,
            DenyAllEngineRuntimeAdmissionPolicy.Instance);

        var strategies = await adapter.ListStrategiesAsync(
            TestContext.Current.CancellationToken);
        var state = await adapter.GetStateAsync(
            TestContext.Current.CancellationToken);

        Assert.Empty(strategies);
        Assert.False(state.IsRunning);
        Assert.Null(state.StrategyId);
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
