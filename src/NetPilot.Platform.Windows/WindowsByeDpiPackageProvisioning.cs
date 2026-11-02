using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

public sealed record WindowsByeDpiPackageStatus(
    bool IsInstalled,
    string? CurrentVersion,
    string? RetainedPreviousVersion);

public sealed record WindowsByeDpiPackageApplyResult(
    ByeDpiPackageLifecycleOperation Operation,
    string CurrentVersion,
    string? RetainedPreviousVersion,
    bool Changed);

/// <summary>
/// R42.12 typed package-operation surface consumed by the privileged service.
///
/// The interface intentionally accepts no path, URL, executable name, version target,
/// archive bytes or command-line arguments. The service may ask only for protected package
/// status or request application of the implementation-owned reviewed package identity.
/// </summary>
public interface IWindowsByeDpiPackageProvisioner
{
    Task<WindowsByeDpiPackageStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<WindowsByeDpiPackageApplyResult> ApplyReviewedPackageAsync(
        bool engineLeaseActive,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Production R42.12 fail-closed implementation. Read-only status is obtained through the
/// existing R42.3 protected managed-state API, while package mutation remains unavailable
/// until a separate live-network/Windows E2E admission gate supplies a reviewed provisioner.
/// </summary>
public sealed class NoAdmittedWindowsByeDpiPackageProvisioner : IWindowsByeDpiPackageProvisioner
{
    public static NoAdmittedWindowsByeDpiPackageProvisioner Instance { get; } = new();

    private NoAdmittedWindowsByeDpiPackageProvisioner()
    {
    }

    public Task<WindowsByeDpiPackageStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = WindowsByeDpiAtomicPackagePromoter.GetState();
        return Task.FromResult(new WindowsByeDpiPackageStatus(
            state.CurrentVersion is not null,
            state.CurrentVersion,
            state.RetainedPreviousVersion));
    }

    public Task<WindowsByeDpiPackageApplyResult> ApplyReviewedPackageAsync(
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new UpdateSecurityException(
            "ByeDpiPackageProvisioningNotAdmitted",
            "ByeDPI package provisioning remains blocked until a separately reviewed live acquisition gate is admitted.");
    }
}
