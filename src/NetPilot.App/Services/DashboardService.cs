using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.App.Services;

public interface IDashboardService : IDisposable
{
    IReadOnlyList<ResourceDefinition> Resources { get; }

    // Health through the app-owned ByeDPI SOCKS5 endpoint; never the system route.
    Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default);

    // Explicit direct-network baseline, not an implicit fallback for ByeDPI checks.
    Task<IReadOnlyList<ResourceHealth>> CheckDirectResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<StrategySelectionResult> FindWorkingStrategyAsync(
        CancellationToken cancellationToken = default);

    Task<StrategySelectionResult> FindWorkingStrategyForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
        CancellationToken cancellationToken = default);

    Task<string?> GetLocalSocks5AddressAsync(
        CancellationToken cancellationToken = default);

    Task StartDefaultStrategyAsync(
        CancellationToken cancellationToken = default);

    // Fixed, reviewed, expert-only diagnosis. Never accepts user-supplied argv.
    Task ActivateDiagnosticBaselineAsync(
        CancellationToken cancellationToken = default);

    Task RestoreRecommendedStrategyAsync(
        CancellationToken cancellationToken = default);

    Task StopStrategyRuntimeAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly PublicHttpsHealthChecker _healthChecker;
    private readonly IResourceHealthProbe _byeDpiHealthProbe;
    private readonly IStrategyRuntime _strategyRuntime;
    private readonly StrategySelectionEngine _selectionEngine;
    private bool _disposed;

    public DashboardService(
        PublicHttpsHealthChecker healthChecker,
        IResourceHealthProbe byeDpiHealthProbe,
        IStrategyRuntime strategyRuntime,
        StrategySelectionEngine selectionEngine)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _byeDpiHealthProbe = byeDpiHealthProbe ?? throw new ArgumentNullException(nameof(byeDpiHealthProbe));
        _strategyRuntime = strategyRuntime ?? throw new ArgumentNullException(nameof(strategyRuntime));
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
    }

    public IReadOnlyList<ResourceDefinition> Resources => BuiltInResourceCatalog.All;

    public Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default)
        => CheckWithProbeAsync(_byeDpiHealthProbe.CheckAsync, cancellationToken);

    public Task<IReadOnlyList<ResourceHealth>> CheckDirectResourcesAsync(
        CancellationToken cancellationToken = default)
        => CheckWithProbeAsync(_healthChecker.CheckAsync, cancellationToken);

    private async Task<IReadOnlyList<ResourceHealth>> CheckWithProbeAsync(
        Func<ResourceDefinition, CancellationToken, Task<ResourceHealth>> check,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var results = new List<ResourceHealth>(Resources.Count);
        foreach (var resource in Resources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await check(resource, cancellationToken));
        }
        return results;
    }

    public Task<StrategySelectionResult> FindWorkingStrategyAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _selectionEngine.EvaluateAndRecoverAsync(Resources, cancellationToken);
    }

    public Task<StrategySelectionResult> FindWorkingStrategyForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        // Only canonical built-in services are eligible; no arbitrary endpoints.
        var target = Resources.SingleOrDefault(resource =>
            string.Equals(resource.Id, resourceId, StringComparison.Ordinal));
        if (target is null)
        {
            throw new ArgumentException("Неизвестный сервис для подбора стратегии.", nameof(resourceId));
        }

        return _selectionEngine.EvaluateAndRecoverAsync([target], cancellationToken);
    }

    public Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return _strategyRuntime.GetStateAsync(cancellationToken);
    }

    public async Task<string?> GetLocalSocks5AddressAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var state = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (!state.IsRunning || string.IsNullOrWhiteSpace(state.StrategyId))
        {
            return null;
        }

        if (_strategyRuntime is not ILocalSocks5EndpointProvider endpoint)
        {
            return null;
        }

        var port = endpoint.LocalSocks5Port;
        return port is >= 1 and <= 65535 ? $"127.0.0.1:{port.Value}" : null;
    }

    public async Task StartDefaultStrategyAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        var current = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (current.IsRunning)
        {
            return;
        }

        var strategies = await _strategyRuntime.ListStrategiesAsync(cancellationToken);
        var selected = strategies.FirstOrDefault()
            ?? throw new StrategyRuntimeException(
                "NoStrategiesAvailable",
                "NetPilot не нашёл ни одной разрешённой стратегии сетевого движка.");

        await _strategyRuntime.ActivateAsync(selected.Id, cancellationToken);
        var confirmed = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (!confirmed.IsRunning
            || !string.Equals(confirmed.StrategyId, selected.Id, StringComparison.Ordinal))
        {
            throw new StrategyRuntimeException(
                "StartStateMismatch",
                "Сетевой движок не подтвердил безопасный запуск выбранной стратегии.");
        }
    }

    public Task ActivateDiagnosticBaselineAsync(
        CancellationToken cancellationToken = default)
        => ActivateFixedRunningProfileAsync("byedpi-diagnostic-pass-through", cancellationToken);

    public Task RestoreRecommendedStrategyAsync(
        CancellationToken cancellationToken = default)
        => ActivateFixedRunningProfileAsync("byedpi-windows-recommended", cancellationToken);

    private async Task ActivateFixedRunningProfileAsync(
        string strategyId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var state = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (!state.IsRunning)
        {
            throw new StrategyRuntimeException(
                "DiagnosticEngineNotRunning",
                "Сначала запустите ByeDPI. Автоматического запуска нет.");
        }

        var allowed = await _strategyRuntime.ListStrategiesAsync(cancellationToken);
        if (!allowed.Any(strategy => string.Equals(strategy.Id, strategyId, StringComparison.Ordinal)))
        {
            throw new StrategyRuntimeException(
                "DiagnosticProfileMissing",
                "Запрошенный диагностический профиль отсутствует в проверенном каталоге.");
        }

        await _strategyRuntime.ActivateAsync(strategyId, cancellationToken);
        var confirmed = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (!confirmed.IsRunning
            || !string.Equals(confirmed.StrategyId, strategyId, StringComparison.Ordinal))
        {
            throw new StrategyRuntimeException(
                "DiagnosticStateMismatch",
                "ByeDPI не подтвердил переключение на выбранный профиль.");
        }
    }

    public async Task StopStrategyRuntimeAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _strategyRuntime.StopAsync(cancellationToken);
        var confirmed = await _strategyRuntime.GetStateAsync(cancellationToken);
        if (confirmed.IsRunning)
        {
            throw new StrategyRuntimeException(
                "StopStateMismatch",
                "Сетевой движок не подтвердил безопасную остановку.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            if (_strategyRuntime is IDisposable disposableRuntime)
            {
                Task.Run(disposableRuntime.Dispose).GetAwaiter().GetResult();
            }
        }
        finally
        {
            _healthChecker.Dispose();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
