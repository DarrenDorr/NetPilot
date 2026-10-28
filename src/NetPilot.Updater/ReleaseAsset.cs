using System.Globalization;
using System.Text.RegularExpressions;

namespace NetPilot.Updater;

public sealed record ReleaseAsset
{
    private static readonly Regex Sha256Pattern = new(
        "\\A[0-9a-f]{64}\\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ReleaseAsset(
        string version,
        string fileName,
        Uri downloadUri,
        long sizeBytes,
        string sha256Hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(downloadUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);

        if (version.Length > 64 || fileName.Length > 160)
        {
            throw new ArgumentException("Метаданные release asset имеют недопустимый размер.");
        }

        if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Для Windows разрешён только безопасный ZIP asset с корректным именем.", nameof(fileName));
        }

        if (!IsTrustedFlowsealDownloadUri(downloadUri, version, fileName))
        {
            throw new ArgumentException("Asset не относится к разрешённому официальному релизу Flowseal.", nameof(downloadUri));
        }

        if (sizeBytes is <= 0 or > UpdateSecurityLimits.MaxDownloadBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeBytes), "Размер asset выходит за безопасный лимит.");
        }

        var normalizedSha256 = sha256Hex.Trim().ToLower(CultureInfo.InvariantCulture);
        if (normalizedSha256.StartsWith("sha256:", StringComparison.Ordinal))
        {
            normalizedSha256 = normalizedSha256[7..];
        }

        if (!Sha256Pattern.IsMatch(normalizedSha256))
        {
            throw new ArgumentException("Digest asset должен содержать корректный SHA-256.", nameof(sha256Hex));
        }

        Version = version;
        FileName = fileName;
        DownloadUri = downloadUri;
        SizeBytes = sizeBytes;
        Sha256Hex = normalizedSha256;
    }

    public string Version { get; }
    public string FileName { get; }
    public Uri DownloadUri { get; }
    public long SizeBytes { get; }
    public string Sha256Hex { get; }

    private static bool IsTrustedFlowsealDownloadUri(Uri uri, string version, string fileName)
    {
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var expectedPath = $"/Flowseal/zapret-discord-youtube/releases/download/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(fileName)}";
        return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal);
    }
}

public static class UpdateSecurityLimits
{
    public const long MaxDownloadBytes = 32L * 1024 * 1024;
    public const int MaxArchiveEntries = 4096;
    public const long MaxSingleExtractedFileBytes = 64L * 1024 * 1024;
    public const long MaxTotalExtractedBytes = 256L * 1024 * 1024;
    public const int MaxArchivePathCharacters = 240;
    public const int MaxCompressionRatio = 200;
}
