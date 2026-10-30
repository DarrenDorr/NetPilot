using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NetPilot.Updater;

public sealed record ByeDpiPackageFileManifest
{
    private static readonly Regex Sha256Pattern = new(
        "\\A[0-9a-f]{64}\\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ByeDpiPackageFileManifest(string relativePath, long sizeBytes, string sha256Hex)
    {
        RelativePath = ByeDpiPackageManifest.NormalizeRelativePath(relativePath);

        if (sizeBytes is <= 0 or > UpdateSecurityLimits.MaxSingleExtractedFileBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeBytes),
                "Размер manifest file выходит за безопасный лимит.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);
        var normalizedHash = sha256Hex.Trim().ToLower(CultureInfo.InvariantCulture);
        if (normalizedHash.StartsWith("sha256:", StringComparison.Ordinal))
        {
            normalizedHash = normalizedHash[7..];
        }

        if (!Sha256Pattern.IsMatch(normalizedHash))
        {
            throw new ArgumentException(
                "Manifest file digest должен содержать корректный SHA-256.",
                nameof(sha256Hex));
        }

        SizeBytes = sizeBytes;
        Sha256Hex = normalizedHash;
    }

    public string RelativePath { get; }

    public long SizeBytes { get; }

    public string Sha256Hex { get; }
}

/// <summary>
/// Exact release/package manifest для одного ByeDPI x64 package tree.
/// Это metadata verification boundary, а не admission или launch token.
/// </summary>
public sealed class ByeDpiPackageManifest
{
    public const string RequiredEngineId = "byedpi";
    public const string RequiredArchitecture = "x64";
    public const string RequiredExecutableRelativePath = "bin/ciadpi.exe";
    public const string RequiredLicenseRelativePath = "LICENSE";
    public const int MaxManifestFiles = 64;

    public ByeDpiPackageManifest(
        string engineId,
        string version,
        string architecture,
        string executableRelativePath,
        string licenseRelativePath,
        IEnumerable<ByeDpiPackageFileManifest> files)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(architecture);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableRelativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseRelativePath);
        ArgumentNullException.ThrowIfNull(files);

        if (!string.Equals(engineId, RequiredEngineId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Manifest относится не к ByeDPI.", nameof(engineId));
        }

        if (!IsCanonicalVersion(version))
        {
            throw new ArgumentException(
                "ByeDPI manifest version должна иметь canonical major.minor.patch format.",
                nameof(version));
        }

        if (!string.Equals(architecture, RequiredArchitecture, StringComparison.Ordinal))
        {
            throw new ArgumentException("R42.1 разрешает только x64 ByeDPI package.", nameof(architecture));
        }

        var executable = NormalizeRelativePath(executableRelativePath);
        var license = NormalizeRelativePath(licenseRelativePath);
        if (!string.Equals(executable, RequiredExecutableRelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ByeDPI executable должен находиться только в bin/ciadpi.exe.",
                nameof(executableRelativePath));
        }

        if (!string.Equals(license, RequiredLicenseRelativePath, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "ByeDPI license notice должен находиться только в LICENSE.",
                nameof(licenseRelativePath));
        }

        var materialized = files.ToArray();
        if (materialized.Length is <= 0 or > MaxManifestFiles)
        {
            throw new ArgumentException(
                "Количество файлов ByeDPI package manifest выходит за безопасный лимит.",
                nameof(files));
        }

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        foreach (var file in materialized)
        {
            ArgumentNullException.ThrowIfNull(file);
            if (!unique.Add(file.RelativePath))
            {
                throw new ArgumentException(
                    "ByeDPI package manifest содержит duplicate/case-collision path.",
                    nameof(files));
            }

            checked
            {
                totalBytes += file.SizeBytes;
            }
        }

        if (totalBytes > UpdateSecurityLimits.MaxTotalExtractedBytes)
        {
            throw new ArgumentException(
                "Суммарный размер ByeDPI package manifest превышает безопасный лимит.",
                nameof(files));
        }

        if (!unique.Contains(executable))
        {
            throw new ArgumentException(
                "ByeDPI package manifest не содержит обязательный ciadpi.exe.",
                nameof(files));
        }

        if (!unique.Contains(license))
        {
            throw new ArgumentException(
                "ByeDPI package manifest не содержит обязательный LICENSE.",
                nameof(files));
        }

        EngineId = RequiredEngineId;
        Version = version;
        Architecture = RequiredArchitecture;
        ExecutableRelativePath = executable;
        LicenseRelativePath = license;
        Files = Array.AsReadOnly(
            materialized
                .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
                .ToArray());
        TotalSizeBytes = totalBytes;
        CanonicalManifestSha256 = ComputeCanonicalManifestSha256();
    }

    public string EngineId { get; }

    public string Version { get; }

    public string Architecture { get; }

    public string ExecutableRelativePath { get; }

    public string LicenseRelativePath { get; }

    public IReadOnlyList<ByeDpiPackageFileManifest> Files { get; }

    public long TotalSizeBytes { get; }

    public string CanonicalManifestSha256 { get; }

    internal static string NormalizeRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > UpdateSecurityLimits.MaxArchivePathCharacters
            || value[0] == '/'
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("Manifest содержит недопустимый relative path.", nameof(value));
        }

        var segments = value.Split('/', StringSplitOptions.None);
        if (segments.Length == 0)
        {
            throw new ArgumentException("Manifest path не содержит сегментов.", nameof(value));
        }

        foreach (var segment in segments)
        {
            if (segment.Length is <= 0 or > 80
                || segment is "." or ".."
                || IsWindowsReservedDeviceName(segment)
                || segment.Any(static character =>
                    !(character is >= 'a' and <= 'z'
                        or >= 'A' and <= 'Z'
                        or >= '0' and <= '9'
                        or '.' or '_' or '-')))
            {
                throw new ArgumentException(
                    "Manifest path содержит неоднозначный или неподдерживаемый сегмент.",
                    nameof(value));
            }
        }

        return string.Join('/', segments);
    }

    private static bool IsCanonicalVersion(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (var part in parts)
        {
            if (part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(
                    part,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsWindowsReservedDeviceName(string segment)
    {
        var dotIndex = segment.IndexOf('.');
        var baseName = dotIndex >= 0 ? segment[..dotIndex] : segment;

        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return baseName.Length == 4
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && baseName[3] is >= '1' and <= '9';
    }

    private string ComputeCanonicalManifestSha256()
    {
        var builder = new StringBuilder();
        builder.Append(EngineId).Append('\n')
            .Append(Version).Append('\n')
            .Append(Architecture).Append('\n')
            .Append(ExecutableRelativePath).Append('\n')
            .Append(LicenseRelativePath).Append('\n');

        foreach (var file in Files)
        {
            builder.Append(file.RelativePath)
                .Append('\t')
                .Append(file.SizeBytes.ToString(CultureInfo.InvariantCulture))
                .Append('\t')
                .Append(file.Sha256Hex)
                .Append('\n');
        }

        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();
    }
}

/// <summary>
/// Read-only proof that a staging tree exactly matched one manifest at verification time.
/// It is not sufficient for execution; Windows ACL/owner proof is a separate gate.
/// </summary>
public sealed class ByeDpiVerifiedStagingTree
{
    internal ByeDpiVerifiedStagingTree(
        string rootPath,
        ByeDpiPackageManifest manifest,
        IReadOnlyList<string> relativePaths)
    {
        RootPath = rootPath;
        Manifest = manifest;
        RelativePaths = relativePaths;
    }

    public string RootPath { get; }

    public ByeDpiPackageManifest Manifest { get; }

    public IReadOnlyList<string> RelativePaths { get; }

    public int VerifiedFileCount => RelativePaths.Count;

    public long VerifiedTotalBytes => Manifest.TotalSizeBytes;
}

public static class ByeDpiPackageManifestVerifier
{
    public static async Task<ByeDpiVerifiedStagingTree> VerifyAsync(
        string stagingRoot,
        ByeDpiPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingRoot);
        ArgumentNullException.ThrowIfNull(manifest);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
        if (!Directory.Exists(root))
        {
            throw new UpdateSecurityException(
                "ByeDpiStagingMissing",
                "ByeDPI staging directory не существует.");
        }

        RejectReparsePoint(root);

        var expected = manifest.Files.ToDictionary(
            static file => file.RelativePath,
            StringComparer.OrdinalIgnoreCase);
        var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>();
        directories.Push(root);

        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            RejectReparsePoint(directory);

            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullPath = Path.GetFullPath(entry);
                EnsureStrictChild(root, fullPath);

                var attributes = File.GetAttributes(fullPath);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UpdateSecurityException(
                        "ByeDpiStagingReparsePoint",
                        "ByeDPI staging tree содержит reparse point.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    directories.Push(fullPath);
                    continue;
                }

                var relative = Path.GetRelativePath(root, fullPath)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
                {
                    relative = relative.Replace(Path.AltDirectorySeparatorChar, '/');
                }

                string normalized;
                try
                {
                    normalized = ByeDpiPackageManifest.NormalizeRelativePath(relative);
                }
                catch (ArgumentException exception)
                {
                    throw new UpdateSecurityException(
                        "ByeDpiStagingInvalidPath",
                        "ByeDPI staging tree содержит path, который не может входить в exact manifest.",
                        exception);
                }

                if (!actual.TryAdd(normalized, fullPath))
                {
                    throw new UpdateSecurityException(
                        "ByeDpiStagingPathCollision",
                        "ByeDPI staging tree содержит duplicate/case-collision path.");
                }

                if (actual.Count > ByeDpiPackageManifest.MaxManifestFiles)
                {
                    throw new UpdateSecurityException(
                        "ByeDpiStagingFileLimit",
                        "ByeDPI staging tree превышает безопасный file-count limit.");
                }
            }
        }

        if (actual.Count != expected.Count)
        {
            throw new UpdateSecurityException(
                "ByeDpiManifestFileSetMismatch",
                "Фактический ByeDPI staging tree не совпадает с exact manifest file set.");
        }

        foreach (var expectedFile in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!actual.TryGetValue(expectedFile.RelativePath, out var fullPath))
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestFileMissing",
                    "Один из обязательных файлов ByeDPI manifest отсутствует в staging.");
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            if (stream.Length != expectedFile.SizeBytes)
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestSizeMismatch",
                    "Размер ByeDPI staging file не совпадает с exact manifest.");
            }

            var digest = await SHA256.HashDataAsync(stream, cancellationToken);
            var actualSha256 = Convert.ToHexString(digest).ToLowerInvariant();
            if (!string.Equals(
                    actualSha256,
                    expectedFile.Sha256Hex,
                    StringComparison.Ordinal))
            {
                throw new UpdateSecurityException(
                    "ByeDpiManifestHashMismatch",
                    "SHA-256 ByeDPI staging file не совпадает с exact manifest.");
            }

            var afterAttributes = File.GetAttributes(fullPath);
            if ((afterAttributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            {
                throw new UpdateSecurityException(
                    "ByeDpiStagingObjectChanged",
                    "ByeDPI staging object изменился во время verification.");
            }
        }

        var sorted = actual.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        return new ByeDpiVerifiedStagingTree(
            root,
            manifest,
            Array.AsReadOnly(sorted));
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ByeDpiStagingReparsePoint",
                "ByeDPI staging boundary не допускает reparse point.");
        }
    }

    private static void EnsureStrictChild(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(normalizedRoot, normalizedCandidate, comparison)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                comparison))
        {
            throw new UpdateSecurityException(
                "ByeDpiStagingTraversal",
                "ByeDPI staging object вышел за пределы staging root.");
        }
    }
}
