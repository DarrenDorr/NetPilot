namespace NetPilot.App.Services;

public sealed record EngineInstallationState(
    bool IsInstalled,
    string? Version = null);

public sealed record EngineInstallResult(
    string Version,
    bool TemporaryCleanupCompleted);

public interface IEngineProvisioningService
{
    Task<EngineInstallationState> GetInstallationStateAsync(
        CancellationToken cancellationToken = default);

    Task<EngineInstallResult> InstallLatestAsync(
        CancellationToken cancellationToken = default);
}
