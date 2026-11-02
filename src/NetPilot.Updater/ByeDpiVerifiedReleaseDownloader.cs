using System.Security.Cryptography;

namespace NetPilot.Updater;

/// <summary>
/// R42.10 verified in-memory ByeDPI release acquisition result.
///
/// Bytes are defensively copied on construction and on export. This object is not a
/// staging token, admission token, executable lease or authorization to run native code.
/// </summary>
public sealed class ByeDpiVerifiedReleaseBundle
{
    private readonly byte[] _payloadBytes;
    private readonly byte[] _manifestDocumentBytes;
    private readonly byte[] _provenanceBytes;

    internal ByeDpiVerifiedReleaseBundle(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiPackageManifest manifest,
        byte[] payloadBytes,
        byte[] manifestDocumentBytes,
        byte[] provenanceBytes)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(payloadBytes);
        ArgumentNullException.ThrowIfNull(manifestDocumentBytes);
        ArgumentNullException.ThrowIfNull(provenanceBytes);

        Descriptor = descriptor;
        Manifest = manifest;
        _payloadBytes = (byte[])payloadBytes.Clone();
        _manifestDocumentBytes = (byte[])manifestDocumentBytes.Clone();
        _provenanceBytes = (byte[])provenanceBytes.Clone();
        TotalBytes = checked(
            (long)_payloadBytes.Length
            + _manifestDocumentBytes.Length
            + _provenanceBytes.Length);
    }

    public ByeDpiImmutableReleaseDescriptor Descriptor { get; }

    public ByeDpiPackageManifest Manifest { get; }

    public long TotalBytes { get; }

    public byte[] CopyPayloadBytes() => (byte[])_payloadBytes.Clone();

    public byte[] CopyManifestDocumentBytes() => (byte[])_manifestDocumentBytes.Clone();

    public byte[] CopyProvenanceBytes() => (byte[])_provenanceBytes.Clone();
}

/// <summary>
/// R42.10 product-level verified downloader boundary for one immutable ByeDPI release.
///
/// The downloader composes the R42.9 injected transport with the R42.7 trust policy,
/// acquires exactly the three immutable R42.6 assets, independently re-verifies each
/// returned byte sequence by exact size and SHA-256, and validates the downloaded
/// package manifest identity before returning a defensive in-memory bundle.
///
/// This type deliberately performs no filesystem writes, staging, Service/IPC wiring,
/// installer mutation or native execution. It does not construct a live network handler.
/// </summary>
public sealed class ByeDpiVerifiedReleaseDownloader
{
    private readonly ByeDpiIsolatedHttpTransport _transport;

    public ByeDpiVerifiedReleaseDownloader(ByeDpiIsolatedHttpTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public async Task<ByeDpiVerifiedReleaseBundle> DownloadAsync(
        ByeDpiImmutableReleaseDescriptor descriptor,
        string? currentInstalledVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        // Re-apply the strict R42.6 descriptor contract at the product boundary.
        _ = ByeDpiImmutableReleaseDocument.Serialize(descriptor);

        var metadata = await _transport.FetchReleaseMetadataAsync(
            descriptor,
            cancellationToken);

        var plan = ByeDpiAcquisitionTrustPolicy.CreatePlan(
            descriptor,
            metadata,
            currentInstalledVersion);

        ValidatePlanShape(descriptor, plan);

        var payload = await DownloadExpectedAssetAsync(
            plan,
            descriptor.Payload,
            cancellationToken);
        var manifestDocument = await DownloadExpectedAssetAsync(
            plan,
            descriptor.Manifest,
            cancellationToken);
        var provenance = await DownloadExpectedAssetAsync(
            plan,
            descriptor.Provenance,
            cancellationToken);

        var totalBytes = checked(
            (long)payload.Length
            + manifestDocument.Length
            + provenance.Length);
        if (totalBytes > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes)
        {
            throw Invalid("ByeDPI verified release bytes exceed the fixed aggregate acquisition limit.");
        }

        var manifest = ByeDpiPackageManifestDocument.Parse(manifestDocument);
        ValidateManifestIdentity(descriptor, manifest);

        return new ByeDpiVerifiedReleaseBundle(
            descriptor,
            manifest,
            payload,
            manifestDocument,
            provenance);
    }

    private async Task<byte[]> DownloadExpectedAssetAsync(
        ByeDpiAcquisitionPlan plan,
        ByeDpiImmutableReleaseAsset expected,
        CancellationToken cancellationToken)
    {
        var matches = plan.Assets
            .Where(asset =>
                string.Equals(asset.Name, expected.Name, StringComparison.Ordinal)
                && asset.SizeBytes == expected.SizeBytes
                && string.Equals(asset.Sha256Hex, expected.Sha256Hex, StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw Invalid("ByeDPI verified downloader could not resolve exactly one immutable asset plan.");
        }

        var bytes = await _transport.FetchVerifiedAssetAsync(
            plan,
            matches[0],
            cancellationToken);

        VerifyReturnedBytes(expected, bytes);
        return bytes;
    }

    private static void ValidatePlanShape(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiAcquisitionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Assets is null || plan.Assets.Count != 3)
        {
            throw Invalid("ByeDPI verified downloader requires exactly three trusted immutable assets.");
        }

        var expectedAssets = new[]
        {
            descriptor.Payload,
            descriptor.Manifest,
            descriptor.Provenance
        };

        long declaredTotal = 0;
        foreach (var expected in expectedAssets)
        {
            if (expected.SizeBytes <= 0)
            {
                throw Invalid("ByeDPI immutable asset size must remain positive at the downloader boundary.");
            }

            if (declaredTotal > ByeDpiAcquisitionTrustPolicy.MaxTotalAssetBytes - expected.SizeBytes)
            {
                throw Invalid("ByeDPI immutable release descriptor exceeds the fixed aggregate acquisition limit.");
            }

            declaredTotal += expected.SizeBytes;

            var exactMatches = plan.Assets.Count(asset =>
                string.Equals(asset.Name, expected.Name, StringComparison.Ordinal)
                && asset.SizeBytes == expected.SizeBytes
                && string.Equals(asset.Sha256Hex, expected.Sha256Hex, StringComparison.Ordinal));

            if (exactMatches != 1)
            {
                throw Invalid("ByeDPI acquisition plan drifted from the immutable release descriptor.");
            }
        }
    }

    private static void VerifyReturnedBytes(
        ByeDpiImmutableReleaseAsset expected,
        byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.LongLength != expected.SizeBytes)
        {
            throw Invalid("ByeDPI downloaded asset length changed after transport verification.");
        }

        var actualHash = SHA256.HashData(bytes);
        var expectedHash = Convert.FromHexString(expected.Sha256Hex);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw Invalid("ByeDPI downloaded asset SHA-256 changed after transport verification.");
        }
    }

    private static void ValidateManifestIdentity(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiPackageManifest manifest)
    {
        if (!string.Equals(manifest.EngineId, descriptor.EngineId, StringComparison.Ordinal)
            || !string.Equals(manifest.Version, descriptor.Version, StringComparison.Ordinal)
            || !string.Equals(manifest.Architecture, descriptor.Architecture, StringComparison.Ordinal))
        {
            throw new UpdateSecurityException(
                "ByeDpiVerifiedDownloadManifestMismatch",
                "Downloaded ByeDPI manifest identity does not match the immutable release descriptor.");
        }
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiVerifiedDownloadInvalid", message);
}
