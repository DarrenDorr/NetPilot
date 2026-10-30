namespace NetPilot.Updater;

public sealed record ByeDpiAcquisitionAssetMetadata(
    long AssetId,
    string Name,
    long SizeBytes,
    string Sha256Hex,
    Uri AssetApiUri);

public sealed record ByeDpiAcquisitionReleaseMetadata(
    long ReleaseId,
    string Tag,
    bool Draft,
    bool Prerelease,
    bool Immutable,
    IReadOnlyList<ByeDpiAcquisitionAssetMetadata> Assets);

public sealed record ByeDpiAcquisitionAssetPlan(
    long AssetId,
    string Name,
    long SizeBytes,
    string Sha256Hex,
    Uri AssetApiUri);

/// <summary>
/// A pure, read-only acquisition plan. It contains only exact, already-reviewed identities and
/// generated GitHub API endpoints. It does not perform HTTP, filesystem mutation or execution.
/// </summary>
public sealed record ByeDpiAcquisitionPlan(
    Uri ReleaseMetadataApiUri,
    IReadOnlyList<ByeDpiAcquisitionAssetPlan> Assets,
    int MaxReleaseMetadataBytes,
    int NetworkTimeoutSeconds,
    int MaxRedirects,
    long MaxTotalAssetBytes);

/// <summary>
/// R42.7 trust policy for a future ByeDPI acquisition layer.
///
/// This type deliberately contains no network client, filesystem or process APIs.
/// It accepts typed release metadata from a future narrow parser, validates it against the
/// immutable R42.6 descriptor, and returns an exact acquisition plan.
/// </summary>
public static class ByeDpiAcquisitionTrustPolicy
{
    public const int MaxReleaseMetadataBytes = 256 * 1024;
    public const int NetworkTimeoutSeconds = 20;
    public const int MaxRedirects = 1;
    public const long MaxTotalAssetBytes = 64L * 1024 * 1024;

    public static ByeDpiAcquisitionPlan CreatePlan(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiAcquisitionReleaseMetadata metadata,
        string? currentInstalledVersion = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(metadata);

        // Reuse the strict R42.6 validator before trusting any descriptor field.
        _ = ByeDpiImmutableReleaseDocument.Serialize(descriptor);

        ValidateAntiDowngrade(descriptor.Version, currentInstalledVersion);
        ValidateReleaseMetadata(descriptor, metadata);

        var expectedAssets = new[]
        {
            descriptor.Payload,
            descriptor.Manifest,
            descriptor.Provenance
        };

        if (metadata.Assets is null || metadata.Assets.Count != expectedAssets.Length)
        {
            throw Invalid("ByeDPI release metadata must contain exactly the three immutable R42.6 assets.");
        }
        if (metadata.Assets.Any(static asset => asset is null))
        {
            throw Invalid("ByeDPI release metadata contains a null asset entry.");
        }
        if (metadata.Assets.Select(static asset => asset.AssetId).Distinct().Count() != metadata.Assets.Count)
        {
            throw Invalid("ByeDPI release metadata contains duplicate GitHub asset ids.");
        }

        var plans = new List<ByeDpiAcquisitionAssetPlan>(expectedAssets.Length);
        long totalBytes = 0;

        foreach (var expected in expectedAssets)
        {
            var matches = metadata.Assets
                .Where(asset => string.Equals(asset.Name, expected.Name, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length != 1)
            {
                throw Invalid("ByeDPI release metadata is missing an expected asset or contains an ambiguous duplicate.");
            }

            var candidate = matches[0];
            ValidateAsset(candidate, expected);

            if (candidate.SizeBytes > MaxTotalAssetBytes
                || totalBytes > MaxTotalAssetBytes - candidate.SizeBytes)
            {
                throw Invalid("ByeDPI release assets exceed the fixed total acquisition limit.");
            }
            totalBytes += candidate.SizeBytes;

            plans.Add(new ByeDpiAcquisitionAssetPlan(
                candidate.AssetId,
                expected.Name,
                expected.SizeBytes,
                expected.Sha256Hex,
                GetAssetApiUri(candidate.AssetId)));
        }

        return new ByeDpiAcquisitionPlan(
            GetReleaseByTagApiUri(descriptor.ReleaseTag),
            plans.AsReadOnly(),
            MaxReleaseMetadataBytes,
            NetworkTimeoutSeconds,
            MaxRedirects,
            MaxTotalAssetBytes);
    }

    private static Uri GetReleaseByTagApiUri(string releaseTag)
    {
        if (string.IsNullOrWhiteSpace(releaseTag) || releaseTag.Length > 96)
        {
            throw Invalid("ByeDPI release tag is invalid.");
        }

        return new Uri(
            $"https://api.github.com/repos/{ByeDpiImmutableReleaseDocument.RequiredRepositoryOwner}/" +
            $"{ByeDpiImmutableReleaseDocument.RequiredRepositoryName}/releases/tags/{Uri.EscapeDataString(releaseTag)}");
    }

    private static Uri GetAssetApiUri(long assetId)
    {
        if (assetId <= 0)
        {
            throw Invalid("ByeDPI release asset id must be positive.");
        }

        return new Uri(
            $"https://api.github.com/repos/{ByeDpiImmutableReleaseDocument.RequiredRepositoryOwner}/" +
            $"{ByeDpiImmutableReleaseDocument.RequiredRepositoryName}/releases/assets/{assetId}");
    }

    /// <summary>
    /// A future transport may follow exactly one GitHub release-asset redirect.
    /// Signed query parameters are allowed; origin/userinfo/custom ports/fragments are not.
    /// </summary>
    public static void ValidateReleaseAssetRedirect(Uri uri, int redirectNumber)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (redirectNumber != 1 || redirectNumber > MaxRedirects)
        {
            throw Invalid("ByeDPI acquisition permits exactly one release-asset redirect hop.");
        }

        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || string.IsNullOrEmpty(uri.AbsolutePath)
            || string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI release asset redirect is outside the fixed trusted GitHub asset origin.");
        }
    }

    private static void ValidateReleaseMetadata(
        ByeDpiImmutableReleaseDescriptor descriptor,
        ByeDpiAcquisitionReleaseMetadata metadata)
    {
        if (metadata.ReleaseId <= 0)
        {
            throw Invalid("ByeDPI release id must be positive.");
        }

        if (metadata.Draft || metadata.Prerelease || !metadata.Immutable)
        {
            throw Invalid("ByeDPI acquisition requires a published stable immutable GitHub release.");
        }

        if (!string.Equals(metadata.Tag, descriptor.ReleaseTag, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI release tag does not match the immutable distribution descriptor.");
        }
    }

    private static void ValidateAsset(
        ByeDpiAcquisitionAssetMetadata candidate,
        ByeDpiImmutableReleaseAsset expected)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (candidate.AssetId <= 0)
        {
            throw Invalid("ByeDPI release asset id must be positive.");
        }

        if (!string.Equals(candidate.Name, expected.Name, StringComparison.Ordinal)
            || candidate.SizeBytes != expected.SizeBytes
            || !string.Equals(candidate.Sha256Hex, expected.Sha256Hex, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI release asset identity drifted from the immutable R42.6 descriptor.");
        }

        var expectedApiUri = GetAssetApiUri(candidate.AssetId);
        if (candidate.AssetApiUri is null
            || !candidate.AssetApiUri.IsAbsoluteUri
            || !string.Equals(candidate.AssetApiUri.AbsoluteUri, expectedApiUri.AbsoluteUri, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI release asset API URI is outside the exact NetPilot GitHub release boundary.");
        }
    }

    private static void ValidateAntiDowngrade(string targetVersion, string? currentVersion)
    {
        var target = ParseCanonicalVersion(targetVersion);
        if (currentVersion is null)
        {
            return;
        }

        var current = ParseCanonicalVersion(currentVersion);
        if (Compare(target, current) < 0)
        {
            throw new UpdateSecurityException(
                "ByeDpiAcquisitionDowngradeBlocked",
                "ByeDPI acquisition target is older than the currently installed canonical version.");
        }
    }

    private static uint[] ParseCanonicalVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            throw Invalid("ByeDPI acquisition version is invalid.");
        }

        var segments = value.Split('.', StringSplitOptions.None);
        if (segments.Length != 3)
        {
            throw Invalid("ByeDPI acquisition version must be canonical major.minor.patch.");
        }

        var result = new uint[3];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0
                || segment.Any(static character => character is < '0' or > '9')
                || (segment.Length > 1 && segment[0] == '0')
                || !uint.TryParse(segment, out result[index]))
            {
                throw Invalid("ByeDPI acquisition version must be canonical major.minor.patch.");
            }
        }

        return result;
    }

    private static int Compare(IReadOnlyList<uint> left, IReadOnlyList<uint> right)
    {
        for (var index = 0; index < 3; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }
        return 0;
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiAcquisitionPolicyInvalid", message);
}
