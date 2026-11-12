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
        Assert.True(viewModel.FindStrategyCommand.CanExecute(null));

        viewModel.IsAdvancedMode = true;
        Assert.True(viewModel.IsAdvancedMode);
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

        public IReadOnlyList<ResourceDefinition> Resources => Array.Empty<ResourceDefinition>();

        public Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResourceHealth>>(Array.Empty<ResourceHealth>());

        public Task<StrategySelectionResult> FindWorkingStrategyAsync(
            CancellationToken cancellationToken = default)
            => Task.FromException<StrategySelectionResult>(new NotSupportedException());

        public Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(running
                ? StrategyRuntimeState.Running("byedpi-windows-recommended")
                : StrategyRuntimeState.Stopped);

        public Task StartDefaultStrategyAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task StopStrategyRuntimeAsync(
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Dispose() => DisposeCalls++;
    }
}
