using System.Security.Cryptography;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

/// <summary>
/// R42.11 bridge from an R42.10 verified in-memory release bundle into the existing
/// R42.2 protected Windows staging transaction.
///
/// This class deliberately introduces no new filesystem primitives, network client,
/// Service/IPC surface, installer mutation, package promotion or native execution.
/// The only production mutation it can request is the already-reviewed
/// WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync boundary.
/// </summary>
public static class WindowsByeDpiVerifiedBundleStager
{
    public static async Task<WindowsByeDpiStagingTransactionResult> StageAsync(
        ByeDpiVerifiedReleaseBundle bundle,
        CancellationToken cancellationToken = default)
    {
        var validated = ValidateAndCopy(bundle);
        await using var payload = new MemoryStream(validated.PayloadBytes, writable: false);

        return await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync(
            payload,
            validated.Manifest,
            cancellationToken);
    }

    internal static async Task<WindowsByeDpiStagingTransactionResult> StageForTestingAsync(
        ByeDpiVerifiedReleaseBundle bundle,
        string stagingBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);

        var validated = ValidateAndCopy(bundle);
        await using var payload = new MemoryStream(validated.PayloadBytes, writable: false);

        return await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyForTestingAsync(
            payload,
            validated.Manifest,
            stagingBase,
            cancellationToken);
    }

    private static ValidatedBundle ValidateAndCopy(ByeDpiVerifiedReleaseBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(bundle.Descriptor);
        ArgumentNullException.ThrowIfNull(bundle.Manifest);

        // Re-apply the immutable descriptor contract at the filesystem boundary.
        _ = ByeDpiImmutableReleaseDocument.Serialize(bundle.Descriptor);

        var payloadBytes = bundle.CopyPayloadBytes();
        var manifestDocumentBytes = bundle.CopyManifestDocumentBytes();
        var provenanceBytes = bundle.CopyProvenanceBytes();

        VerifyExactAsset(bundle.Descriptor.Payload, payloadBytes, "payload");
        VerifyExactAsset(bundle.Descriptor.Manifest, manifestDocumentBytes, "manifest");
        VerifyExactAsset(bundle.Descriptor.Provenance, provenanceBytes, "provenance");

        var aggregateBytes = checked(
            (long)payloadBytes.Length
            + manifestDocumentBytes.Length
            + provenanceBytes.Length);
        if (aggregateBytes != bundle.TotalBytes
            || aggregateBytes > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes)
        {
            throw Invalid("ByeDPI verified bundle aggregate byte identity changed before protected staging.");
        }

        var reparsedManifest = ByeDpiPackageManifestDocument.Parse(manifestDocumentBytes);
        ValidateManifestIdentity(bundle.Descriptor, bundle.Manifest, reparsedManifest);

        return new ValidatedBundle(payloadBytes, reparsedManifest);
    }

    private static void VerifyExactAsset(
        ByeDpiImmutableReleaseAsset expected,
        byte[] bytes,
        string context)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.LongLength != expected.SizeBytes)
        {
            throw Invalid($"ByeDPI verified {context} length changed before protected staging.");
        }

        var actualHash = SHA256.HashData(bytes);
        var expectedHash = Convert.FromHexString(expected.Sha256Hex);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw Invalid($"ByeDPI verified {context} SHA-256 changed before protected staging.");
        }
    }

    private static void ValidateManifestIdentity(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiPackageManifest bundledManifest,
        ByeDpiPackageManifest reparsedManifest)
    {
        if (!string.Equals(reparsedManifest.EngineId, descriptor.EngineId, StringComparison.Ordinal)
            || !string.Equals(reparsedManifest.Version, descriptor.Version, StringComparison.Ordinal)
            || !string.Equals(reparsedManifest.Architecture, descriptor.Architecture, StringComparison.Ordinal)
            || !string.Equals(
                reparsedManifest.CanonicalManifestSha256,
                bundledManifest.CanonicalManifestSha256,
                StringComparison.Ordinal)
            || reparsedManifest.TotalSizeBytes != bundledManifest.TotalSizeBytes
            || reparsedManifest.Files.Count != bundledManifest.Files.Count)
        {
            throw Invalid("ByeDPI package manifest identity changed between verified download and protected staging.");
        }

        for (var index = 0; index < reparsedManifest.Files.Count; index++)
        {
            var left = reparsedManifest.Files[index];
            var right = bundledManifest.Files[index];
            if (!string.Equals(left.RelativePath, right.RelativePath, StringComparison.Ordinal)
                || left.SizeBytes != right.SizeBytes
                || !string.Equals(left.Sha256Hex, right.Sha256Hex, StringComparison.Ordinal))
            {
                throw Invalid("ByeDPI package manifest file identity changed before protected staging.");
            }
        }
    }

    private sealed record ValidatedBundle(
        byte[] PayloadBytes,
        ByeDpiPackageManifest Manifest);

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiVerifiedBundleStagingInvalid", message);
}
