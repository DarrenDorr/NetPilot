using NetPilot.Core.Models;
using NetPilot.Health;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ByeDpiProxyResourceHealthProbeTests
{
    [Fact]
    public async Task CanonicalBuiltInResource_WhenRuntimeStopped_FailsOnlyAsNotRunning()
    {
        using var runtime = CreateStoppedRuntime();
        var probe = new ByeDpiProxyResourceHealthProbe(runtime);

        var health = await probe.CheckAsync(BuiltInResourceCatalog.Discord);

        Assert.Equal(ResourceHealthState.Unavailable, health.State);
        Assert.Equal("ByeDpiNotRunning", health.DiagnosticCode);
    }

    [Fact]
    public async Task SameResourceId_WithUnapprovedEndpoint_IsRejectedBeforeNetworkUse()
    {
        using var runtime = CreateStoppedRuntime();
        var probe = new ByeDpiProxyResourceHealthProbe(runtime);
        var injected = new ResourceDefinition(
            BuiltInResourceCatalog.Discord.Id,
            BuiltInResourceCatalog.Discord.DisplayName,
            [new Uri("https://example.com/")],
            []);

        var health = await probe.CheckAsync(injected);

        Assert.Equal(ResourceHealthState.Unavailable, health.State);
        Assert.Equal("EndpointRejected", health.DiagnosticCode);
    }

    private static WindowsByeDpiStrategyRuntime CreateStoppedRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "NetPilot-Test-ByeDPI", Guid.NewGuid().ToString("N"));
        return new WindowsByeDpiStrategyRuntime(new ByeDpiUserModeOptions
        {
            PackageRoot = root,
            ExecutablePath = Path.Combine(root, "bin", "ciadpi.exe")
        });
    }
}
