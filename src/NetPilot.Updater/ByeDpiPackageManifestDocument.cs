using System.Text.Json;

namespace NetPilot.Updater;

/// <summary>
/// Strict, deterministic on-disk representation of ByeDpiPackageManifest.
/// This is package metadata only: it performs no network access, staging, admission or execution.
/// Unknown/duplicate fields and digest drift are rejected fail-closed.
/// </summary>
public static class ByeDpiPackageManifestDocument
{
    public const int SchemaVersion = 1;
    public const int MaxDocumentBytes = 64 * 1024;

    private static readonly string[] RootProperties =
    [
        "schemaVersion",
        "engineId",
        "version",
        "architecture",
        "executableRelativePath",
        "licenseRelativePath",
        "canonicalManifestSha256",
        "files"
    ];

    private static readonly string[] FileProperties =
    [
        "relativePath",
        "sizeBytes",
        "sha256Hex"
    ];

    public static byte[] Serialize(ByeDpiPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", SchemaVersion);
            writer.WriteString("engineId", manifest.EngineId);
            writer.WriteString("version", manifest.Version);
            writer.WriteString("architecture", manifest.Architecture);
            writer.WriteString("executableRelativePath", manifest.ExecutableRelativePath);
            writer.WriteString("licenseRelativePath", manifest.LicenseRelativePath);
            writer.WriteString("canonicalManifestSha256", manifest.CanonicalManifestSha256);
            writer.WriteStartArray("files");
            foreach (var file in manifest.Files)
            {
                writer.WriteStartObject();
                writer.WriteString("relativePath", file.RelativePath);
                writer.WriteNumber("sizeBytes", file.SizeBytes);
                writer.WriteString("sha256Hex", file.Sha256Hex);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        if (buffer.Length is <= 0 or > MaxDocumentBytes)
        {
            throw new UpdateSecurityException(
                "ByeDpiManifestDocumentSize",
                "Serialized ByeDPI manifest document exceeds the fixed metadata limit.");
        }

        return buffer.ToArray();
    }

    public static ByeDpiPackageManifest Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > MaxDocumentBytes)
        {
            throw new UpdateSecurityException(
                "ByeDpiManifestDocumentSize",
                "ByeDPI manifest document size is outside the fixed metadata limit.");
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
                throw Invalid("ByeDPI manifest document root must be an object.");
            }

            var rootMap = ReadExactObject(root, RootProperties, "root");
            if (!rootMap["schemaVersion"].TryGetInt32(out var schemaVersion)
                || schemaVersion != SchemaVersion)
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestDocumentSchema",
                    "ByeDPI manifest document schema version is unsupported.");
            }

            var engineId = RequiredString(rootMap["engineId"], "engineId", 32);
            var version = RequiredString(rootMap["version"], "version", 32);
            var architecture = RequiredString(rootMap["architecture"], "architecture", 16);
            var executableRelativePath = RequiredString(
                rootMap["executableRelativePath"],
                "executableRelativePath",
                240);
            var licenseRelativePath = RequiredString(
                rootMap["licenseRelativePath"],
                "licenseRelativePath",
                240);
            var persistedCanonicalDigest = RequiredString(
                rootMap["canonicalManifestSha256"],
                "canonicalManifestSha256",
                64);
            ValidateLowercaseSha256(persistedCanonicalDigest, "canonicalManifestSha256");

            var filesElement = rootMap["files"];
            if (filesElement.ValueKind != JsonValueKind.Array
                || filesElement.GetArrayLength() is <= 0 or > ByeDpiPackageManifest.MaxManifestFiles)
            {
                throw Invalid("ByeDPI manifest files must be a bounded non-empty array.");
            }

            var files = new List<ByeDpiPackageFileManifest>(filesElement.GetArrayLength());
            foreach (var fileElement in filesElement.EnumerateArray())
            {
                if (fileElement.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid("ByeDPI manifest file entry must be an object.");
                }

                var fileMap = ReadExactObject(fileElement, FileProperties, "file");
                var relativePath = RequiredString(fileMap["relativePath"], "relativePath", 240);
                if (!fileMap["sizeBytes"].TryGetInt64(out var sizeBytes))
                {
                    throw Invalid("ByeDPI manifest sizeBytes must be an integer.");
                }
                var sha256 = RequiredString(fileMap["sha256Hex"], "sha256Hex", 64);
                ValidateLowercaseSha256(sha256, "sha256Hex");
                files.Add(new ByeDpiPackageFileManifest(relativePath, sizeBytes, sha256));
            }

            ByeDpiPackageManifest manifest;
            try
            {
                manifest = new ByeDpiPackageManifest(
                    engineId,
                    version,
                    architecture,
                    executableRelativePath,
                    licenseRelativePath,
                    files);
            }
            catch (ArgumentException exception)
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestDocumentSemanticInvalid",
                    "ByeDPI manifest document failed semantic validation.",
                    exception);
            }

            if (!string.Equals(
                    manifest.CanonicalManifestSha256,
                    persistedCanonicalDigest,
                    StringComparison.Ordinal))
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestDocumentDigestMismatch",
                    "Persisted canonical ByeDPI manifest digest does not match reconstructed metadata.");
            }

            return manifest;
        }
        catch (JsonException exception)
        {
            throw new UpdateSecurityException(
                "ByeDpiManifestDocumentJsonInvalid",
                "ByeDPI manifest document is not strict JSON.",
                exception);
        }
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
                throw Invalid($"ByeDPI manifest {context} contains an unknown property.");
            }
            if (!values.TryAdd(property.Name, property.Value))
            {
                throw Invalid($"ByeDPI manifest {context} contains a duplicate property.");
            }
        }

        if (values.Count != expected.Count || expected.Any(name => !values.ContainsKey(name)))
        {
            throw Invalid($"ByeDPI manifest {context} is missing a required property.");
        }
        return values;
    }

    private static string RequiredString(JsonElement element, string field, int maxLength)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw Invalid($"ByeDPI manifest {field} must be a string.");
        }
        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
        {
            throw Invalid($"ByeDPI manifest {field} has an invalid length/value.");
        }
        return value;
    }

    private static void ValidateLowercaseSha256(string value, string field)
    {
        if (value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw Invalid($"ByeDPI manifest {field} must be lowercase SHA-256 hex.");
        }
    }

    private static UpdateSecurityException Invalid(string message) =>
        new("ByeDpiManifestDocumentInvalid", message);
}
