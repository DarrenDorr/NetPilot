using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ZapretProcessRunnerTests
{
    [Fact]
    public async Task Same_strategy_start_is_idempotent()
    {
        var process = new FakeManagedProcess(1234);
        var launcher = new FakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);
        var plan = CreatePlan("ALT11");

        var first = await runner.StartAsync(plan);
        var second = await runner.StartAsync(plan);

        Assert.Equal(1, launcher.LaunchCount);
        Assert.Equal(ZapretProcessState.Running, first.State);
        Assert.Equal(1234, first.ProcessId);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Different_strategy_requires_controlled_stop()
    {
        var process = new FakeManagedProcess(1234);
        var launcher = new FakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);

        await runner.StartAsync(CreatePlan("ALT11"));

        var exception = await Assert.ThrowsAsync<ZapretProcessRunnerException>(
            () => runner.StartAsync(CreatePlan("ALT12")));

        Assert.Equal("AlreadyRunning", exception.Code);
        Assert.Equal(1, launcher.LaunchCount);
    }

    [Fact]
    public async Task Stop_kills_only_runner_owned_process_tree()
    {
        var process = new FakeManagedProcess(4321);
        var launcher = new FakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);

        await runner.StartAsync(CreatePlan("ALT11"));
        var stopped = await runner.StopAsync();

        Assert.True(process.KillCalled);
        Assert.True(process.WaitCalled);
        Assert.True(process.Disposed);
        Assert.Equal(ZapretProcessState.Stopped, stopped.State);
        Assert.Null(stopped.ProcessId);
    }

    [Fact]
    public async Task Process_that_exits_immediately_is_not_kept_as_running()
    {
        var process = new FakeManagedProcess(2222) { HasExitedValue = true };
        var launcher = new FakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);

        var exception = await Assert.ThrowsAsync<ZapretProcessRunnerException>(
            () => runner.StartAsync(CreatePlan("ALT11")));

        Assert.Equal("ExitedImmediately", exception.Code);
        Assert.True(process.Disposed);
        var status = await runner.GetStatusAsync();
        Assert.Equal(ZapretProcessState.Stopped, status.State);
    }

    [Fact]
    public async Task Exited_owned_process_is_cleared_on_status_check()
    {
        var process = new FakeManagedProcess(9876);
        var launcher = new FakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);

        await runner.StartAsync(CreatePlan("ALT11"));
        process.HasExitedValue = true;

        var status = await runner.GetStatusAsync();

        Assert.Equal(ZapretProcessState.Stopped, status.State);
        Assert.True(process.Disposed);
        Assert.False(process.KillCalled);
    }

    private static ZapretLaunchPlan CreatePlan(string strategyId) =>
        new(strategyId, @"C:\Program Files\NetPilot\Engine\bin\winws.exe", @"C:\Program Files\NetPilot\Engine\bin", ["--wf-tcp=443"]);

    private sealed class FakeLauncher(FakeManagedProcess process) : IZapretProcessLauncher
    {
        public int LaunchCount { get; private set; }

        public IZapretManagedProcess Launch(ZapretLaunchPlan plan)
        {
            LaunchCount++;
            return process;
        }
    }

    private sealed class FakeManagedProcess(int id) : IZapretManagedProcess
    {
        public int Id { get; } = id;
        public bool HasExited => HasExitedValue;
        public bool HasExitedValue { get; set; }
        public bool KillCalled { get; private set; }
        public bool WaitCalled { get; private set; }
        public bool Disposed { get; private set; }

        public void KillEntireTree()
        {
            KillCalled = true;
            HasExitedValue = true;
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCalled = true;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
