using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ZapretEngineControllerTests
{
    [Fact]
    public async Task Start_selects_strategy_by_id_and_builds_plan_in_engine_root()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", ValidStrategySource);

        var process = new FakeManagedProcess(777);
        var launcher = new CapturingLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);

        var status = await controller.StartAsync(
            "ALT11",
            TestContext.Current.CancellationToken);

        Assert.Equal(ZapretProcessState.Running, status.State);
        Assert.NotNull(launcher.LastPlan);
        Assert.Equal("ALT11", launcher.LastPlan!.StrategyId);
        Assert.Equal(Path.Combine(fixture.Root, "bin", "winws.exe"), launcher.LastPlan.ExecutablePath);
        Assert.Contains("--wf-tcp=443,12", launcher.LastPlan.ArgumentList);
        Assert.DoesNotContain(launcher.LastPlan.ArgumentList, static argument =>
            argument.Contains(".bat", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_strategy_id_is_rejected_without_launch()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", ValidStrategySource);

        var launcher = new CapturingLauncher(new FakeManagedProcess(777));
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);

        var exception = await Assert.ThrowsAsync<ZapretProcessRunnerException>(
            () => controller.StartAsync(
                "ALT12",
                TestContext.Current.CancellationToken));

        Assert.Equal("StrategyNotFound", exception.Code);
        Assert.Null(launcher.LastPlan);
    }

    [Fact]
    public void List_strategies_skips_incompatible_official_files()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT11).bat", ValidStrategySource);
        fixture.AddStrategy("general (ALT12).bat", "@echo off\r\necho incompatible");

        using var runner = new AsyncRunnerHolder(new ZapretProcessRunner(
            new CapturingLauncher(new FakeManagedProcess(777))));
        var controller = new ZapretEngineController(fixture.Root, runner.Runner);

        var strategies = controller.ListStrategies();

        var strategy = Assert.Single(strategies);
        Assert.Equal("ALT11", strategy.Id);
    }

    [Fact]
    public async Task Duplicate_normalized_strategy_id_is_rejected()
    {
        using var fixture = new EngineFixture();
        fixture.AddStrategy("general (ALT-11).bat", ValidStrategySource);
        fixture.AddStrategy("general (ALT_11).bat", ValidStrategySource);

        var launcher = new CapturingLauncher(new FakeManagedProcess(777));
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = new ZapretEngineController(fixture.Root, runner);

        var exception = await Assert.ThrowsAsync<ZapretProcessRunnerException>(
            () => controller.StartAsync(
                "ALT_11",
                TestContext.Current.CancellationToken));

        Assert.Equal("DuplicateStrategyId", exception.Code);
        Assert.Null(launcher.LastPlan);
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

    private sealed class CapturingLauncher(FakeManagedProcess process) : IZapretProcessLauncher
    {
        public ZapretLaunchPlan? LastPlan { get; private set; }

        public IZapretManagedProcess Launch(ZapretLaunchPlan plan)
        {
            LastPlan = plan;
            return process;
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

    private sealed class AsyncRunnerHolder(ZapretProcessRunner runner) : IDisposable
    {
        public ZapretProcessRunner Runner { get; } = runner;

        public void Dispose() => Runner.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
