using System.Security.AccessControl;

namespace NetPilot.Platform.Windows;

public sealed class WindowsByeDpiStagingSecurityVerification
{
    internal WindowsByeDpiStagingSecurityVerification(
        string transactionId,
        string stagingRoot,
        int verifiedObjectCount)
    {
        TransactionId = transactionId;
        StagingRoot = stagingRoot;
        VerifiedObjectCount = verifiedObjectCount;
    }

    public string TransactionId { get; }

    public string StagingRoot { get; }

    public int VerifiedObjectCount { get; }
}

/// <summary>
/// Read-only Windows gate для будущего ByeDPI staging transaction.
/// Public API принимает только service-generated transaction id и exact manifest paths;
/// абсолютный base path, URL, executable path или arguments не принимаются.
/// Ничего не создаёт, не меняет ACL и ничего не запускает.
/// </summary>
public static class WindowsByeDpiProtectedStagingSecurityVerifier
{
    private const int TransactionIdLength = 32;

    public static string ResolveTransactionRoot(string transactionId)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "ByeDPI protected staging доступен только в Windows.");
        }

        ValidateTransactionId(transactionId);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ProgramFilesUnavailable",
                "Не удалось определить системный Program Files для ByeDPI staging.");
        }

        var stagingBase = NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Staging", "ByeDPI"));
        var transactionRoot = NormalizeDirectory(Path.Combine(stagingBase, transactionId));
        EnsureStrictChild(stagingBase, transactionRoot, "ByeDpiStagingRootOutsideBoundary");
        return transactionRoot;
    }

    public static WindowsByeDpiStagingSecurityVerification Verify(
        string transactionId,
        IReadOnlyCollection<string> exactRelativeFilePaths)
    {
        ArgumentNullException.ThrowIfNull(exactRelativeFilePaths);
        if (exactRelativeFilePaths.Count is <= 0 or > 64)
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingFileCountInvalid",
                "Количество exact ByeDPI staging files выходит за допустимый boundary.");
        }

        var root = ResolveTransactionRoot(transactionId);
        if (!Directory.Exists(root))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingRootMissing",
                "Ожидаемый protected ByeDPI staging transaction не существует.");
        }

        var programFiles = NormalizeDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var verifiedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectoryChain(programFiles, root, verifiedDirectories);

        var verifiedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in exactRelativeFilePaths)
        {
            var platformRelativePath = ValidateRelativeManifestPath(relativePath)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(root, platformRelativePath));
            EnsureStrictChild(root, fullPath, "ByeDpiStagingFileOutsideBoundary");

            if (!verifiedFiles.Add(fullPath))
            {
                throw new WindowsEngineSecurityVerificationException(
                    "ByeDpiStagingPathCollision",
                    "Exact ByeDPI staging file set содержит duplicate/case-collision path.");
            }

            var parent = Path.GetDirectoryName(fullPath)
                ?? throw new WindowsEngineSecurityVerificationException(
                    "ByeDpiStagingParentMissing",
                    "Не удалось определить parent directory ByeDPI staging file.");
            AddDirectoryChain(root, NormalizeDirectory(parent), verifiedDirectories);
        }

        foreach (var directory in verifiedDirectories
                     .OrderBy(static path => path.Length)
                     .ThenBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            VerifyFilesystemObject(directory, expectDirectory: true);
        }

        foreach (var file in verifiedFiles.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            VerifyFilesystemObject(file, expectDirectory: false);
        }

        return new WindowsByeDpiStagingSecurityVerification(
            transactionId,
            root,
            verifiedDirectories.Count + verifiedFiles.Count);
    }

    private static string ValidateRelativeManifestPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 240
            || value.StartsWith('/', StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains(':')
            || value.Contains('\0'))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingRelativePathInvalid",
                "ByeDPI staging manifest path недопустим для Windows boundary.");
        }

        var segments = value.Split('/', StringSplitOptions.None);
        foreach (var segment in segments)
        {
            if (segment.Length is <= 0 or > 80
                || segment is "." or ".."
                || segment.Any(static character =>
                    !(character is >= 'a' and <= 'z'
                        or >= 'A' and <= 'Z'
                        or >= '0' and <= '9'
                        or '.' or '_' or '-')))
            {
                throw new WindowsEngineSecurityVerificationException(
                    "ByeDpiStagingRelativePathInvalid",
                    "ByeDPI staging manifest path содержит неоднозначный segment.");
            }
        }

        return string.Join('/', segments);
    }

    private static void ValidateTransactionId(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId)
            || transactionId.Length != TransactionIdLength
            || transactionId.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingTransactionIdInvalid",
                "ByeDPI staging transaction id должен быть 32-character lowercase hex token.");
        }
    }

    private static void AddDirectoryChain(
        string root,
        string target,
        ISet<string> output)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedTarget = NormalizeDirectory(target);
        EnsureInsideOrEqual(normalizedRoot, normalizedTarget, "ByeDpiStagingDirectoryOutsideBoundary");
        output.Add(normalizedRoot);

        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            if (string.IsNullOrEmpty(segment) || segment is "." or "..")
            {
                throw new WindowsEngineSecurityVerificationException(
                    "ByeDpiStagingDirectoryChainInvalid",
                    "Protected ByeDPI staging directory chain содержит неоднозначный segment.");
            }

            current = NormalizeDirectory(Path.Combine(current, segment));
            EnsureInsideOrEqual(normalizedRoot, current, "ByeDpiStagingDirectoryOutsideBoundary");
            output.Add(current);
        }
    }

    private static void VerifyFilesystemObject(string path, bool expectDirectory)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingSecurityObjectMissing",
                "Filesystem object исчез до staging ACL verification.");
        }

        var attributes = File.GetAttributes(fullPath);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        if (isDirectory != expectDirectory)
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingSecurityObjectTypeMismatch",
                "ByeDPI staging object имеет неожиданный filesystem type.");
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingReparsePointRejected",
                "Reparse point в protected ByeDPI staging chain запрещён.");
        }

        FileSystemSecurity security = expectDirectory
            ? FileSystemAclExtensions.GetAccessControl(
                new DirectoryInfo(fullPath),
                AccessControlSections.Access | AccessControlSections.Owner)
            : FileSystemAclExtensions.GetAccessControl(
                new FileInfo(fullPath),
                AccessControlSections.Access | AccessControlSections.Owner);

        WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor(security);

        var afterAttributes = File.GetAttributes(fullPath);
        if (((afterAttributes & FileAttributes.Directory) != 0) != expectDirectory
            || (afterAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsEngineSecurityVerificationException(
                "ByeDpiStagingSecurityObjectChanged",
                "ByeDPI staging object изменился во время ACL verification.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void EnsureInsideOrEqual(string root, string candidate, string code)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = NormalizeDirectory(candidate);
        if (!string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            && !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineSecurityVerificationException(
                code,
                "ByeDPI staging path вышел за пределы protected boundary.");
        }
    }

    private static void EnsureStrictChild(string root, string candidate, string code)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsEngineSecurityVerificationException(
                code,
                "ByeDPI staging path не является strict child protected boundary.");
        }
    }
}
