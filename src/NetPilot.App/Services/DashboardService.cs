using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.App.Services;

public interface IDashboardService : IDisposable
{
    IReadOnlyList<ResourceDefinition> Resources { get; }

    Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<StrategySelectionResult> FindWorkingStrategyAsync(
        CancellationToken cancellationToken = default);

    Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly PublicHttpsHealthChecker _healthChecker;
    private readonly IStrategyRuntime _strategyRuntime;
    private readonly StrategySelectionEngine _selectionEngine;
    private bool _disposed;

    public DashboardService(
        PublicHttpsHealthChecker healthChecker,
        IStrategyRuntime strategyRuntime,
        StrategySelectionEngine selectionEngine)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _strategyRuntime = strategyRuntime ?? throw new ArgumentNullException(nameof(strategyRuntime));
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
    }

    public IReadOnlyList<ResourceDefinition> Resources => BuiltInResourceCatalog.All;

    public async Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var results = new List<ResourceHealth>(Resources.Count);
        foreach (var resource in Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await _healthChecker.CheckAsync(resource, cancellationToken));
        }

        return results;
    }

    public Task<StrategySelectionResult> FindWorkingStrategyAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _selectionEngine.EvaluateAndRecoverAsync(Resources, cancellationToken);
    }

    public Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _strategyRuntime.GetStateAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _healthChecker.Dispose();
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
