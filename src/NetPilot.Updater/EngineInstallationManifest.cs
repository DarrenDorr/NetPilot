using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetPilot.Updater;

public sealed record EngineInstallationManifest(
    int SchemaVersion,
    string UpstreamId,
    string Version,
    string Sha256Hex,
    DateTimeOffset InstalledAtUtc)
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = ".netpilot-engine.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8,
        WriteIndented = true
    };

    public static EngineInstallationManifest CreateZapret(GithubZapretRelease release) =>
        new(
            CurrentSchemaVersion,
            TrustedUpstreams.FlowsealZapret.Id,
            release.Version,
            release.Asset.Sha256Hex,
            DateTimeOffset.UtcNow);

    public static async Task WriteNewAsync(
        string engineRoot,
        EngineInstallationManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);
        ArgumentNullException.ThrowIfNull(manifest);
        Validate(manifest);

        var path = Path.Combine(Path.GetFullPath(engineRoot), FileName);
        if (File.Exists(path))
        {
            throw new UpdateSecurityException(
                "InstallManifestCollision",
                "Пакет уже содержит зарезервированный manifest NetPilot.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);

        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<EngineInstallationManifest> ReadAsync(
        string engineRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineRoot);
        var path = Path.Combine(Path.GetFullPath(engineRoot), FileName);

        var info = new FileInfo(path);
        if (!info.Exists || info.Length is <= 0 or > 16 * 1024)
        {
            throw new UpdateSecurityException(
                "InstalledVersionUnknown",
                "Установленный движок не содержит корректный manifest NetPilot.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        try
        {
            var manifest = await JsonSerializer.DeserializeAsync<EngineInstallationManifest>(
                stream,
                JsonOptions,
                cancellationToken)
                ?? throw new UpdateSecurityException(
                    "InvalidInstallManifest",
                    "Manifest установленного движка пуст.");

            Validate(manifest);
            return manifest;
        }
        catch (JsonException exception)
        {
            throw new UpdateSecurityException(
                "InvalidInstallManifest",
                "Manifest установленного движка повреждён или имеет неизвестную схему.",
                exception);
        }
    }

    private static void Validate(EngineInstallationManifest manifest)
    {
        if (manifest.SchemaVersion != CurrentSchemaVersion
            || !string.Equals(
                manifest.UpstreamId,
                TrustedUpstreams.FlowsealZapret.Id,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.Version)
            || manifest.Version.Length > 64
            || string.IsNullOrWhiteSpace(manifest.Sha256Hex)
            || manifest.Sha256Hex.Length != 64
            || manifest.Sha256Hex.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new UpdateSecurityException(
                "InvalidInstallManifest",
                "Manifest установленного движка не прошёл проверку.");
        }
    }
}
