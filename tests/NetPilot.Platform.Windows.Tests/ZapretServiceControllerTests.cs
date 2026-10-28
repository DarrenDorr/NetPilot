using NetPilot.Engine.Zapret;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Service;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ZapretServiceControllerTests
{
    [Fact]
    public async Task Production_default_is_fail_closed_without_registered_installation()
    {
        await using var runner = new ZapretProcessRunner(new FakeLauncher(new FakeManagedProcess(1001)));
        var controller = new ZapretServiceController(
            new UnconfiguredZapretInstallationLocator(),
            new UnconfiguredZapretRuntimeConfigurationProvider(),
            runner);

        var exception = await Assert.ThrowsAsync<ZapretServiceException>(
            () => controller.ListStrategiesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("EngineNotInstalled", exception.Code);
    }

    [Fact]
    public async Task Lists_only_valid_parsed_strategies_from_service_side_root()
    {
        using var fixture = new ServiceEngineFixture();
        fixture.CreateStrategy("general (ALT11).bat", """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """);
        fixture.CreateStrategy("general (ALT12).bat", """
            @echo off
            rem no winws here
            """);

        await using var runner = new ZapretProcessRunner(new FakeLauncher(new FakeManagedProcess(1002)));
        var controller = CreateController(fixture, runner);

        var strategies = await controller.ListStrategiesAsync(TestContext.Current.CancellationToken);

        var strategy = Assert.Single(strategies);
        Assert.Equal("ALT11", strategy.Id);
        Assert.Equal("ALT11", strategy.DisplayName);
    }

    [Fact]
    public async Task Duplicate_normalized_strategy_ids_fail_closed()
    {
        using var fixture = new ServiceEngineFixture();
        fixture.CreateStrategy("general (A B).bat", """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """);
        fixture.CreateStrategy("general (A-B).bat", """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """);

        await using var runner = new ZapretProcessRunner(new FakeLauncher(new FakeManagedProcess(1003)));
        var controller = CreateController(fixture, runner);

        var exception = await Assert.ThrowsAsync<ZapretServiceException>(
            () => controller.ListStrategiesAsync(TestContext.Current.CancellationToken));

        Assert.Equal("DuplicateStrategyId", exception.Code);
    }

    [Fact]
    public async Task Start_resolves_strategy_and_paths_only_on_service_side()
    {
        using var fixture = new ServiceEngineFixture();
        fixture.CreateStrategy("general (ALT11).bat", """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=80,%GameFilterTCP% --wf-udp=443,%GameFilterUDP%
            """);

        var process = new FakeManagedProcess(2222);
        var launcher = new CapturingFakeLauncher(process);
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = CreateController(fixture, runner);

        var status = await controller.StartAsync("ALT11", TestContext.Current.CancellationToken);

        Assert.Equal(ZapretProcessState.Running, status.State);
        Assert.NotNull(launcher.CapturedPlan);
        Assert.Equal(Path.Combine(fixture.Bin, "winws.exe"), launcher.CapturedPlan!.ExecutablePath);
        Assert.Equal(fixture.Bin, launcher.CapturedPlan.WorkingDirectory);
        Assert.Equal("--wf-tcp=80,1024-65535", launcher.CapturedPlan.ArgumentList[0]);
        Assert.Equal("--wf-udp=443,1024-65535", launcher.CapturedPlan.ArgumentList[1]);
    }

    [Fact]
    public async Task Unknown_strategy_id_cannot_be_used_as_path()
    {
        using var fixture = new ServiceEngineFixture();
        fixture.CreateStrategy("general (ALT11).bat", """
            start "zapret" /min "%BIN%winws.exe" --wf-tcp=443
            """);

        var launcher = new CapturingFakeLauncher(new FakeManagedProcess(3333));
        await using var runner = new ZapretProcessRunner(launcher);
        var controller = CreateController(fixture, runner);

        var exception = await Assert.ThrowsAsync<ZapretServiceException>(
            () => controller.StartAsync("..\\bin\\winws.exe", TestContext.Current.CancellationToken));

        Assert.Equal("StrategyNotFound", exception.Code);
        Assert.Null(launcher.CapturedPlan);
    }

    private static ZapretServiceController CreateController(
        ServiceEngineFixture fixture,
        ZapretProcessRunner runner) =>
        new(
            new FixedInstallationLocator(fixture.Root),
            new FixedRuntimeConfigurationProvider(new ZapretRuntimeVariables("1024-65535", "1024-65535")),
            runner);

    private sealed class FixedInstallationLocator(string root) : IZapretInstallationLocator
    {
        public string? TryGetEngineRoot() => root;
    }

    private sealed class FixedRuntimeConfigurationProvider(ZapretRuntimeVariables variables)
        : IZapretRuntimeConfigurationProvider
    {
        public ZapretRuntimeVariables? TryGetRuntimeVariables() => variables;
    }

    private class FakeLauncher(IZapretManagedProcess process) : IZapretProcessLauncher
    {
        public virtual IZapretManagedProcess Launch(ZapretLaunchPlan plan) => process;
    }

    private sealed class CapturingFakeLauncher(IZapretManagedProcess process) : FakeLauncher(process)
    {
        public ZapretLaunchPlan? CapturedPlan { get; private set; }

        public override IZapretManagedProcess Launch(ZapretLaunchPlan plan)
        {
            CapturedPlan = plan;
            return base.Launch(plan);
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

    private sealed class ServiceEngineFixture : IDisposable
    {
        public ServiceEngineFixture()
        {
            Root = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), $"NetPilot-Service-Zapret-{Guid.NewGuid():N}")).FullName;
            Bin = Directory.CreateDirectory(Path.Combine(Root, "bin")).FullName;
            Lists = Directory.CreateDirectory(Path.Combine(Root, "lists")).FullName;
            File.WriteAllBytes(Path.Combine(Bin, "winws.exe"), [0x4D, 0x5A]);
        }

        public string Root { get; }
        public string Bin { get; }
        public string Lists { get; }

        public void CreateStrategy(string fileName, string source) =>
            File.WriteAllText(Path.Combine(Root, fileName), source);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
