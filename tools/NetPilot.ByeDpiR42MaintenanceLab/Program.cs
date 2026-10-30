using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using NetPilot.Platform.Windows;
using NetPilot.Updater;

namespace NetPilot.ByeDpiR42MaintenanceLab;

internal static class Program
{
    private const string OwnershipMarkerName = ".r42-4-lab-owned";

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
                throw new PlatformNotSupportedException("R42.4 maintenance lab is Windows-only.");
            }

            using var identity = WindowsIdentity.GetCurrent();
            if (!identity.IsSystem)
            {
                throw new UnauthorizedAccessException("R42.4 maintenance lab must run as LocalSystem.");
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
                throw new InvalidOperationException("R42.4 lab ownership marker is missing or mismatched.");
            }
            evidence.Add("LAB_OWNERSHIP_MARKER=PASS");

            var v1Files = CreateFiles("0.17.2", "previous");
            var v1Manifest = CreateManifest("0.17.2", v1Files);
            var v1Source = await StageAndTrustAsync(v1Manifest, v1Files);
            var install = await WindowsByeDpiAtomicPackagePromoter.PromoteAsync(
                v1Source,
                ByeDpiPackageLifecycleOperation.Install,
                engineLeaseActive: false);
            AssertSyntheticNonPe(install.ManagedPackageRoot);
            evidence.Add("INSTALL_PRECONDITION=PASS");

            var v2Files = CreateFiles("0.17.3", "current");
            var v2Manifest = CreateManifest("0.17.3", v2Files);
            var v2Source = await StageAndTrustAsync(v2Manifest, v2Files);
            var upgrade = await WindowsByeDpiAtomicPackagePromoter.PromoteAsync(
                v2Source,
                ByeDpiPackageLifecycleOperation.Upgrade,
                engineLeaseActive: false);
            if (!string.Equals(upgrade.CurrentVersion, "0.17.3", StringComparison.Ordinal)
                || !string.Equals(upgrade.RetainedPreviousVersion, "0.17.2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.4 upgrade precondition state is invalid.");
            }
            AssertSyntheticNonPe(upgrade.ManagedPackageRoot);
            evidence.Add("UPGRADE_PRECONDITION=PASS");

            var currentExecutable = Path.Combine(upgrade.ManagedPackageRoot, "bin", "ciadpi.exe");
            await File.WriteAllBytesAsync(
                currentExecutable,
                Encoding.ASCII.GetBytes("synthetic-r42-4-intentional-corruption-not-pe"));
            AssertSyntheticNonPe(upgrade.ManagedPackageRoot);
            evidence.Add("INTENTIONAL_CURRENT_CORRUPTION=PASS");

            var repairSource = await StageAndTrustAsync(v2Manifest, v2Files);
            var repair = await WindowsByeDpiPackageMaintenance.RepairAsync(
                repairSource,
                engineLeaseActive: false);
            if (!string.Equals(repair.Version, "0.17.3", StringComparison.Ordinal)
                || repair.VerifiedFileCount != v2Manifest.Files.Count
                || repair.VerifiedSecurityObjectCount <= 0)
            {
                throw new InvalidOperationException("R42.4 repair evidence is incomplete.");
            }

            var restoredBytes = await File.ReadAllBytesAsync(currentExecutable);
            if (!restoredBytes.SequenceEqual(v2Files["bin/ciadpi.exe"]))
            {
                throw new InvalidOperationException("R42.4 repair did not restore exact canonical bytes.");
            }
            AssertSyntheticNonPe(repair.ManagedPackageRoot);
            evidence.Add("EXACT_CURRENT_REPAIR=PASS");
            evidence.Add("REPAIR_POST_SWAP_REVERIFY=PASS");

            var stateAfterRepair = WindowsByeDpiAtomicPackagePromoter.GetState();
            if (!string.Equals(stateAfterRepair.CurrentVersion, "0.17.3", StringComparison.Ordinal)
                || !string.Equals(stateAfterRepair.RetainedPreviousVersion, "0.17.2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.4 repair unexpectedly changed managed version state.");
            }
            evidence.Add("REPAIR_STATE_UNCHANGED=PASS");

            var uninstall = await WindowsByeDpiPackageMaintenance.UninstallAsync(
                engineLeaseActive: false);
            if (!string.Equals(uninstall.RemovedCurrentVersion, "0.17.3", StringComparison.Ordinal)
                || !string.Equals(uninstall.RemovedRetainedPreviousVersion, "0.17.2", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R42.4 uninstall result did not describe exact managed state.");
            }

            var managedByeDpiRoot = Path.Combine(
                programFiles,
                "NetPilot",
                "Engines",
                "Managed",
                ByeDpiPackageManifest.RequiredEngineId);
            if (Directory.Exists(managedByeDpiRoot))
            {
                throw new InvalidOperationException("R42.4 uninstall left managed ByeDPI root behind.");
            }
            evidence.Add("TARGET_FREE_UNINSTALL=PASS");
            evidence.Add("WHOLE_MANAGED_ROOT_REMOVED=PASS");

            var recovery = await WindowsByeDpiPackageMaintenance.RecoverIncompleteMaintenanceAsync();
            if (recovery.AbortedPreparedRepairs != 0
                || recovery.RestoredRepairs != 0
                || recovery.CompletedRepairs != 0
                || recovery.AbortedPreparedUninstalls != 0
                || recovery.CompletedUninstalls != 0)
            {
                throw new InvalidOperationException("R42.4 clean recovery unexpectedly changed maintenance state.");
            }
            evidence.Add("CLEAN_MAINTENANCE_RECOVERY=PASS");

            var maintenanceRoot = Path.Combine(
                programFiles,
                "NetPilot",
                "Engines",
                "Maintenance",
                "ByeDPI");
            var journalRoot = Path.Combine(maintenanceRoot, "Journal");
            var repairBackupRoot = Path.Combine(maintenanceRoot, "RepairBackups");
            var tombstoneRoot = Path.Combine(maintenanceRoot, "UninstallTombstones");
            if (Directory.Exists(journalRoot)
                && Directory.EnumerateFiles(journalRoot, "*.json", SearchOption.TopDirectoryOnly).Any())
            {
                throw new InvalidOperationException("R42.4 maintenance journal residue remains.");
            }
            if (Directory.Exists(repairBackupRoot)
                && Directory.EnumerateDirectories(repairBackupRoot).Any())
            {
                throw new InvalidOperationException("R42.4 repair backup residue remains.");
            }
            if (Directory.Exists(tombstoneRoot)
                && Directory.EnumerateDirectories(tombstoneRoot).Any())
            {
                throw new InvalidOperationException("R42.4 uninstall tombstone residue remains.");
            }
            evidence.Add("MAINTENANCE_RESIDUE=ABSENT");

            evidence.Add("SYNTHETIC_PAYLOAD_NOT_PE=PASS");
            evidence.Add("SERVICE_IPC_WIRING=ABSENT");
            evidence.Add("NATIVE_EXECUTION_TESTED=False");
            evidence.Add("PRODUCTION_ADMISSION=BLOCK");
            evidence.Add("RESULT=R42_4_LOCALSYSTEM_REPAIR_UNINSTALL_PASS_BLOCK");
            await WriteEvidenceAsync(outputPath, evidence);
            return 0;
        }
        catch (Exception exception)
        {
            evidence.Add("RESULT=R42_4_LOCALSYSTEM_REPAIR_UNINSTALL_FAIL");
            evidence.Add("ERROR_TYPE=" + exception.GetType().Name);
            switch (exception)
            {
                case WindowsByeDpiMaintenanceException maintenanceException:
                    evidence.Add("ERROR_CODE=" + maintenanceException.Code);
                    break;
                case WindowsByeDpiPromotionException promotionException:
                    evidence.Add("ERROR_CODE=" + promotionException.Code);
                    break;
                case WindowsByeDpiStagingTransactionException stagingException:
                    evidence.Add("ERROR_CODE=" + stagingException.Code);
                    break;
            }
            await WriteEvidenceAsync(outputPath, evidence);
            return 1;
        }
    }

    private static async Task<WindowsByeDpiVerifiedPromotionSource> StageAndTrustAsync(
        ByeDpiPackageManifest manifest,
        IReadOnlyDictionary<string, byte[]> files)
    {
        await using var zip = CreateZip(files);
        var staged = await WindowsByeDpiProtectedStagingTransaction.StageAndVerifyAsync(
            zip,
            manifest);
        return await WindowsByeDpiPromotionSourceVerifier.VerifyAsync(staged, manifest);
    }

    private static ByeDpiPackageManifest CreateManifest(
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
                "synthetic-r42-4-localsystem-not-executable-" + version + "-" + label),
            ["LICENSE"] = Encoding.UTF8.GetBytes(
                "Synthetic MIT notice for R42.4 LocalSystem lab only.\n")
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

    private static void AssertSyntheticNonPe(string managedRoot)
    {
        var executablePath = Path.Combine(managedRoot, "bin", "ciadpi.exe");
        var bytes = File.ReadAllBytes(executablePath);
        if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
        {
            throw new InvalidOperationException("R42.4 synthetic payload unexpectedly looks like a PE executable.");
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
}