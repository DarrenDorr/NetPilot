using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using NetPilot.Platform.Windows;
using NetPilot.Updater;

namespace NetPilot.ByeDpiR42PromotionLab;

internal static class Program
{
    private const string OwnershipMarkerName = ".r42-3-lab-owned";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2)
        {
            return 2;
        }

        var outputPath = Path.GetFullPath(args[0]);
        var ownershipToken = args[1];
        var evidence = new List<string>();

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("R42.3 LocalSystem promotion lab is Windows-only.");
            }

            using var identity = WindowsIdentity.GetCurrent();
            if (!identity.IsSystem)
            {
                throw new UnauthorizedAccessException("R42.3 LocalSystem promotion lab must run as LocalSystem.");
            }
            evidence.Add("IDENTITY_LOCALSYSTEM=PASS");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                throw new InvalidOperationException("Program Files path is unavailable.");
            }

            var productRoot = Path.Combine(programFiles, "NetPilot");
            var ownershipMarker = Path.Combine(productRoot, OwnershipMarkerName);
            if (!File.Exists(ownershipMarker)
                || !string.Equals(
                    (await File.ReadAllTextAsync(ownershipMarker)).Trim(),
                    ownershipToken,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.3 lab ownership marker is missing or mismatched.");
            }
            evidence.Add("LAB_OWNERSHIP_MARKER=PASS");

            var manifestV1 = CreateManifest("0.17.2", "previous");
            var stagedV1 = await StageAsync(manifestV1);
            var sourceV1 = await WindowsByeDpiPromotionSourceVerifier.VerifyAsync(
                stagedV1,
                manifestV1);
            var install = await WindowsByeDpiAtomicPackagePromoter.PromoteAsync(
                sourceV1,
                ByeDpiPackageLifecycleOperation.Install,
                engineLeaseActive: false);

            if (!string.Equals(install.CurrentVersion, "0.17.2", StringComparison.Ordinal)
                || install.RetainedPreviousVersion is not null
                || install.VerifiedFileCount != manifestV1.Files.Count
                || install.VerifiedSecurityObjectCount <= 0)
            {
                throw new InvalidOperationException("R42.3 install evidence is incomplete.");
            }
            AssertSyntheticNonPe(install.ManagedPackageRoot);
            evidence.Add("INSTALL_ATOMIC_PROMOTION=PASS");
            evidence.Add("INSTALL_POST_MOVE_REVERIFY=PASS");

            var manifestV2 = CreateManifest("0.17.3", "current");
            var stagedV2 = await StageAsync(manifestV2);
            var sourceV2 = await WindowsByeDpiPromotionSourceVerifier.VerifyAsync(
                stagedV2,
                manifestV2);
            var upgrade = await WindowsByeDpiAtomicPackagePromoter.PromoteAsync(
                sourceV2,
                ByeDpiPackageLifecycleOperation.Upgrade,
                engineLeaseActive: false);

            if (!string.Equals(upgrade.CurrentVersion, "0.17.3", StringComparison.Ordinal)
                || !string.Equals(upgrade.RetainedPreviousVersion, "0.17.2", StringComparison.Ordinal)
                || !Directory.Exists(install.ManagedPackageRoot)
                || !Directory.Exists(upgrade.ManagedPackageRoot)
                || upgrade.VerifiedFileCount != manifestV2.Files.Count
                || upgrade.VerifiedSecurityObjectCount <= 0)
            {
                throw new InvalidOperationException("R42.3 upgrade/retained previous evidence is incomplete.");
            }
            AssertSyntheticNonPe(upgrade.ManagedPackageRoot);
            evidence.Add("UPGRADE_ATOMIC_PROMOTION=PASS");
            evidence.Add("RETAINED_PREVIOUS_IMMUTABLE=PASS");

            var beforeRollback = WindowsByeDpiAtomicPackagePromoter.GetState();
            if (!string.Equals(beforeRollback.CurrentVersion, "0.17.3", StringComparison.Ordinal)
                || !string.Equals(beforeRollback.RetainedPreviousVersion, "0.17.2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.3 protected state before rollback is incorrect.");
            }
            evidence.Add("PROTECTED_STATE_BEFORE_ROLLBACK=PASS");

            var rollback = await WindowsByeDpiAtomicPackagePromoter.RollbackAsync(
                engineLeaseActive: false);
            if (!string.Equals(rollback.CurrentVersion, "0.17.2", StringComparison.Ordinal)
                || !string.Equals(rollback.RetainedPreviousVersion, "0.17.3", StringComparison.Ordinal)
                || !Directory.Exists(install.ManagedPackageRoot)
                || !Directory.Exists(upgrade.ManagedPackageRoot))
            {
                throw new InvalidOperationException("R42.3 target-free rollback evidence is incomplete.");
            }
            evidence.Add("TARGET_FREE_ROLLBACK=PASS");

            var afterRollback = WindowsByeDpiAtomicPackagePromoter.GetState();
            if (!string.Equals(afterRollback.CurrentVersion, "0.17.2", StringComparison.Ordinal)
                || !string.Equals(afterRollback.RetainedPreviousVersion, "0.17.3", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.3 protected state after rollback is incorrect.");
            }
            evidence.Add("PROTECTED_STATE_AFTER_ROLLBACK=PASS");

            var recovery = await WindowsByeDpiAtomicPackagePromoter.RecoverIncompleteOperationsAsync();
            if (recovery.AbortedPreparedPromotions != 0
                || recovery.CompletedMovedPromotions != 0
                || recovery.CompletedRollbacks != 0)
            {
                throw new InvalidOperationException("R42.3 clean recovery unexpectedly changed package state.");
            }
            evidence.Add("CLEAN_RECOVERY_IDEMPOTENT=PASS");

            var managedBase = Path.Combine(
                programFiles,
                "NetPilot",
                "Engines",
                "Managed",
                ByeDpiPackageManifest.RequiredEngineId);
            var expectedV1 = Path.Combine(managedBase, "0.17.2", "x64");
            var expectedV2 = Path.Combine(managedBase, "0.17.3", "x64");
            if (!string.Equals(
                    Path.GetFullPath(install.ManagedPackageRoot),
                    Path.GetFullPath(expectedV1),
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    Path.GetFullPath(upgrade.ManagedPackageRoot),
                    Path.GetFullPath(expectedV2),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("R42.3 promotion escaped canonical managed layout.");
            }
            evidence.Add("CANONICAL_MANAGED_LAYOUT=PASS");

            evidence.Add("SYNTHETIC_PAYLOAD_NOT_PE=PASS");
            evidence.Add("SERVICE_IPC_WIRING=ABSENT");
            evidence.Add("NATIVE_EXECUTION_TESTED=False");
            evidence.Add("PRODUCTION_ADMISSION=BLOCK");
            evidence.Add("RESULT=R42_3_LOCALSYSTEM_PROMOTION_ROLLBACK_PASS_BLOCK");
            await WriteEvidenceAsync(outputPath, evidence);
            return 0;
        }
        catch (Exception exception)
        {
            evidence.Add("RESULT=R42_3_LOCALSYSTEM_PROMOTION_ROLLBACK_FAIL");
            evidence.Add("ERROR_TYPE=" + exception.GetType().Name);
            if (exception is WindowsByeDpiPromotionException promotionException)
            {
                evidence.Add("ERROR_CODE=" + promotionException.Code);
            }
            else if (exception is WindowsByeDpiStagingTransactionException stagingException)
            {
                evidence.Add("ERROR_CODE=" + stagingException.Code);
            }

            await WriteEvidenceAsync(outputPath, evidence);
            return 1;
        }
    }

    private static async Task<WindowsByeDpiStagingTransactionResult> StageAsync(
        ByeDpiPackageManifest manifest)
    {
        var files = CreateFiles(manifest.Version, manifest.CanonicalManifestSha256[..8]);
        manifest = CreateManifestFromFiles(manifest.Version, files);
        await using var zip = CreateZip(files);
        return await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync(
            zip,
            manifest);
    }

    private static void AssertSyntheticNonPe(string managedRoot)
    {
        var executablePath = Path.Combine(managedRoot, "bin", "ciadpi.exe");
        var bytes = File.ReadAllBytes(executablePath);
        if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
        {
            throw new InvalidOperationException("R42.3 synthetic payload unexpectedly looks like a PE executable.");
        }
    }

    private static async Task WriteEvidenceAsync(
        string outputPath,
        IEnumerable<string> lines)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllLinesAsync(outputPath, lines, Encoding.UTF8);
    }

    private static ByeDpiPackageManifest CreateManifest(string version, string label)
    {
        var files = CreateFiles(version, label);
        return CreateManifestFromFiles(version, files);
    }

    private static ByeDpiPackageManifest CreateManifestFromFiles(
        string version,
        IReadOnlyDictionary<string, byte[]> files) =>
        new(
            ByeDpiPackageManifest.RequiredEngineId,
            version,
            ByeDpiPackageManifest.RequiredArchitecture,
            ByeDpiPackageManifest.RequiredExecutableRelativePath,
            ByeDpiPackageManifest.RequiredLicenseRelativePath,
            files.Select(static pair => new ByeDpiPackageFileManifest(
                pair.Key,
                pair.Value.Length,
                Convert.ToHexString(SHA256.HashData(pair.Value)).ToLowerInvariant())));

    private static Dictionary<string, byte[]> CreateFiles(string version, string label) =>
        new(StringComparer.Ordinal)
        {
            ["bin/ciadpi.exe"] = Encoding.ASCII.GetBytes(
                "synthetic-r42-3-localsystem-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes(
                "Synthetic MIT notice for R42.3 LocalSystem lab only.\n")
        };

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
