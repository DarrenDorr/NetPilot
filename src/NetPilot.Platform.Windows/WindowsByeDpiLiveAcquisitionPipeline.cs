using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

public enum WindowsByeDpiLiveAcquisitionAction
{
    NoChange,
    Install,
    Upgrade
}

public sealed record WindowsByeDpiLiveAcquisitionResult(
    WindowsByeDpiLiveAcquisitionAction Action,
    string CurrentVersion,
    string? RetainedPreviousVersion,
    bool Changed);

/// <summary>
/// R42.13 lab-only composition of the reviewed release catalog, hardened live HTTP handler,
/// R42.10 downloader, R42.11 protected staging and R42.11.1 atomic promotion boundary.
///
/// The normal NetPilot.Service does not construct or register this type in R42.13. It exists
/// only so the separately gated Windows/LocalSystem lab can exercise the complete acquisition
/// and package-placement path once the exact GitHub release is published and immutable.
/// Native ByeDPI execution is not performed by this type.
/// </summary>
public sealed class WindowsByeDpiLiveAcquisitionPipeline
{
    private readonly Func<HttpMessageHandler> handlerFactory;

    public WindowsByeDpiLiveAcquisitionPipeline()
        : this(static () => ByeDpiLiveHttpHandlerFactory.Create())
    {
    }

    internal WindowsByeDpiLiveAcquisitionPipeline(Func<HttpMessageHandler> handlerFactory)
    {
        this.handlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
    }

    public async Task<WindowsByeDpiLiveAcquisitionResult> ApplyReviewedPackageAsync(
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var descriptor = ByeDpiReviewedReleaseCatalog.Current;
        var state = WindowsByeDpiAtomicPackagePromoter.GetState();

        return await ApplyCoreAsync(
            descriptor,
            state.CurrentVersion,
            state.RetainedPreviousVersion,
            engineLeaseActive,
            static (bundle, token) => WindowsByeDpiVerifiedBundleStager.StageAsync(bundle, token),
            static (bundle, staged, operation, leaseActive, token) =>
                WindowsByeDpiVerifiedStagingPromoter.PromoteAsync(
                    bundle,
                    staged,
                    operation,
                    leaseActive,
                    token),
            cancellationToken);
    }

    internal async Task<WindowsByeDpiLiveAcquisitionResult> ApplyForTestingAsync(
        ByeDpiImmutableReleaseDescriptor descriptor,
        bool engineLeaseActive,
        string stagingBase,
        string managedBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedBase);

        var state = WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);
        return await ApplyCoreAsync(
            descriptor,
            state.CurrentVersion,
            state.RetainedPreviousVersion,
            engineLeaseActive,
            (bundle, token) => WindowsByeDpiVerifiedBundleStager.StageForTestingAsync(
                bundle,
                stagingBase,
                token),
            (bundle, staged, operation, leaseActive, token) =>
                WindowsByeDpiVerifiedStagingPromoter.PromoteForTestingAsync(
                    bundle,
                    staged,
                    operation,
                    leaseActive,
                    stagingBase,
                    managedBase,
                    token),
            cancellationToken);
    }

    private async Task<WindowsByeDpiLiveAcquisitionResult> ApplyCoreAsync(
        ByeDpiImmutableReleaseDescriptor descriptor,
        string? currentVersion,
        string? retainedPreviousVersion,
        bool engineLeaseActive,
        Func<ByeDpiVerifiedReleaseBundle, CancellationToken, Task<WindowsByeDpiStagingTransactionResult>> stage,
        Func<
            ByeDpiVerifiedReleaseBundle,
            WindowsByeDpiStagingTransactionResult,
            ByeDpiPackageLifecycleOperation,
            bool,
            CancellationToken,
            Task<WindowsByeDpiPromotionResult>> promote,
        CancellationToken cancellationToken)
    {
        _ = ByeDpiImmutableReleaseDocument.Serialize(descriptor);

        if (engineLeaseActive)
        {
            throw new UpdateSecurityException(
                "ByeDpiLiveAcquisitionActiveLease",
                "ByeDPI package acquisition/promotion is blocked while an engine lease is active.");
        }

        if (string.Equals(currentVersion, descriptor.Version, StringComparison.Ordinal))
        {
            return new WindowsByeDpiLiveAcquisitionResult(
                WindowsByeDpiLiveAcquisitionAction.NoChange,
                descriptor.Version,
                retainedPreviousVersion,
                Changed: false);
        }

        var operation = currentVersion is null
            ? ByeDpiPackageLifecycleOperation.Install
            : ByeDpiPackageLifecycleOperation.Upgrade;

        using var handler = handlerFactory()
            ?? throw new UpdateSecurityException(
                "ByeDpiLiveHandlerMissing",
                "ByeDPI live acquisition handler factory returned no handler.");
        using var transport = new ByeDpiIsolatedHttpTransport(
            handler,
            disposeHandler: false);
        var downloader = new ByeDpiVerifiedReleaseDownloader(transport);
        var bundle = await downloader.DownloadAsync(
            descriptor,
            currentVersion,
            cancellationToken);

        var staged = await stage(bundle, cancellationToken);
        var promoted = await promote(
            bundle,
            staged,
            operation,
            engineLeaseActive: false,
            cancellationToken);

        if (!string.Equals(promoted.CurrentVersion, descriptor.Version, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "ByeDpiLiveAcquisitionPromotionMismatch",
                "ByeDPI atomic promotion result does not match the exact reviewed release version.");
        }

        return new WindowsByeDpiLiveAcquisitionResult(
            operation == ByeDpiPackageLifecycleOperation.Install
                ? WindowsByeDpiLiveAcquisitionAction.Install
                : WindowsByeDpiLiveAcquisitionAction.Upgrade,
            promoted.CurrentVersion,
            promoted.RetainedPreviousVersion,
            Changed: true);
    }
}
