using System.Security.Cryptography;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

/// <summary>
/// R42.11.1 bridge from a verified protected staging transaction into the already-reviewed
/// R42.3 atomic package promotion boundary.
///
/// This type does not accept a raw target path, does not implement filesystem mutation,
/// does not launch native code and does not change runtime admission. Production promotion
/// is delegated only to the existing verified source verifier and atomic package promoter.
/// </summary>
public static class WindowsByeDpiVerifiedStagingPromoter
{
    public static async Task<WindowsByeDpiPromotionResult> PromoteAsync(
        ByeDpiVerifiedReleaseBundle bundle,
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageLifecycleOperation operation,
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        var manifest = ValidateBundleAndTransaction(bundle, stagedTransaction, operation);

        var source = await WindowsByeDpiPromotionSourceVerifier.VerifyAsync(
            stagedTransaction,
            manifest,
            cancellationToken);

        return await WindowsByeDpiAtomicPackagePromoter.PromoteAsync(
            source,
            operation,
            engineLeaseActive,
            cancellationToken);
    }

    internal static async Task<WindowsByeDpiPromotionResult> PromoteForTestingAsync(
        ByeDpiVerifiedReleaseBundle bundle,
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageLifecycleOperation operation,
        bool engineLeaseActive,
        string stagingBase,
        string managedBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);
        ArgumentException.ThrowIfNullOrWhiteSpace(managedBase);

        var manifest = ValidateBundleAndTransaction(bundle, stagedTransaction, operation);

        var source = await WindowsByeDpiPromotionSourceVerifier.VerifyForTestingAsync(
            stagedTransaction,
            manifest,
            stagingBase,
            cancellationToken);

        return await WindowsByeDpiAtomicPackagePromoter.PromoteForTestingAsync(
            source,
            operation,
            engineLeaseActive,
            managedBase,
            stagingBase,
            cancellationToken);
    }

    private static ByeDpiPackageManifest ValidateBundleAndTransaction(
        ByeDpiVerifiedReleaseBundle bundle,
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageLifecycleOperation operation)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(stagedTransaction);

        if (operation is not ByeDpiPackageLifecycleOperation.Install
            and not ByeDpiPackageLifecycleOperation.Upgrade)
        {
            throw Invalid("R42.11.1 promotion bridge permits only Install or Upgrade operations.");
        }

        _ = ByeDpiImmutableReleaseDocument.Serialize(bundle.Descriptor);

        var manifestBytes = bundle.CopyManifestDocumentBytes();
        if (manifestBytes.LongLength != bundle.Descriptor.Manifest.SizeBytes)
        {
            throw Invalid("ByeDPI manifest document length changed before atomic promotion.");
        }

        var actualHash = SHA256.HashData(manifestBytes);
        var expectedHash = Convert.FromHexString(bundle.Descriptor.Manifest.Sha256Hex);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw Invalid("ByeDPI manifest document SHA-256 changed before atomic promotion.");
        }

        var reparsedManifest = ByeDpiPackageManifestDocument.Parse(manifestBytes);
        if (!string.Equals(
                reparsedManifest.CanonicalManifestSha256,
                bundle.Manifest.CanonicalManifestSha256,
                StringComparison.Ordinal)
            || !string.Equals(reparsedManifest.EngineId, bundle.Descriptor.EngineId, StringComparison.Ordinal)
            || !string.Equals(reparsedManifest.Version, bundle.Descriptor.Version, StringComparison.Ordinal)
            || !string.Equals(reparsedManifest.Architecture, bundle.Descriptor.Architecture, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI manifest identity changed before atomic promotion.");
        }

        if (!string.Equals(
                stagedTransaction.ManifestSha256,
                reparsedManifest.CanonicalManifestSha256,
                StringComparison.Ordinal)
            || stagedTransaction.VerifiedFileCount != reparsedManifest.Files.Count)
        {
            throw Invalid("Verified staging result does not match the exact downloaded package manifest.");
        }

        return reparsedManifest;
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiVerifiedPromotionInvalid", message);
}
