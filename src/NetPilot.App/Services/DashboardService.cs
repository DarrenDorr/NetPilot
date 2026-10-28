using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.App.Services;

public interface IDashboardService : IDisposable
{
    IReadOnlyList<ResourceDefinition> Resources { get; }

    Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<ProfileSelectionResult> FindWorkingProfileAsync(
        CancellationToken cancellationToken = default);

    Task<ProfileRuntimeState> GetProfileRuntimeStateAsync(
        CancellationToken cancellationToken = default);

    Task<EngineInstallationState> GetEngineInstallationStateAsync(
        CancellationToken cancellationToken = default);

    Task<EngineInstallResult> InstallEngineAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly PublicHttpsHealthChecker _healthChecker;
    private readonly IStrategyRuntime _strategyRuntime;
    private readonly StrategySelectionEngine _selectionEngine;
    private readonly IEngineProvisioningService _provisioningService;
    private bool _disposed;

    public DashboardService(
        PublicHttpsHealthChecker healthChecker,
        IStrategyRuntime strategyRuntime,
        StrategySelectionEngine selectionEngine,
        IEngineProvisioningService provisioningService)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _strategyRuntime = strategyRuntime ?? throw new ArgumentNullException(nameof(strategyRuntime));
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _provisioningService = provisioningService ?? throw new ArgumentNullException(nameof(provisioningService));
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

    public async Task<ProfileSelectionResult> FindWorkingProfileAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var result = await _selectionEngine.EvaluateAndRecoverAsync(
                Resources,
                cancellationToken);
            return ToProfileSelectionResult(result);
        }
        catch (StrategyRuntimeException exception)
        {
            throw new ProfileOperationException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    public async Task<ProfileRuntimeState> GetProfileRuntimeStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            var state = await _strategyRuntime.GetStateAsync(cancellationToken);
            return state.IsRunning && !string.IsNullOrWhiteSpace(state.StrategyId)
                ? ProfileRuntimeState.Running(state.StrategyId)
                : ProfileRuntimeState.Stopped;
        }
        catch (StrategyRuntimeException exception)
        {
            throw new ProfileOperationException(
                exception.Code,
                exception.Message,
                exception);
        }
    }

    public Task<EngineInstallationState> GetEngineInstallationStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _provisioningService.GetInstallationStateAsync(cancellationToken);
    }

    public Task<EngineInstallResult> InstallEngineAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _provisioningService.InstallLatestAsync(cancellationToken);
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

    private static ProfileSelectionResult ToProfileSelectionResult(
        StrategySelectionResult result) =>
        new(
            result.Status switch
            {
                StrategySelectionStatus.NotRunning => ProfileSelectionStatus.NotRunning,
                StrategySelectionStatus.CurrentHealthy => ProfileSelectionStatus.CurrentHealthy,
                StrategySelectionStatus.TransientFailure => ProfileSelectionStatus.TransientFailure,
                StrategySelectionStatus.RecoveryNotApplicable => ProfileSelectionStatus.RecoveryNotApplicable,
                StrategySelectionStatus.Switched => ProfileSelectionStatus.Switched,
                StrategySelectionStatus.NoWorkingAlternative => ProfileSelectionStatus.NoWorkingAlternative,
                StrategySelectionStatus.OriginalRestoreFailed => ProfileSelectionStatus.OriginalRestoreFailed,
                _ => throw new ProfileOperationException(
                    "UnknownProfileSelectionStatus",
                    "Automation вернула неизвестный статус выбора профиля.")
            },
            result.OriginalStrategyId,
            result.SelectedStrategyId,
            result.DiagnosticCode);

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
