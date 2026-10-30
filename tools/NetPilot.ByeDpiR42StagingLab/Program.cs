using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NetPilot.Platform.Windows;
using NetPilot.Updater;

namespace NetPilot.ByeDpiR42StagingLab;

internal static class Program
{
    private const string LabMarkerName = ".r42-2-lab-owned";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 1)
        {
            return 2;
        }

        var outputPath = Path.GetFullPath(args[0]);
        var lines = new List<string>();

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("R42.2 LocalSystem lab is Windows-only.");
            }

            using var identity = WindowsIdentity.GetCurrent();
            if (!identity.IsSystem)
            {
                throw new UnauthorizedAccessException("R42.2 LocalSystem lab must run as LocalSystem.");
            }

            lines.Add("IDENTITY_LOCALSYSTEM=PASS");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                throw new InvalidOperationException("Program Files path is unavailable.");
            }

            var netPilotRoot = Path.Combine(programFiles, "NetPilot");
            if (Directory.Exists(netPilotRoot) || File.Exists(netPilotRoot))
            {
                throw new InvalidOperationException("R42.2 lab refuses a pre-existing Program Files\\NetPilot root.");
            }

            var files = CreateSyntheticFiles();
            var manifest = CreateManifest(files);
            await using var zip = CreateZip(files);

            var result = await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync(
                zip,
                manifest,
                CancellationToken.None);

            var stagingBase = Path.GetDirectoryName(result.StagingRoot)
                ?? throw new InvalidOperationException("Staging base could not be resolved.");
            var expectedBase = Path.Combine(programFiles, "NetPilot", "Engines", "Staging", "ByeDPI");
            if (!string.Equals(
                    Path.GetFullPath(stagingBase),
                    Path.GetFullPath(expectedBase),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Protected staging escaped the fixed Program Files boundary.");
            }

            if (!string.Equals(result.ManifestSha256, manifest.CanonicalManifestSha256, StringComparison.Ordinal)
                || result.VerifiedFileCount != files.Count
                || result.VerifiedSecurityObjectCount <= 0)
            {
                throw new InvalidOperationException("Protected staging verification evidence is incomplete.");
            }

            if (!File.Exists(Path.Combine(result.StagingRoot, "bin", "ciadpi.exe"))
                || !File.Exists(Path.Combine(result.StagingRoot, "LICENSE")))
            {
                throw new InvalidOperationException("Synthetic staged tree is incomplete.");
            }

            lines.Add("STAGE_FIXED_PROGRAM_FILES_BOUNDARY=PASS");
            lines.Add("MANIFEST_REVERIFY=PASS");
            lines.Add($"ACL_VERIFIED_OBJECTS={result.VerifiedSecurityObjectCount.ToString(CultureInfo.InvariantCulture)}");

            Directory.CreateDirectory(netPilotRoot);
            File.WriteAllText(
                Path.Combine(netPilotRoot, LabMarkerName),
                "R42.2 LocalSystem synthetic staging lab\n",
                Encoding.UTF8);

            var journalRoot = Path.Combine(stagingBase, "Journal");
            var verifiedJournalPath = Path.Combine(journalRoot, result.TransactionId + ".json");
            var verifiedJournal = await File.ReadAllTextAsync(verifiedJournalPath);
            if (!verifiedJournal.Contains("\"phase\":\"Verified\"", StringComparison.Ordinal)
                || verifiedJournal.Contains(result.StagingRoot, StringComparison.OrdinalIgnoreCase)
                || verifiedJournal.Contains(stagingBase, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Verified journal is not path-free or has the wrong phase.");
            }

            lines.Add("JOURNAL_PATH_FREE=PASS");

            var preserveReport = WindowsByeDpiProtectedStagingTransaction.RecoverIncompleteTransactions();
            if (preserveReport.RecoveredIncompleteTransactions != 0
                || preserveReport.PreservedVerifiedTransactions != 1
                || preserveReport.RemovedOrphanTransactions != 0)
            {
                throw new InvalidOperationException("Recovery did not preserve the verified transaction exactly.");
            }

            lines.Add("RECOVERY_VERIFIED_PRESERVED=PASS");

            var incompleteId = Guid.NewGuid().ToString("N");
            var incompleteRoot = Path.Combine(stagingBase, incompleteId);
            Directory.CreateDirectory(incompleteRoot);
            await File.WriteAllTextAsync(
                Path.Combine(incompleteRoot, "partial.bin"),
                "synthetic-partial-r42-2",
                Encoding.UTF8);

            var incompleteJournalPath = Path.Combine(journalRoot, incompleteId + ".json");
            var incompleteJournal = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                transactionId = incompleteId,
                manifestSha256 = manifest.CanonicalManifestSha256,
                version = manifest.Version,
                phase = "Extracting",
                updatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
            });
            await File.WriteAllTextAsync(incompleteJournalPath, incompleteJournal + "\n", Encoding.UTF8);

            var orphanId = Guid.NewGuid().ToString("N");
            var orphanRoot = Path.Combine(stagingBase, orphanId);
            Directory.CreateDirectory(orphanRoot);
            await File.WriteAllTextAsync(
                Path.Combine(orphanRoot, "orphan.bin"),
                "synthetic-orphan-r42-2",
                Encoding.UTF8);

            var recovery = WindowsByeDpiProtectedStagingTransaction.RecoverIncompleteTransactions();
            if (recovery.RecoveredIncompleteTransactions != 1
                || recovery.PreservedVerifiedTransactions != 1
                || recovery.RemovedOrphanTransactions != 1
                || Directory.Exists(incompleteRoot)
                || File.Exists(incompleteJournalPath)
                || Directory.Exists(orphanRoot)
                || !Directory.Exists(result.StagingRoot)
                || !File.Exists(verifiedJournalPath))
            {
                throw new InvalidOperationException("Deterministic LocalSystem recovery did not match the expected exact state.");
            }

            lines.Add("RECOVERY_INCOMPLETE=PASS");
            lines.Add("RECOVERY_ORPHAN=PASS");
            lines.Add("NATIVE_EXECUTION_TESTED=False");
            lines.Add("SERVICE_IPC_WIRING=ABSENT");
            lines.Add("PRODUCTION_ADMISSION=BLOCK");
            lines.Add("RESULT=R42_2_LOCALSYSTEM_STAGING_RECOVERY_PASS_BLOCK");

            await WriteEvidenceAsync(outputPath, lines);
            return 0;
        }
        catch (Exception exception)
        {
            lines.Add("RESULT=R42_2_LOCALSYSTEM_STAGING_RECOVERY_FAIL");
            lines.Add("ERROR_TYPE=" + exception.GetType().Name);
            if (exception is WindowsByeDpiStagingTransactionException stagingException)
            {
                lines.Add("ERROR_CODE=" + stagingException.Code);
            }

            await WriteEvidenceAsync(outputPath, lines);
            return 1;
        }
    }

    private static async Task WriteEvidenceAsync(string outputPath, IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8);
    }

    private static Dictionary<string, byte[]> CreateSyntheticFiles() => new(StringComparer.Ordinal)
    {
        ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes("synthetic-not-executable-r42-2-localsystem"),
        ["LICENSE"] = Encoding.UTF8.GetBytes("Synthetic MIT notice for R42.2 LocalSystem lab only.\n")
    };

    private static ByeDpiPackageManifest CreateManifest(IReadOnlyDictionary<string, byte[]> files) =>
        new(
            "byedpi",
            "0.17.3",
            "x64",
            "bin/ciadpi.exe",
            "LICENSE",
            files.Select(static pair => ToManifestFile(pair.Key, pair.Value)));

    private static ByeDpiPackageFileManifest ToManifestFile(string path, byte[] content) =>
        new(
            path,
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());

    private static MemoryStream CreateZip(IReadOnlyDictionary<string, byte[]> files)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var pair in files)
            {
                var entry = archive.CreateEntry(pair.Key, CompressionLevel.NoCompression);
                using var target = entry.Open();
                target.Write(pair.Value);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
