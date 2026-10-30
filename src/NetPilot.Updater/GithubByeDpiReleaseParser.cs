using System.Text.Json;

namespace NetPilot.Updater;

/// <summary>
/// R42.8 structural parser for GitHub release metadata obtained from the exact R42.7 tag endpoint.
/// It performs no network access and makes no acquisition/admission decision by itself.
/// Security-relevant required fields are typed and bounded; duplicate JSON properties are rejected.
/// Unknown properties are tolerated so GitHub may extend its response schema without changing the
/// meaning of the fields consumed by NetPilot.
/// </summary>
public static class GithubByeDpiReleaseParser
{
    public const int MaxAssetsInMetadata = 16;

    private static readonly string[] RequiredRootProperties =
    [
        "id",
        "tag_name",
        "draft",
        "prerelease",
        "immutable",
        "assets"
    ];

    private static readonly string[] RequiredAssetProperties =
    [
        "id",
        "name",
        "state",
        "size",
        "digest",
        "url"
    ];

    public static ByeDpiAcquisitionReleaseMetadata Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > ByeDpiAcquisitionTrustPolicy.MaxReleaseMetadataBytes)
        {
            throw Invalid("ByeDPI GitHub release metadata size is outside the fixed R42.7 limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("ByeDPI GitHub release metadata root must be an object.");
            }

            var rootMap = ReadObject(root, RequiredRootProperties, "root");
            var releaseId = PositiveInt64(rootMap["id"], "id");
            var tag = RequiredString(rootMap["tag_name"], "tag_name", 96);
            var draft = RequiredBoolean(rootMap["draft"], "draft");
            var prerelease = RequiredBoolean(rootMap["prerelease"], "prerelease");
            var immutable = RequiredBoolean(rootMap["immutable"], "immutable");

            var assetsElement = rootMap["assets"];
            if (assetsElement.ValueKind != JsonValueKind.Array
                || assetsElement.GetArrayLength() is <= 0 or > MaxAssetsInMetadata)
            {
                throw Invalid("ByeDPI GitHub release assets must be a bounded non-empty array.");
            }

            var assets = new List<ByeDpiAcquisitionAssetMetadata>(assetsElement.GetArrayLength());
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                if (assetElement.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("ByeDPI GitHub release asset entry must be an object.");
                }

                var assetMap = ReadObject(assetElement, RequiredAssetProperties, "asset");
                var assetId = PositiveInt64(assetMap["id"], "asset.id");
                var name = RequiredString(assetMap["name"], "asset.name", 160);
                var state = RequiredString(assetMap["state"], "asset.state", 32);
                var sizeBytes = PositiveInt64(assetMap["size"], "asset.size");
                var digest = RequiredString(assetMap["digest"], "asset.digest", 96);
                var sha256Hex = ParseSha256Digest(digest);
                var assetApiUri = RequiredAbsoluteUri(assetMap["url"], "asset.url");

                assets.Add(new ByeDpiAcquisitionAssetMetadata(
                    assetId,
                    name,
                    sizeBytes,
                    sha256Hex,
                    state,
                    assetApiUri));
            }

            return new ByeDpiAcquisitionReleaseMetadata(
                releaseId,
                tag,
                draft,
                prerelease,
                immutable,
                assets.AsReadOnly());
        }
        catch (JsonException exception)
        {
            throw new UpdateSecurityException(
                "ByeDpiGithubMetadataJsonInvalid",
                "ByeDPI GitHub release metadata is not strict JSON.",
                exception);
        }
    }

    private static Dictionary<string, JsonElement> ReadObject(
        JsonElement element,
        IReadOnlyCollection<string> requiredProperties,
        string context)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!values.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"ByeDPI GitHub release metadata {context} contains a duplicate property.");
            }
        }

        foreach (var required in requiredProperties)
        {
            if (!values.ContainsKey(required))
            {
                throw Invalid($"ByeDPI GitHub release metadata {context} is missing a required property.");
            }
        }

        return values;
    }

    private static string RequiredString(JsonElement element, string field, int maxLength)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"ByeDPI GitHub release metadata {field} must be a string.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw Invalid($"ByeDPI GitHub release metadata {field} has an invalid length/value.");
        }

        return value;
    }

    private static bool RequiredBoolean(JsonElement element, string field)
    {
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw Invalid($"ByeDPI GitHub release metadata {field} must be boolean.");
        }

        return element.GetBoolean();
    }

    private static long PositiveInt64(JsonElement element, string field)
    {
        if (element.ValueKind != JsonValueKind.Number
            || !element.TryGetInt64(out var value)
            || value <= 0)
        {
            throw Invalid($"ByeDPI GitHub release metadata {field} must be a positive integer.");
        }

        return value;
    }

    private static Uri RequiredAbsoluteUri(JsonElement element, string field)
    {
        var value = RequiredString(element, field, 512);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw Invalid($"ByeDPI GitHub release metadata {field} must be a canonical HTTPS URI.");
        }

        return uri;
    }

    private static string ParseSha256Digest(string digest)
    {
        const string prefix = "sha256:";
        if (!digest.StartsWith(prefix, StringComparison.Ordinal)
            || digest.Length != prefix.Length + 64)
        {
            throw Invalid("ByeDPI GitHub release asset digest must use exact sha256:<64-lowercase-hex> form.");
        }

        var hex = digest[prefix.Length..];
        if (hex.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw Invalid("ByeDPI GitHub release asset digest must be lowercase SHA-256 hex.");
        }

        return hex;
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiGithubMetadataInvalid", message);
}
