using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;
using NetPilot.Ipc;

namespace NetPilot.App.Services;

public sealed record ZapretProvisioningOutcome(
    string Code,
    string? Message,
    IpcZapretInstallationStatus Installation,
    IpcZapretProvisioningResult? Details);

public interface IDashboardService : IDisposable
{
    IReadOnlyList<ResourceDefinition> Resources { get; }

    Task<IReadOnlyList<ResourceHealth>> CheckResourcesAsync(
        CancellationToken cancellationToken = default);

    Task<StrategySelectionResult> FindWorkingStrategyAsync(
        CancellationToken cancellationToken = default);

    Task<StrategyRuntimeState> GetStrategyRuntimeStateAsync(
        CancellationToken cancellationToken = default);

    Task<IpcZapretInstallationStatus> GetZapretInstallationStatusAsync(
        CancellationToken cancellationToken = default);

    Task<ZapretProvisioningOutcome> InstallOrUpdateOfficialZapretLatestAsync(
        CancellationToken cancellationToken = default);
}

public sealed class DashboardService : IDashboardService
{
    private readonly PublicHttpsHealthChecker _healthChecker;
    private readonly IStrategyRuntime _strategyRuntime;
    private readonly StrategySelectionEngine _selectionEngine;
    private readonly IPrivilegedServiceClient _serviceClient;
    private bool _disposed;

    public DashboardService(
        PublicHttpsHealthChecker healthChecker,
        IStrategyRuntime strategyRuntime,
        StrategySelectionEngine selectionEngine,
        IPrivilegedServiceClient serviceClient)
    {
        _healthChecker = healthChecker ?? throw new ArgumentNullException(nameof(healthChecker));
        _strategyRuntime = strategyRuntime ?? throw new ArgumentNullException(nameof(strategyRuntime));
        _selectionEngine = selectionEngine ?? throw new ArgumentNullException(nameof(selectionEngine));
        _serviceClient = serviceClient ?? throw new ArgumentNullException(nameof(serviceClient));
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

    public async Task<IpcZapretInstallationStatus> GetZapretInstallationStatusAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var response = await _serviceClient.GetZapretInstallationStatusAsync(cancellationToken);
        EnsureSuccessful(response);

        return response.ZapretInstallation
            ?? throw new IpcProtocolException(
                "MissingInstallationStatus",
                "Служба NetPilot не вернула состояние установки Zapret.");
    }

    public async Task<ZapretProvisioningOutcome> InstallOrUpdateOfficialZapretLatestAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var response = await _serviceClient.InstallOrUpdateOfficialZapretLatestAsync(cancellationToken);
        EnsureSuccessful(response);

        var installation = response.ZapretInstallation
            ?? throw new IpcProtocolException(
                "MissingInstallationStatus",
                "Служба NetPilot не вернула состояние Zapret после provisioning.");

        return new ZapretProvisioningOutcome(
            response.Code,
            response.Message,
            installation,
            response.ZapretProvisioning);
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

    private static void EnsureSuccessful(IpcResponse response)
    {
        if (!response.Success)
        {
            throw new IpcProtocolException(
                response.Code,
                response.Message ?? "Служба NetPilot отклонила операцию.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
