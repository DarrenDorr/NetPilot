using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsByeDpiRuntimeSessionTests
{
    [Fact]
    public void NoAdmittedPlanProvider_AlwaysRejects()
    {
        var exception = Assert.Throws<WindowsServiceOwnedEngineProcessException>(
            () => NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance.CreatePlan());

        Assert.Equal("NoAdmittedByeDpiPlan", exception.Code);
    }

    [Fact]
    public async Task RuntimeSession_RemainsStoppedAndStartFailsClosedWithoutAdmission()
    {
        await using var controller = new WindowsByeDpiServiceOwnedProcessController(
            NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance);
        var session = new WindowsByeDpiRuntimeSession(
            NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance,
            controller);

        var before = await session.GetStatusAsync(CancellationToken.None);
        Assert.Equal(WindowsServiceOwnedEngineProcessState.Stopped, before.State);
        Assert.Null(before.ProcessId);

        var exception = await Assert.ThrowsAsync<WindowsServiceOwnedEngineProcessException>(
            () => session.StartAsync(CancellationToken.None));
        Assert.Equal("NoAdmittedByeDpiPlan", exception.Code);

        var stopped = await session.StopAsync(CancellationToken.None);
        Assert.Equal(WindowsServiceOwnedEngineProcessState.Stopped, stopped.State);
        Assert.Null(stopped.ProcessId);
    }
}
