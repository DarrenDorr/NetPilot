using System.ComponentModel;
using Avalonia.Media;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;
using NetPilot.Automation;
using NetPilot.Core.Models;

namespace NetPilot.Platform.Windows.Tests;

public sealed class NetPilotAlphaDashboardViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initialize_OnlyShowsRunningAfterRuntimeConfirmsIt(bool isRunning)
    {
        using var dashboard = new FakeDashboardService(isRunning);
        using var viewModel = new MainWindowViewModel(dashboard);

        await viewModel.InitializeAsync();

        Assert.Equal(isRunning, viewModel.IsEngineRunning);
        Assert.Equal(
            isRunning ? "Остановить ByeDPI" : "Запустить ByeDPI",
            viewModel.EngineActionText);
        Assert.Same(
            isRunning ? Brushes.MediumSeaGreen : Brushes.SlateGray,
            viewModel.EngineStatusBrush);
        Assert.Contains(
            isRunning ? "запущен" : "остановлен",
            viewModel.EngineStatus,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            isRunning ? "127.0.0.1:37801" : "Недоступен — ByeDPI остановлен.",
            viewModel.LocalSocks5Address);
    }

    [Fact]
    public void HealthyHealthProbe_IsNotDescribedAsApplicationWorking()
    {
        var card = new ResourceStatusViewModel("example", "Example");
        card.Apply(new ResourceHealth("example", ResourceHealthState.Healthy, DateTimeOffset.UtcNow));
        Assert.Equal("HTTPS отвечает", card.StatusText);
        Assert.DoesNotContain("Работает", card.StatusText);
    }

    [Fact]
    public async Task EmptyHealthCheck_ReportsNotVerifiedInsteadOfSuccessOrDegraded()
    {
        using var dashboard = new FakeDashboardService(false);
        using var viewModel = new MainWindowViewModel(dashboard);
        viewModel.CheckCommand.Execute(null);
        for (var i = 0; i < 100 && viewModel.IsBusy; i++)
        {
            await Task.Delay(10);
        }
        Assert.Equal("Нет результатов проверки", viewModel.OverallStatus);
        Assert.Contains("не подтверждена", viewModel.OverallDetails);
    }

    [Fact]
    public void Sidebar_ContractsWithoutHidingFunctionalOperations()
    {
        using var dashboard = new FakeDashboardService(false);
        using var viewModel = new MainWindowViewModel(dashboard);
        var changed = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is { } name)
            {
                changed.Add(name);
            }
        };

        Assert.True(viewModel.IsMenuExpanded);
        Assert.Equal(228, viewModel.SidebarWidth);

        viewModel.IsMenuExpanded = false;

        Assert.Equal(76, viewModel.SidebarWidth);
        Assert.Contains(nameof(MainWindowViewModel.IsMenuExpanded), changed);
        Assert.Contains(nameof(MainWindowViewModel.SidebarWidth), changed);
        Assert.True(viewModel.ToggleEngineCommand.CanExecute(null));
        Assert.True(viewModel.CheckCommand.CanExecute(null));
        Assert.True(viewModel.CheckDirectCommand.CanExecute(null));
        Assert.Contains("SOCKS5", viewModel.HealthRouteDescription);
        Assert.True(viewModel.FindStrategyCommand.CanExecute(null));
        Assert.Equal(4, viewModel.StrategyTargets.Count);
        Assert.Equal(3, viewModel.Resources.Count);
        Assert.Equal(
            dashboard.Resources.Select(resource => resource.Id),
            viewModel.Resources.Select(resource => resource.Id));
        Assert.Null(viewModel.SelectedStrategyTarget.ResourceId);
        Assert.Contains(viewModel.StrategyTargets, target => target.ResourceId == "example");

        viewModel.IsAdvancedMode = true;
        Assert.True(viewModel.IsAdvancedMode);
    }

    [Fact]
    public async Task StrategyDiagnostics_UsesBoundedAggregatesWithoutCandidateIdentifiers()
    {
        using var dashboard = new FakeDashboardService(true);
        using var viewModel = new MainWindowViewModel(dashboard);
        viewModel.IsAdvancedMode = true;
        viewModel.FindStrategyCommand.Execute(null);
        for (var i = 0; i < 100 && !viewModel.StrategyDiagnostics.Contains("Прогонов:", StringComparison.Ordinal); i++)
        {
            await Task.Delay(10);
        }
        Assert.Contains("Прогонов: 2", viewModel.StrategyDiagnostics);
        Assert.Contains("работает: 1", viewModel.StrategyDiagnostics);
        Assert.Contains("недоступно: 1", viewModel.StrategyDiagnostics);
        Assert.Contains("Discord: OK 1", viewModel.StrategyDiagnostics);
        Assert.Contains("YouTube: OK 0", viewModel.StrategyDiagnostics);
        Assert.DoesNotContain("private-strategy", viewModel.StrategyDiagnostics);
    }

    [Theory]
    [InlineData("discord")]
    [InlineData("youtube")]
    [InlineData("example")]
    public async Task TargetedStrategyCommand_UsesSelectedCatalogResource(string resourceId)
    {
        using var dashboard = new FakeDashboardService(true);
        using var viewModel = new MainWindowViewModel(dashboard);
        viewModel.SelectedStrategyTarget = viewModel.StrategyTargets.Single(
            target => target.ResourceId == resourceId);
        viewModel.FindStrategyCommand.Execute(null);
        for (var i = 0; i < 100 && dashboard.LastRequestedResourceId is null; i++)
        {
            await Task.Delay(10);
        }
        Assert.Equal(resourceId, dashboard.LastRequestedResourceId);
    }

    [Fact]
    public void SelectedStrategyTarget_RejectsUnknownObjects()
    {
        using var dashboard = new FakeDashboardService(true);
        using var viewModel = new MainWindowViewModel(dashboard);
        Assert.Throws<ArgumentException>(() =>
            viewModel.SelectedStrategyTarget = new StrategyTargetOption("unreviewed", "Custom"));
    }

    [Fact]
    public void Dispose_ClosesUnderlyingDashboardExactlyOnce()
    {
        var dashboard = new FakeDashboardService(false);
        var viewModel = new MainWindowViewModel(dashboard);

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Equal(1, dashboard.DisposeCalls);
    }

    private sealed class FakeDashboardService(bool running) : IDashboardService
    {
        public int DisposeCalls { get; private set; }
        public string? LastRequestedResourceId { get; private set; }

        public IReadOnlyList<ResourceDefinition> Resources =>
        [
            new("discord", "Discord", [new Uri("https://discord.com/")], []),
            new("youtube", "YouTube", [new Uri("https://www.youtube.com/generate_204")], []),
            new("example", "Example", [new Uri("https://example.com/")], [])
        ];

        public Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResourceHealth>>(Array.Empty<ResourceHealth>());

        public Task<IReadOnlyList<ResourceHealth>> CheckDirectResourcesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResourceHealth>>(Array.Empty<ResourceHealth>());

        public Task<StrategySelectionResult> FindWorkingStrategyForResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            if (!Resources.Any(resource => resource.Id == resourceId))
            {
                throw new ArgumentException("Unknown resource", nameof(resourceId));
            }
            LastRequestedResourceId = resourceId;
            return FindWorkingStrategyAsync(cancellationToken);
        }

        public Task<StrategySelectionResult> FindWorkingStrategyAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(new StrategySelectionResult(
                StrategySelectionStatus.NoWorkingAlternative,
                "private-strategy",
                "private-strategy",
                [
                    new StrategyEvaluation("private-strategy", 0, false, null,
                        [
                            new ResourceHealth("discord", ResourceHealthState.Healthy, DateTimeOffset.UtcNow),
                            new ResourceHealth("youtube", ResourceHealthState.Unavailable, DateTimeOffset.UtcNow)
                        ]),
                    new StrategyEvaluation("private-strategy-alt", 0, false, null, [])
                ]));

        public Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(running
                ? StrategyRuntimeState.Running("byedpi-windows-recommended")
                : StrategyRuntimeState.Stopped);

        public Task<string?> GetLocalSocks5AddressAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(running ? "127.0.0.1:37801" : null);

        public Task StartDefaultStrategyAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopStrategyRuntimeAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose() => DisposeCalls++;
    }
}
