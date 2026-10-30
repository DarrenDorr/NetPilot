using System.Text.Json;

namespace NetPilot.Updater;

public sealed record ByeDpiImmutableReleaseAsset(
    string Name,
    long SizeBytes,
    string Sha256Hex);

/// <summary>
/// Read-only identity of a future immutable ByeDPI distribution release.
/// This descriptor is metadata only: it does not discover, download, publish, stage or execute anything.
/// </summary>
public sealed record ByeDpiImmutableReleaseDescriptor(
    string EngineId,
    string Version,
    string Architecture,
    string RepositoryOwner,
    string RepositoryName,
    string ReleaseTag,
    string ReviewedSourceCommit,
    string ReviewedSourceTree,
    ByeDpiImmutableReleaseAsset Payload,
    ByeDpiImmutableReleaseAsset Manifest,
    ByeDpiImmutableReleaseAsset Provenance);

/// <summary>
/// Strict deterministic document for the immutable distribution boundary introduced by R42.6.
/// Unknown/duplicate fields, non-canonical identity, asset-name drift and digest/size ambiguity are rejected.
/// No network, filesystem mutation, service/IPC wiring or native execution is performed here.
/// </summary>
public static class ByeDpiImmutableReleaseDocument
{
    public const int SchemaVersion = 1;
    public const int MaxDocumentBytes = 64 * 1024;

    public const string RequiredEngineId = "byedpi";
    public const string RequiredArchitecture = "x64";
    public const string RequiredRepositoryOwner = "DarrenDorr";
    public const string RequiredRepositoryName = "NetPilot";

    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "engineId",
        "version",
        "architecture",
        "repositoryOwner",
        "repositoryName",
        "releaseTag",
        "reviewedSourceCommit",
        "reviewedSourceTree",
        "payload",
        "manifest",
        "provenance"
    ];

    private static readonly string[] AssetProperties =
    [
        "name",
        "sizeBytes",
        "sha256Hex"
    ];

    public static byte[] Serialize(ByeDpiImmutableReleaseDescriptor descriptor)
    {
        ValidateDescriptor(descriptor);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("engineId", descriptor.EngineId);
            writer.WriteString("version", descriptor.Version);
            writer.WriteString("architecture", descriptor.Architecture);
            writer.WriteString("repositoryOwner", descriptor.RepositoryOwner);
            writer.WriteString("repositoryName", descriptor.RepositoryName);
            writer.WriteString("releaseTag", descriptor.ReleaseTag);
            writer.WriteString("reviewedSourceCommit", descriptor.ReviewedSourceCommit);
            writer.WriteString("reviewedSourceTree", descriptor.ReviewedSourceTree);
            WriteAsset(writer, "payload", descriptor.Payload);
            WriteAsset(writer, "manifest", descriptor.Manifest);
            WriteAsset(writer, "provenance", descriptor.Provenance);
            writer.WriteEndObject();
        }

        if (buffer.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new UpdateSecurityException(
                "ByeDpiImmutableReleaseDocumentSize",
                "Serialized ByeDPI immutable release document exceeds the fixed metadata limit.");
        }

        return buffer.ToArray();
    }

    public static ByeDpiImmutableReleaseDescriptor Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > MaxDocumentBytes)
        {
            throw new UpdateSecurityException(
                "ByeDpiImmutableReleaseDocumentSize",
                "ByeDPI immutable release document size is outside the fixed metadata limit.");
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("ByeDPI immutable release document root must be an object.");
            }

            var rootMap = ReadExactObject(root, RootProperties, "root");
            if (!rootMap["schemaVersion"].TryGetInt32(out var schemaVersion)
                || schemaVersion != SchemaVersion)
            {
                throw new UpdateSecurityException(
                    "ByeDpiImmutableReleaseDocumentSchema",
                    "ByeDPI immutable release document schema version is unsupported.");
            }

            var descriptor = new ByeDpiImmutableReleaseDescriptor(
                RequiredString(rootMap["engineId"], "engineId", 32),
                RequiredString(rootMap["version"], "version", 32),
                RequiredString(rootMap["architecture"], "architecture", 16),
                RequiredString(rootMap["repositoryOwner"], "repositoryOwner", 64),
                RequiredString(rootMap["repositoryName"], "repositoryName", 64),
                RequiredString(rootMap["releaseTag"], "releaseTag", 96),
                RequiredString(rootMap["reviewedSourceCommit"], "reviewedSourceCommit", 40),
                RequiredString(rootMap["reviewedSourceTree"], "reviewedSourceTree", 40),
                ReadAsset(rootMap["payload"], "payload"),
                ReadAsset(rootMap["manifest"], "manifest"),
                ReadAsset(rootMap["provenance"], "provenance"));

            ValidateDescriptor(descriptor);
            return descriptor;
        }
        catch (JsonException exception)
        {
            throw new UpdateSecurityException(
                "ByeDpiImmutableReleaseDocumentJsonInvalid",
                "ByeDPI immutable release document is not strict JSON.",
                exception);
        }
    }

    private static void ValidateDescriptor(ByeDpiImmutableReleaseDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!string.Equals(descriptor.EngineId, RequiredEngineId, StringComparison.Ordinal)
            || !string.Equals(descriptor.Architecture, RequiredArchitecture, StringComparison.Ordinal)
            || !string.Equals(descriptor.RepositoryOwner, RequiredRepositoryOwner, StringComparison.Ordinal)
            || !string.Equals(descriptor.RepositoryName, RequiredRepositoryName, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI immutable release identity is outside the fixed NetPilot distribution boundary.");
        }

        ValidateCanonicalVersion(descriptor.Version);

        var expectedTag = $"byedpi-v{descriptor.Version}";
        if (!string.Equals(descriptor.ReleaseTag, expectedTag, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI immutable release tag does not match the canonical version identity.");
        }

        ValidateLowercaseHex(descriptor.ReviewedSourceCommit, 40, "reviewedSourceCommit");
        ValidateLowercaseHex(descriptor.ReviewedSourceTree, 40, "reviewedSourceTree");

        ValidateAsset(
            descriptor.Payload,
            $"NetPilot-ByeDPI-{descriptor.Version}-x64.zip",
            "payload");
        ValidateAsset(
            descriptor.Manifest,
            $"NetPilot-ByeDPI-{descriptor.Version}-x64.manifest.json",
            "manifest");
        ValidateAsset(
            descriptor.Provenance,
            $"NetPilot-ByeDPI-{descriptor.Version}-x64.provenance.json",
            "provenance");

        if (string.Equals(descriptor.Payload.Sha256Hex, descriptor.Manifest.Sha256Hex, StringComparison.Ordinal)
            || string.Equals(descriptor.Payload.Sha256Hex, descriptor.Provenance.Sha256Hex, StringComparison.Ordinal)
            || string.Equals(descriptor.Manifest.Sha256Hex, descriptor.Provenance.Sha256Hex, StringComparison.Ordinal))
        {
            throw Invalid("ByeDPI immutable release assets must have distinct SHA-256 identities.");
        }
    }

    private static void ValidateCanonicalVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            throw Invalid("ByeDPI immutable release version is invalid.");
        }

        var segments = value.Split('.', StringSplitOptions.None);
        if (segments.Length != 3)
        {
            throw Invalid("ByeDPI immutable release version must be canonical major.minor.patch.");
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0
                || segment.Any(static character => character is < '0' or > '9')
                || (segment.Length > 1 && segment[0] == '0')
                || !uint.TryParse(segment, out _))
            {
                throw Invalid("ByeDPI immutable release version must be canonical major.minor.patch.");
            }
        }
    }

    private static void ValidateAsset(
        ByeDpiImmutableReleaseAsset asset,
        string expectedName,
        string context)
    {
        if (asset is null)
        {
            throw Invalid($"ByeDPI immutable release {context} asset is required.");
        }
        if (!string.Equals(asset.Name, expectedName, StringComparison.Ordinal))
        {
            throw Invalid($"ByeDPI immutable release {context} asset name is not canonical.");
        }
        if (asset.SizeBytes <= 0)
        {
            throw Invalid($"ByeDPI immutable release {context} asset size must be positive.");
        }
        ValidateLowercaseHex(asset.Sha256Hex, 64, $"{context}.sha256Hex");
    }

    private static ByeDpiImmutableReleaseAsset ReadAsset(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"ByeDPI immutable release {context} asset must be an object.");
        }

        var map = ReadExactObject(element, AssetProperties, context);
        var name = RequiredString(map["name"], $"{context}.name", 160);
        if (!map["sizeBytes"].TryGetInt64(out var sizeBytes))
        {
            throw Invalid($"ByeDPI immutable release {context}.sizeBytes must be an integer.");
        }
        var sha256 = RequiredString(map["sha256Hex"], $"{context}.sha256Hex", 64);
        return new ByeDpiImmutableReleaseAsset(name, sizeBytes, sha256);
    }

    private static void WriteAsset(Utf8JsonWriter writer, string propertyName, ByeDpiImmutableReleaseAsset asset)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteString("name", asset.Name);
        writer.WriteNumber("sizeBytes", asset.SizeBytes);
        writer.WriteString("sha256Hex", asset.Sha256Hex);
        writer.WriteEndObject();
    }

    private static Dictionary<string, JsonElement> ReadExactObject(
        JsonElement element,
        IReadOnlyCollection<string> expectedProperties,
        string context)
    {
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expected.Contains(property.Name))
            {
                throw Invalid($"ByeDPI immutable release {context} contains an unknown property.");
            }
            if (!values.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"ByeDPI immutable release {context} contains a duplicate property.");
            }
        }

        if (values.Count != expected.Count || expected.Any(name => !values.ContainsKey(name)))
        {
            throw Invalid($"ByeDPI immutable release {context} is missing a required property.");
        }
        return values;
    }

    private static string RequiredString(JsonElement element, string field, int maxLength)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"ByeDPI immutable release {field} must be a string.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw Invalid($"ByeDPI immutable release {field} has an invalid length/value.");
        }
        return value;
    }

    private static void ValidateLowercaseHex(string value, int length, string field)
    {
        if (value.Length != length
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw Invalid($"ByeDPI immutable release {field} must be lowercase hexadecimal with exact length {length}.");
        }
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiImmutableReleaseDocumentInvalid", message);
}
