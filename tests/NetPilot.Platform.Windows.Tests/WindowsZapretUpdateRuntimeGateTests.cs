using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsZapretUpdateRuntimeGateTests
{
    [Fact]
    public async Task Capture_stopped_runtime_returns_stopped_snapshot()
    {
        using var fixture = new EngineFixture();
        var launcher = new QueueLauncher();
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);
        var gate = new WindowsZapretUpdateRuntimeGate(controller);

        var state = await gate.CaptureAndStopAsync(TestContext.Current.CancellationToken);

        Assert.False(state.WasRunning);
        Assert.Null(state.StrategyId);
        Assert.Equal(0, launcher.LaunchCalls);
    }

    [Fact]
    public async Task Capture_running_runtime_remembers_exact_strategy_and_stops_process()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", ValidStrategySource);

        var firstProcess = new FakeManagedProcess(101);
        var launcher = new QueueLauncher(firstProcess);
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);
        var gate = new WindowsZapretUpdateRuntimeGate(controller);

        await controller.StartAsync("ALT11", TestContext.Current.CancellationToken);
        var state = await gate.CaptureAndStopAsync(TestContext.Current.CancellationToken);
        var status = await controller.GetStatusAsync(TestContext.Current.CancellationToken);

        Assert.True(state.WasRunning);
        Assert.Equal("ALT11", state.StrategyId);
        Assert.Equal(ZapretProcessState.Stopped, status.State);
        Assert.True(firstProcess.HasExited);
    }

    [Fact]
    public async Task Restore_starts_only_the_exact_previous_strategy()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", ValidStrategySource);
        fixture.AddStrategy("general (ALT12).bat", ValidStrategySource);

        var restoredProcess = new FakeManagedProcess(202);
        var launcher = new QueueLauncher(restoredProcess);
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);
        var gate = new WindowsZapretUpdateRuntimeGate(controller);

        var result = await gate.RestoreAsync(
            ZapretUpdateRuntimeState.Running("ALT11"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretRuntimeRestoreStatus.Restored, result.Status);
        Assert.Equal("ALT11", result.StrategyId);
        Assert.Equal("ALT11", launcher.LastPlan?.StrategyId);
        Assert.Equal(1, launcher.LaunchCalls);
    }

    [Fact]
    public async Task Missing_previous_strategy_is_reported_without_fallback_launch()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT12).bat", ValidStrategySource);

        var launcher = new QueueLauncher(new FakeManagedProcess(303));
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);
        var gate = new WindowsZapretUpdateRuntimeGate(controller);

        var result = await gate.RestoreAsync(
            ZapretUpdateRuntimeState.Running("ALT11"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretRuntimeRestoreStatus.Failed, result.Status);
        Assert.Equal("ALT11", result.StrategyId);
        Assert.Equal("StrategyUnavailableOrIncompatibleAfterUpdate", result.ErrorCode);
        Assert.Equal(0, launcher.LaunchCalls);
        Assert.Null(launcher.LastPlan);
    }

    [Fact]
    public async Task Incompatible_previous_strategy_is_reported_without_fallback_launch()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", "@echo off\r\necho incompatible\r\n");
        fixture.AddStrategy("general (ALT12).bat", ValidStrategySource);

        var launcher = new QueueLauncher(new FakeManagedProcess(404));
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);
        var gate = new WindowsZapretUpdateRuntimeGate(controller);

        var result = await gate.RestoreAsync(
            ZapretUpdateRuntimeState.Running("ALT11"),
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretRuntimeRestoreStatus.Failed, result.Status);
        Assert.Equal("StrategyUnavailableOrIncompatibleAfterUpdate", result.ErrorCode);
        Assert.Equal(0, launcher.LaunchCalls);
    }

    private const string ValidStrategySource =
        "@echo off\r\n" +
        "start \"zapret\" /min \"%BIN%winws.exe\" --wf-tcp=443,%GameFilterTCP% --wf-udp=443,%GameFilterUDP%\r\n";

    private sealed class EngineFixture : IDisposable
    {
        public EngineFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "NetPilotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(Root, "bin"));
            Directory.CreateDirectory(Path.Combine(Root, "lists"));
            File.WriteAllBytes(Path.Combine(Root, "bin", "winws.exe"), [0x4D, 0x5A]);
        }

        public string Root { get; }

        public void AddStrategy(string fileName, string source) =>
            File.WriteAllText(Path.Combine(Root, fileName), source);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class QueueLauncher(params FakeManagedProcess[] processes) : IZapretProcessLauncher
    {
        private readonly Queue<FakeManagedProcess> _processes = new(processes);

        public int LaunchCalls { get; private set; }
        public ZapretLaunchPlan? LastPlan { get; private set; }

        public IZapretManagedProcess Launch(ZapretLaunchPlan plan)
        {
            LaunchCalls++;
            LastPlan = plan;

            if (_processes.Count == 0)
            {
                throw new InvalidOperationException("No fake process configured.");
            }

            return _processes.Dequeue();
        }
    }

    private sealed class FakeManagedProcess(int id) : IZapretManagedProcess
    {
        public int Id { get; } = id;
        public bool HasExited { get; private set; }

        public void KillEntireTree() => HasExited = true;

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
