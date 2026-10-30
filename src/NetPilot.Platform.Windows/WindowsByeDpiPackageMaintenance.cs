using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

public enum WindowsByeDpiMaintenancePhase
{
    Prepared = 1,
    CurrentMoved = 2,
    ReplacementMoved = 3,
    ManagedMoved = 4
}

public sealed record WindowsByeDpiRepairResult(
    string Version,
    string ManagedPackageRoot,
    int VerifiedFileCount,
    int VerifiedSecurityObjectCount);

public sealed record WindowsByeDpiUninstallResult(
    string RemovedCurrentVersion,
    string? RemovedRetainedPreviousVersion);

public sealed record WindowsByeDpiMaintenanceRecoveryReport(
    int AbortedPreparedRepairs,
    int RestoredRepairs,
    int CompletedRepairs,
    int AbortedPreparedUninstalls,
    int CompletedUninstalls,
    int RemovedTemporaryFiles);

public sealed class WindowsByeDpiMaintenanceException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

internal enum WindowsByeDpiMaintenanceTestFaultPoint
{
    None = 0,
    AfterRepairCurrentMove = 1,
    AfterRepairReplacementMove = 2,
    AfterUninstallManagedMove = 3
}

/// <summary>
/// R42.4 fail-closed maintenance boundary for an already-managed ByeDPI package.
/// Repair accepts only an R42.2/R42.3 verified source whose exact manifest digest is
/// identical to the canonical manifest of the current version. Uninstall accepts no
/// path or target. Both operations are LocalSystem-only in production, journaled outside
/// the managed ByeDPI root, same-volume, and intentionally do not execute native code.
/// </summary>
public static class WindowsByeDpiPackageMaintenance
{
    private const int JournalSchemaVersion = 1;
    private const int ManifestSchemaVersion = 1;
    private const int MaxMetadataBytes = 64 * 1024;
    private const string JournalDirectoryName = "Journal";
    private const string RepairBackupDirectoryName = "RepairBackups";
    private const string UninstallTombstoneDirectoryName = "UninstallTombstones";

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Task<WindowsByeDpiRepairResult> RepairAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        if (!source.ProductionProtected)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairSourceNotProductionProtected",
                "Production repair требует protected verified staging source.");
        }

        return RepairCoreAsync(
            source,
            engineLeaseActive,
            ResolveProductionManagedBase(),
            WindowsByeDpiPromotionSourceVerifier.ResolveProductionStagingBase(),
            ResolveProductionMaintenanceBase(),
            enforceWindowsSecurity: true,
            WindowsByeDpiMaintenanceTestFaultPoint.None,
            cancellationToken);
    }

    public static Task<WindowsByeDpiUninstallResult> UninstallAsync(
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        return UninstallCoreAsync(
            engineLeaseActive,
            ResolveProductionManagedBase(),
            ResolveProductionMaintenanceBase(),
            enforceWindowsSecurity: true,
            WindowsByeDpiMaintenanceTestFaultPoint.None,
            cancellationToken);
    }

    public static Task<WindowsByeDpiMaintenanceRecoveryReport> RecoverIncompleteMaintenanceAsync(
        CancellationToken cancellationToken = default)
    {
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        return RecoverCoreAsync(
            ResolveProductionManagedBase(),
            WindowsByeDpiPromotionSourceVerifier.ResolveProductionStagingBase(),
            ResolveProductionMaintenanceBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    internal static Task<WindowsByeDpiRepairResult> RepairForTestingAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        bool engineLeaseActive,
        string managedBase,
        string stagingBase,
        string maintenanceBase,
        WindowsByeDpiMaintenanceTestFaultPoint faultPoint = WindowsByeDpiMaintenanceTestFaultPoint.None,
        CancellationToken cancellationToken = default) =>
        RepairCoreAsync(
            source,
            engineLeaseActive,
            managedBase,
            stagingBase,
            maintenanceBase,
            enforceWindowsSecurity: false,
            faultPoint,
            cancellationToken);

    internal static Task<WindowsByeDpiUninstallResult> UninstallForTestingAsync(
        bool engineLeaseActive,
        string managedBase,
        string maintenanceBase,
        WindowsByeDpiMaintenanceTestFaultPoint faultPoint = WindowsByeDpiMaintenanceTestFaultPoint.None,
        CancellationToken cancellationToken = default) =>
        UninstallCoreAsync(
            engineLeaseActive,
            managedBase,
            maintenanceBase,
            enforceWindowsSecurity: false,
            faultPoint,
            cancellationToken);

    internal static Task<WindowsByeDpiMaintenanceRecoveryReport> RecoverForTestingAsync(
        string managedBase,
        string stagingBase,
        string maintenanceBase,
        CancellationToken cancellationToken = default) =>
        RecoverCoreAsync(
            managedBase,
            stagingBase,
            maintenanceBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    private static async Task<WindowsByeDpiRepairResult> RepairCoreAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        bool engineLeaseActive,
        string managedBase,
        string stagingBase,
        string maintenanceBase,
        bool enforceWindowsSecurity,
        WindowsByeDpiMaintenanceTestFaultPoint faultPoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var managed = NormalizeDirectory(managedBase);
        var staging = NormalizeDirectory(stagingBase);
        var maintenance = NormalizeDirectory(maintenanceBase);
        PrepareMaintenanceBase(maintenance, enforceWindowsSecurity);
        EnsureNoPendingMaintenanceTransactions(maintenance);
        EnsurePromotionStateClean(managed);
        ValidateRepairSource(source, staging, enforceWindowsSecurity);

        var state = ReadManagedState(managed, enforceWindowsSecurity);
        var sameVolume = SameVolume(managed, source.StagingRoot)
            && SameVolume(managed, maintenance);
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            new ByeDpiPackageLifecycleRequest(
                ByeDpiPackageLifecycleOperation.Repair,
                source.Version,
                state.CurrentVersion,
                state.RetainedPreviousVersion,
                PackageVerified: true,
                ProtectedStaging: true,
                SameVolumeStaging: sameVolume,
                EngineLeaseActive: engineLeaseActive));
        if (!decision.Allowed || decision.CanonicalTargetVersion is null)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairLifecycleBlocked_" + decision.Code,
                "ByeDPI lifecycle policy заблокировала repair.");
        }

        var version = decision.CanonicalTargetVersion;
        var canonicalManifest = ReadCanonicalManifest(managed, version, enforceWindowsSecurity);
        if (!string.Equals(
                canonicalManifest.CanonicalManifestSha256,
                source.ManifestSha256,
                StringComparison.Ordinal))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairManifestMismatch",
                "Repair source не совпадает с canonical manifest уже установленной версии.");
        }

        await VerifyStagingSourceAgainAsync(
            source,
            canonicalManifest,
            enforceWindowsSecurity,
            cancellationToken);

        var currentRoot = ResolveManagedPackageRoot(managed, version);
        if (!Directory.Exists(currentRoot))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairCurrentPackageMissing",
                "Current managed package отсутствует и не может быть repair target.");
        }
        VerifyTreeSecurityOnly(currentRoot, enforceWindowsSecurity);

        var transactionId = CreateTransactionId();
        var backupTransactionRoot = ResolveRepairBackupTransactionRoot(maintenance, transactionId);
        EnsureProtectedDirectory(backupTransactionRoot, enforceWindowsSecurity);
        var backupRoot = ResolveRepairBackupRoot(maintenance, transactionId);
        var journal = new MaintenanceJournalRecord(
            JournalSchemaVersion,
            transactionId,
            ByeDpiPackageLifecycleOperation.Repair,
            WindowsByeDpiMaintenancePhase.Prepared,
            version,
            canonicalManifest.CanonicalManifestSha256,
            source.TransactionId,
            state.CurrentVersion,
            state.RetainedPreviousVersion,
            DateTimeOffset.UtcNow);
        WriteJournal(maintenance, journal, enforceWindowsSecurity);

        Directory.Move(currentRoot, backupRoot);
        WriteJournal(
            maintenance,
            journal with
            {
                Phase = WindowsByeDpiMaintenancePhase.CurrentMoved,
                UpdatedUtc = DateTimeOffset.UtcNow
            },
            enforceWindowsSecurity);
        InjectTestFault(faultPoint, WindowsByeDpiMaintenanceTestFaultPoint.AfterRepairCurrentMove);

        Directory.Move(source.StagingRoot, currentRoot);
        WriteJournal(
            maintenance,
            journal with
            {
                Phase = WindowsByeDpiMaintenancePhase.ReplacementMoved,
                UpdatedUtc = DateTimeOffset.UtcNow
            },
            enforceWindowsSecurity);
        InjectTestFault(faultPoint, WindowsByeDpiMaintenanceTestFaultPoint.AfterRepairReplacementMove);

        var evidence = await VerifyExactManagedPackageAsync(
            managed,
            version,
            canonicalManifest,
            enforceWindowsSecurity,
            cancellationToken);

        CleanupStagingJournal(staging, source.TransactionId, enforceWindowsSecurity);
        DeleteTreeStrict(backupRoot, enforceWindowsSecurity);
        DeleteEmptyDirectoryStrict(backupTransactionRoot);
        DeleteJournal(maintenance, transactionId);

        return new WindowsByeDpiRepairResult(
            version,
            currentRoot,
            evidence.VerifiedFileCount,
            evidence.VerifiedSecurityObjectCount);
    }

    private static async Task<WindowsByeDpiUninstallResult> UninstallCoreAsync(
        bool engineLeaseActive,
        string managedBase,
        string maintenanceBase,
        bool enforceWindowsSecurity,
        WindowsByeDpiMaintenanceTestFaultPoint faultPoint,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var managed = NormalizeDirectory(managedBase);
        var maintenance = NormalizeDirectory(maintenanceBase);
        PrepareMaintenanceBase(maintenance, enforceWindowsSecurity);
        EnsureNoPendingMaintenanceTransactions(maintenance);

        if (!Directory.Exists(managed))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiUninstallManagedRootMissing",
                "ByeDPI managed root отсутствует.");
        }

        EnsurePromotionStateClean(managed);
        var state = ReadManagedState(managed, enforceWindowsSecurity);
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            new ByeDpiPackageLifecycleRequest(
                ByeDpiPackageLifecycleOperation.Uninstall,
                TargetVersion: null,
                state.CurrentVersion,
                state.RetainedPreviousVersion,
                PackageVerified: false,
                ProtectedStaging: false,
                SameVolumeStaging: SameVolume(managed, maintenance),
                EngineLeaseActive: engineLeaseActive));
        if (!decision.Allowed || state.CurrentVersion is null)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiUninstallLifecycleBlocked_" + decision.Code,
                "ByeDPI lifecycle policy заблокировала uninstall.");
        }

        VerifyTreeSecurityOnly(managed, enforceWindowsSecurity);
        if (!SameVolume(managed, maintenance))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiUninstallCrossVolume",
                "Crash-safe uninstall требует managed root и maintenance tombstone на одном volume.");
        }

        var transactionId = CreateTransactionId();
        var tombstoneTransactionRoot = ResolveUninstallTombstoneTransactionRoot(
            maintenance,
            transactionId);
        EnsureProtectedDirectory(tombstoneTransactionRoot, enforceWindowsSecurity);
        var tombstoneRoot = ResolveUninstallTombstoneRoot(maintenance, transactionId);
        var journal = new MaintenanceJournalRecord(
            JournalSchemaVersion,
            transactionId,
            ByeDpiPackageLifecycleOperation.Uninstall,
            WindowsByeDpiMaintenancePhase.Prepared,
            state.CurrentVersion,
            ManifestSha256: null,
            SourceTransactionId: null,
            state.CurrentVersion,
            state.RetainedPreviousVersion,
            DateTimeOffset.UtcNow);
        WriteJournal(maintenance, journal, enforceWindowsSecurity);

        Directory.Move(managed, tombstoneRoot);
        WriteJournal(
            maintenance,
            journal with
            {
                Phase = WindowsByeDpiMaintenancePhase.ManagedMoved,
                UpdatedUtc = DateTimeOffset.UtcNow
            },
            enforceWindowsSecurity);
        InjectTestFault(faultPoint, WindowsByeDpiMaintenanceTestFaultPoint.AfterUninstallManagedMove);

        VerifyTreeSecurityOnly(tombstoneRoot, enforceWindowsSecurity);
        DeleteTreeStrict(tombstoneRoot, enforceWindowsSecurity);
        DeleteEmptyDirectoryStrict(tombstoneTransactionRoot);
        DeleteJournal(maintenance, transactionId);

        return new WindowsByeDpiUninstallResult(
            state.CurrentVersion,
            state.RetainedPreviousVersion);
    }

    private static async Task<WindowsByeDpiMaintenanceRecoveryReport> RecoverCoreAsync(
        string managedBase,
        string stagingBase,
        string maintenanceBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        var managed = NormalizeDirectory(managedBase);
        var staging = NormalizeDirectory(stagingBase);
        var maintenance = NormalizeDirectory(maintenanceBase);
        if (!Directory.Exists(maintenance))
        {
            return new WindowsByeDpiMaintenanceRecoveryReport(0, 0, 0, 0, 0, 0);
        }

        PrepareMaintenanceBase(maintenance, enforceWindowsSecurity);
        var removedTemps = RemoveJournalTemps(maintenance);
        var abortedRepairs = 0;
        var restoredRepairs = 0;
        var completedRepairs = 0;
        var abortedUninstalls = 0;
        var completedUninstalls = 0;

        foreach (var journalPath in Directory.EnumerateFiles(
                     ResolveJournalRoot(maintenance),
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadJournal(journalPath, enforceWindowsSecurity);
            if (record.Operation == ByeDpiPackageLifecycleOperation.Repair)
            {
                var outcome = await RecoverRepairAsync(
                    record,
                    managed,
                    staging,
                    maintenance,
                    enforceWindowsSecurity,
                    cancellationToken);
                abortedRepairs += outcome.Aborted;
                restoredRepairs += outcome.Restored;
                completedRepairs += outcome.Completed;
                continue;
            }

            if (record.Operation == ByeDpiPackageLifecycleOperation.Uninstall)
            {
                var outcome = RecoverUninstall(
                    record,
                    managed,
                    maintenance,
                    enforceWindowsSecurity);
                abortedUninstalls += outcome.Aborted;
                completedUninstalls += outcome.Completed;
                continue;
            }

            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceJournalOperationInvalid",
                "Maintenance recovery journal содержит неподдерживаемую operation.");
        }

        return new WindowsByeDpiMaintenanceRecoveryReport(
            abortedRepairs,
            restoredRepairs,
            completedRepairs,
            abortedUninstalls,
            completedUninstalls,
            removedTemps);
    }

    private static async Task<(int Aborted, int Restored, int Completed)> RecoverRepairAsync(
        MaintenanceJournalRecord record,
        string managedBase,
        string stagingBase,
        string maintenanceBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        if (record.SourceTransactionId is null || record.ManifestSha256 is null)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairJournalShapeInvalid",
                "Repair recovery journal не содержит source transaction/digest.");
        }

        var currentRoot = ResolveManagedPackageRoot(managedBase, record.Version);
        var backupTransactionRoot = ResolveRepairBackupTransactionRoot(
            maintenanceBase,
            record.TransactionId);
        var backupRoot = ResolveRepairBackupRoot(maintenanceBase, record.TransactionId);
        var stagingRoot = ResolveStagingTransactionRoot(stagingBase, record.SourceTransactionId);
        var currentExists = Directory.Exists(currentRoot);
        var backupExists = Directory.Exists(backupRoot);
        var stagingExists = Directory.Exists(stagingRoot);

        if (!backupExists && currentExists && stagingExists)
        {
            DeleteJournal(maintenanceBase, record.TransactionId);
            DeleteEmptyDirectoryStrict(backupTransactionRoot);
            return (1, 0, 0);
        }

        if (backupExists && !currentExists && stagingExists)
        {
            VerifyTreeSecurityOnly(backupRoot, enforceWindowsSecurity);
            Directory.Move(backupRoot, currentRoot);
            DeleteEmptyDirectoryStrict(backupTransactionRoot);
            DeleteJournal(maintenanceBase, record.TransactionId);
            return (0, 1, 0);
        }

        if (currentExists && !stagingExists)
        {
            var canonicalManifest = ReadCanonicalManifest(
                managedBase,
                record.Version,
                enforceWindowsSecurity);
            if (!string.Equals(
                    canonicalManifest.CanonicalManifestSha256,
                    record.ManifestSha256,
                    StringComparison.Ordinal))
            {
                throw new WindowsByeDpiMaintenanceException(
                    "ByeDpiRepairRecoveryManifestMismatch",
                    "Repair recovery journal не совпадает с canonical manifest.");
            }

            var replacementValid = await TryVerifyExactManagedPackageAsync(
                managedBase,
                record.Version,
                canonicalManifest,
                enforceWindowsSecurity,
                cancellationToken);
            if (replacementValid)
            {
                if (backupExists)
                {
                    DeleteTreeStrict(backupRoot, enforceWindowsSecurity);
                }
                CleanupStagingJournal(stagingBase, record.SourceTransactionId, enforceWindowsSecurity);
                DeleteEmptyDirectoryStrict(backupTransactionRoot);
                DeleteJournal(maintenanceBase, record.TransactionId);
                return (0, 0, 1);
            }

            if (!backupExists)
            {
                throw new WindowsByeDpiMaintenanceException(
                    "ByeDpiRepairRecoveryNoTrustedBackup",
                    "Repair replacement не прошёл verification и trusted backup отсутствует.");
            }

            DeleteTreeStrict(currentRoot, enforceWindowsSecurity);
            VerifyTreeSecurityOnly(backupRoot, enforceWindowsSecurity);
            Directory.Move(backupRoot, currentRoot);
            CleanupStagingJournal(stagingBase, record.SourceTransactionId, enforceWindowsSecurity);
            DeleteEmptyDirectoryStrict(backupTransactionRoot);
            DeleteJournal(maintenanceBase, record.TransactionId);
            return (0, 1, 0);
        }

        throw new WindowsByeDpiMaintenanceException(
            "ByeDpiRepairRecoveryStateAmbiguous",
            "Repair recovery обнаружил неоднозначное сочетание current/backup/staging roots.");
    }

    private static (int Aborted, int Completed) RecoverUninstall(
        MaintenanceJournalRecord record,
        string managedBase,
        string maintenanceBase,
        bool enforceWindowsSecurity)
    {
        var tombstoneTransactionRoot = ResolveUninstallTombstoneTransactionRoot(
            maintenanceBase,
            record.TransactionId);
        var tombstoneRoot = ResolveUninstallTombstoneRoot(
            maintenanceBase,
            record.TransactionId);
        var managedExists = Directory.Exists(managedBase);
        var tombstoneExists = Directory.Exists(tombstoneRoot);

        if (managedExists && !tombstoneExists)
        {
            DeleteJournal(maintenanceBase, record.TransactionId);
            DeleteEmptyDirectoryStrict(tombstoneTransactionRoot);
            return (1, 0);
        }

        if (!managedExists && tombstoneExists)
        {
            VerifyTreeSecurityOnly(tombstoneRoot, enforceWindowsSecurity);
            DeleteTreeStrict(tombstoneRoot, enforceWindowsSecurity);
            DeleteEmptyDirectoryStrict(tombstoneTransactionRoot);
            DeleteJournal(maintenanceBase, record.TransactionId);
            return (0, 1);
        }

        if (!managedExists && !tombstoneExists)
        {
            DeleteEmptyDirectoryStrict(tombstoneTransactionRoot);
            DeleteJournal(maintenanceBase, record.TransactionId);
            return (0, 1);
        }

        throw new WindowsByeDpiMaintenanceException(
            "ByeDpiUninstallRecoveryStateAmbiguous",
            "Uninstall recovery обнаружил одновременно managed root и tombstone.");
    }

    private static WindowsByeDpiManagedPackageState ReadManagedState(
        string managedBase,
        bool enforceWindowsSecurity) =>
        enforceWindowsSecurity
            ? WindowsByeDpiAtomicPackagePromoter.GetState()
            : WindowsByeDpiAtomicPackagePromoter.GetStateForTesting(managedBase);

    private static void ValidateRepairSource(
        WindowsByeDpiVerifiedPromotionSource source,
        string stagingBase,
        bool enforceWindowsSecurity)
    {
        WindowsByeDpiPromotionSourceVerifier.ValidateTransactionId(source.TransactionId);
        var expected = ResolveStagingTransactionRoot(stagingBase, source.TransactionId);
        if (!string.Equals(
                expected,
                NormalizeDirectory(source.StagingRoot),
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(expected))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairSourceNoLongerValid",
                "Repair source больше не соответствует exact staging transaction.");
        }

        if (enforceWindowsSecurity && !source.ProductionProtected)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairSourceNotProtected",
                "Production repair source не имеет protected staging proof.");
        }

        var stagingJournal = ResolveStagingJournalPath(stagingBase, source.TransactionId);
        if (!File.Exists(stagingJournal))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiRepairStagingJournalMissing",
                "Repair source больше не имеет verified R42.2 staging journal.");
        }
        RejectReparsePoint(stagingJournal, "ByeDpiRepairStagingJournalReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(stagingJournal, expectDirectory: false);
        }
    }

    private static async Task VerifyStagingSourceAgainAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        ByeDpiPackageManifest canonicalManifest,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        await ByeDpiPackageManifestVerifier.VerifyAsync(
            source.StagingRoot,
            canonicalManifest,
            cancellationToken);
        if (enforceWindowsSecurity)
        {
            WindowsByeDpiProtectedStagingSecurityVerifier.Verify(
                source.TransactionId,
                canonicalManifest.Files.Select(static file => file.RelativePath).ToArray());
        }
    }

    private static async Task<ManagedVerificationEvidence> VerifyExactManagedPackageAsync(
        string managedBase,
        string version,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        var root = ResolveManagedPackageRoot(managedBase, version);
        var exact = await ByeDpiPackageManifestVerifier.VerifyAsync(root, manifest, cancellationToken);
        var securityCount = VerifyTreeSecurityOnly(root, enforceWindowsSecurity);
        return new ManagedVerificationEvidence(exact.VerifiedFileCount, securityCount);
    }

    private static async Task<bool> TryVerifyExactManagedPackageAsync(
        string managedBase,
        string version,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        try
        {
            await VerifyExactManagedPackageAsync(
                managedBase,
                version,
                manifest,
                enforceWindowsSecurity,
                cancellationToken);
            return true;
        }
        catch (UpdateSecurityException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static int VerifyTreeSecurityOnly(string root, bool enforceWindowsSecurity)
    {
        if (!Directory.Exists(root))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceTreeMissing",
                "Maintenance filesystem tree отсутствует.");
        }

        var count = 0;
        var pending = new Stack<string>();
        pending.Push(NormalizeDirectory(root));
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            RejectReparsePoint(directory, "ByeDpiMaintenanceTreeReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyFilesystemSecurity(directory, expectDirectory: true);
            }
            count++;

            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                RejectReparsePoint(entry, "ByeDpiMaintenanceTreeReparsePoint");
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    if (enforceWindowsSecurity)
                    {
                        VerifyFilesystemSecurity(entry, expectDirectory: false);
                    }
                    count++;
                }
            }
        }
        return count;
    }

    private static void EnsurePromotionStateClean(string managedBase)
    {
        if (!Directory.Exists(managedBase))
        {
            return;
        }

        var stateRoot = Path.Combine(managedBase, ".state");
        var promotionJournalRoot = Path.Combine(stateRoot, "journal");
        var incomingRoot = Path.Combine(stateRoot, "incoming-manifests");
        foreach (var root in new[] { promotionJournalRoot, incomingRoot })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }
            RejectReparsePoint(root, "ByeDpiMaintenancePendingPromotionRootReparsePoint");
            if (Directory.EnumerateFileSystemEntries(root).Any())
            {
                throw new WindowsByeDpiMaintenanceException(
                    "ByeDpiMaintenancePendingPromotion",
                    "Repair/uninstall запрещён при незавершённой R42.3 promotion transaction.");
            }
        }
    }

    private static ByeDpiPackageManifest ReadCanonicalManifest(
        string managedBase,
        string version,
        bool enforceWindowsSecurity)
    {
        ValidateVersion(version);
        var manifestRoot = NormalizeDirectory(
            Path.Combine(managedBase, ".state", "manifests"));
        var path = Path.GetFullPath(Path.Combine(manifestRoot, version + ".json"));
        EnsureStrictChild(manifestRoot, path, "ByeDpiMaintenanceManifestOutsideBoundary");
        if (!File.Exists(path))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceCanonicalManifestMissing",
                "Canonical manifest текущей managed версии отсутствует.");
        }
        RejectReparsePoint(path, "ByeDpiMaintenanceManifestReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }

        var dto = ReadJsonStrict<ManifestDto>(path, "ByeDpiMaintenanceCanonicalManifestInvalid");
        if (dto.SchemaVersion != ManifestSchemaVersion
            || dto.EngineId is null
            || dto.Version is null
            || dto.Architecture is null
            || dto.ExecutableRelativePath is null
            || dto.LicenseRelativePath is null
            || dto.ManifestSha256 is null
            || dto.Files is null)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceCanonicalManifestShapeInvalid",
                "Canonical managed manifest имеет неверную shape.");
        }

        var manifest = new ByeDpiPackageManifest(
            dto.EngineId,
            dto.Version,
            dto.Architecture,
            dto.ExecutableRelativePath,
            dto.LicenseRelativePath,
            dto.Files.Select(static file => new ByeDpiPackageFileManifest(
                file.RelativePath ?? string.Empty,
                file.SizeBytes,
                file.Sha256Hex ?? string.Empty)));
        if (!string.Equals(manifest.Version, version, StringComparison.Ordinal)
            || !string.Equals(
                manifest.CanonicalManifestSha256,
                dto.ManifestSha256,
                StringComparison.Ordinal))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceCanonicalManifestDigestMismatch",
                "Canonical managed manifest не совпадает с persisted digest/version.");
        }
        return manifest;
    }

    private static void PrepareMaintenanceBase(string maintenanceBase, bool enforceWindowsSecurity)
    {
        var normalized = NormalizeDirectory(maintenanceBase);
        if (enforceWindowsSecurity)
        {
            var expected = ResolveProductionMaintenanceBase();
            if (!string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new WindowsByeDpiMaintenanceException(
                    "ByeDpiMaintenanceBaseMismatch",
                    "Production maintenance разрешён только в fixed Program Files boundary.");
            }

            var programFiles = NormalizeDirectory(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            var enginesRoot = NormalizeDirectory(
                Path.Combine(programFiles, "NetPilot", "Engines"));
            Directory.CreateDirectory(enginesRoot);
            EnsureNoReparsePointsBetween(programFiles, enginesRoot);
        }

        EnsureProtectedDirectory(normalized, enforceWindowsSecurity);
        EnsureProtectedDirectory(ResolveJournalRoot(normalized), enforceWindowsSecurity);
        EnsureProtectedDirectory(ResolveRepairBackupRootBase(normalized), enforceWindowsSecurity);
        EnsureProtectedDirectory(ResolveUninstallTombstoneRootBase(normalized), enforceWindowsSecurity);
    }

    private static void EnsureProtectedDirectory(string path, bool enforceWindowsSecurity)
    {
        var fullPath = NormalizeDirectory(path);
        if (!enforceWindowsSecurity)
        {
            Directory.CreateDirectory(fullPath);
            RejectReparsePoint(fullPath, "ByeDpiMaintenanceDirectoryReparsePoint");
            return;
        }

        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceParentMissing",
                "Не удалось определить parent maintenance directory.");
        if (!Directory.Exists(parent))
        {
            Directory.CreateDirectory(parent);
        }
        RejectReparsePoint(parent, "ByeDpiMaintenanceParentReparsePoint");

        if (Directory.Exists(fullPath))
        {
            RejectReparsePoint(fullPath, "ByeDpiMaintenanceDirectoryReparsePoint");
            FileSystemAclExtensions.SetAccessControl(
                new DirectoryInfo(fullPath),
                WindowsUserProfileDirectorySecurity.CreateServiceOnlyAcl());
        }
        else
        {
            FileSystemAclExtensions.Create(
                new DirectoryInfo(fullPath),
                WindowsUserProfileDirectorySecurity.CreateServiceOnlyAcl());
        }
        VerifyFilesystemSecurity(fullPath, expectDirectory: true);
    }

    private static void VerifyFilesystemSecurity(string path, bool expectDirectory)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceSecurityObjectMissing",
                "Maintenance filesystem object отсутствует.");
        }
        RejectReparsePoint(path, "ByeDpiMaintenanceSecurityReparsePoint");
        var attributes = File.GetAttributes(path);
        if (((attributes & FileAttributes.Directory) != 0) != expectDirectory)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceSecurityObjectTypeMismatch",
                "Maintenance filesystem object имеет неожиданный type.");
        }

        FileSystemSecurity security = expectDirectory
            ? FileSystemAclExtensions.GetAccessControl(
                new DirectoryInfo(path),
                AccessControlSections.Access | AccessControlSections.Owner)
            : FileSystemAclExtensions.GetAccessControl(
                new FileInfo(path),
                AccessControlSections.Access | AccessControlSections.Owner);
        WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor(security);
    }

    private static void WriteJournal(
        string maintenanceBase,
        MaintenanceJournalRecord record,
        bool enforceWindowsSecurity)
    {
        ValidateJournal(record);
        var dto = new MaintenanceJournalDto
        {
            SchemaVersion = record.SchemaVersion,
            TransactionId = record.TransactionId,
            Operation = record.Operation.ToString(),
            Phase = record.Phase.ToString(),
            Version = record.Version,
            ManifestSha256 = record.ManifestSha256,
            SourceTransactionId = record.SourceTransactionId,
            PreviousCurrentVersion = record.PreviousCurrentVersion,
            PreviousRetainedVersion = record.PreviousRetainedVersion,
            UpdatedUtc = record.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        WriteJsonAtomic(
            ResolveJournalRoot(maintenanceBase),
            record.TransactionId + ".json",
            dto,
            enforceWindowsSecurity);
    }

    private static MaintenanceJournalRecord ReadJournal(
        string path,
        bool enforceWindowsSecurity)
    {
        RejectReparsePoint(path, "ByeDpiMaintenanceJournalReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".json", StringComparison.Ordinal))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceJournalNameInvalid",
                "Maintenance journal filename invalid.");
        }
        var transactionId = fileName[..^5];
        ValidateTransactionId(transactionId);
        var dto = ReadJsonStrict<MaintenanceJournalDto>(
            path,
            "ByeDpiMaintenanceJournalInvalid");
        if (dto.SchemaVersion != JournalSchemaVersion
            || !string.Equals(dto.TransactionId, transactionId, StringComparison.Ordinal)
            || !Enum.TryParse<ByeDpiPackageLifecycleOperation>(dto.Operation, false, out var operation)
            || !Enum.IsDefined(operation)
            || !Enum.TryParse<WindowsByeDpiMaintenancePhase>(dto.Phase, false, out var phase)
            || !Enum.IsDefined(phase)
            || dto.Version is null
            || dto.UpdatedUtc is null
            || !DateTimeOffset.TryParseExact(
                dto.UpdatedUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedUtc))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceJournalShapeInvalid",
                "Maintenance journal имеет неверную shape.");
        }

        var record = new MaintenanceJournalRecord(
            dto.SchemaVersion,
            transactionId,
            operation,
            phase,
            dto.Version,
            dto.ManifestSha256,
            dto.SourceTransactionId,
            dto.PreviousCurrentVersion,
            dto.PreviousRetainedVersion,
            updatedUtc);
        ValidateJournal(record);
        return record;
    }

    private static void ValidateJournal(MaintenanceJournalRecord record)
    {
        if (record.SchemaVersion != JournalSchemaVersion)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceJournalSchemaInvalid",
                "Maintenance journal schema version invalid.");
        }
        ValidateTransactionId(record.TransactionId);
        ValidateVersion(record.Version);
        if (record.ManifestSha256 is not null)
        {
            ValidateDigest(record.ManifestSha256);
        }
        if (record.SourceTransactionId is not null)
        {
            ValidateTransactionId(record.SourceTransactionId);
        }
        if (record.PreviousCurrentVersion is not null)
        {
            ValidateVersion(record.PreviousCurrentVersion);
        }
        if (record.PreviousRetainedVersion is not null)
        {
            ValidateVersion(record.PreviousRetainedVersion);
        }
        if (record.Operation is not ByeDpiPackageLifecycleOperation.Repair
            and not ByeDpiPackageLifecycleOperation.Uninstall)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceJournalOperationInvalid",
                "Maintenance journal operation invalid.");
        }
    }

    private static T ReadJsonStrict<T>(string path, string code)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaxMetadataBytes)
        {
            throw new WindowsByeDpiMaintenanceException(code, "Protected metadata size invalid.");
        }
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan);
            return JsonSerializer.Deserialize<T>(stream, StrictJsonOptions)
                ?? throw new WindowsByeDpiMaintenanceException(code, "Protected metadata deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new WindowsByeDpiMaintenanceException(code, "Protected metadata JSON invalid.", exception);
        }
    }

    private static void WriteJsonAtomic<T>(
        string root,
        string fileName,
        T value,
        bool enforceWindowsSecurity)
    {
        EnsureProtectedDirectory(root, enforceWindowsSecurity);
        var normalizedRoot = NormalizeDirectory(root);
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar)
            || fileName is "." or "..")
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceMetadataNameInvalid",
                "Protected metadata filename invalid.");
        }

        var finalPath = Path.GetFullPath(Path.Combine(normalizedRoot, fileName));
        EnsureStrictChild(normalizedRoot, finalPath, "ByeDpiMaintenanceMetadataOutsideBoundary");
        var tempPath = Path.Combine(
            normalizedRoot,
            fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        EnsureStrictChild(normalizedRoot, tempPath, "ByeDpiMaintenanceMetadataTempOutsideBoundary");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, StrictJsonOptions);
        if (bytes.Length is <= 0 or > MaxMetadataBytes)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceMetadataSizeInvalid",
                "Protected metadata serialized size invalid.");
        }

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            RejectReparsePoint(tempPath, "ByeDpiMaintenanceMetadataTempReparsePoint");
            if (File.Exists(finalPath))
            {
                File.Replace(tempPath, finalPath, null, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
            RejectReparsePoint(finalPath, "ByeDpiMaintenanceMetadataReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyFilesystemSecurity(finalPath, expectDirectory: false);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                RejectReparsePoint(tempPath, "ByeDpiMaintenanceMetadataTempReparsePoint");
                File.Delete(tempPath);
            }
        }
    }

    private static void CleanupStagingJournal(
        string stagingBase,
        string transactionId,
        bool enforceWindowsSecurity)
    {
        var path = ResolveStagingJournalPath(stagingBase, transactionId);
        if (!File.Exists(path))
        {
            return;
        }
        RejectReparsePoint(path, "ByeDpiMaintenanceStagingJournalCleanupReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }
        File.Delete(path);
    }

    private static void DeleteJournal(string maintenanceBase, string transactionId)
    {
        var path = ResolveJournalPath(maintenanceBase, transactionId);
        if (!File.Exists(path))
        {
            return;
        }
        RejectReparsePoint(path, "ByeDpiMaintenanceJournalCleanupReparsePoint");
        File.Delete(path);
    }

    private static int RemoveJournalTemps(string maintenanceBase)
    {
        var root = ResolveJournalRoot(maintenanceBase);
        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            RejectReparsePoint(path, "ByeDpiMaintenanceJournalTempReparsePoint");
            File.Delete(path);
            removed++;
        }
        return removed;
    }

    private static void EnsureNoPendingMaintenanceTransactions(string maintenanceBase)
    {
        var root = ResolveJournalRoot(maintenanceBase);
        if (Directory.EnumerateFiles(root, "*.json", SearchOption.TopDirectoryOnly).Any())
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceRecoveryRequired",
                "Перед новой maintenance operation требуется завершить recovery.");
        }
    }

    private static void DeleteTreeStrict(string root, bool enforceWindowsSecurity)
    {
        if (!Directory.Exists(root))
        {
            return;
        }
        VerifyTreeSecurityOnly(root, enforceWindowsSecurity);
        Directory.Delete(root, recursive: true);
    }

    private static void DeleteEmptyDirectoryStrict(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        RejectReparsePoint(path, "ByeDpiMaintenanceEmptyDirectoryReparsePoint");
        if (Directory.EnumerateFileSystemEntries(path).Any())
        {
            return;
        }
        Directory.Delete(path, recursive: false);
    }

    private static string ResolveProductionManagedBase()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ProgramFilesUnavailable",
                "Не удалось определить Program Files для ByeDPI maintenance.");
        }
        return NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Managed", ByeDpiPackageManifest.RequiredEngineId));
    }

    private static string ResolveProductionMaintenanceBase()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ProgramFilesUnavailable",
                "Не удалось определить Program Files для ByeDPI maintenance.");
        }
        return NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Maintenance", "ByeDPI"));
    }

    private static string ResolveManagedPackageRoot(string managedBase, string version)
    {
        ValidateVersion(version);
        var root = NormalizeDirectory(Path.Combine(managedBase, version, "x64"));
        EnsureStrictChild(managedBase, root, "ByeDpiMaintenanceManagedRootOutsideBoundary");
        return root;
    }

    private static string ResolveStagingTransactionRoot(string stagingBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = NormalizeDirectory(Path.Combine(stagingBase, transactionId));
        EnsureStrictChild(stagingBase, root, "ByeDpiMaintenanceStagingRootOutsideBoundary");
        return root;
    }

    private static string ResolveStagingJournalPath(string stagingBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = NormalizeDirectory(Path.Combine(stagingBase, "Journal"));
        var path = Path.GetFullPath(Path.Combine(root, transactionId + ".json"));
        EnsureStrictChild(root, path, "ByeDpiMaintenanceStagingJournalOutsideBoundary");
        return path;
    }

    private static string ResolveJournalRoot(string maintenanceBase) =>
        NormalizeDirectory(Path.Combine(maintenanceBase, JournalDirectoryName));

    private static string ResolveJournalPath(string maintenanceBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = ResolveJournalRoot(maintenanceBase);
        var path = Path.GetFullPath(Path.Combine(root, transactionId + ".json"));
        EnsureStrictChild(root, path, "ByeDpiMaintenanceJournalOutsideBoundary");
        return path;
    }

    private static string ResolveRepairBackupRootBase(string maintenanceBase) =>
        NormalizeDirectory(Path.Combine(maintenanceBase, RepairBackupDirectoryName));

    private static string ResolveRepairBackupTransactionRoot(string maintenanceBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = NormalizeDirectory(
            Path.Combine(ResolveRepairBackupRootBase(maintenanceBase), transactionId));
        EnsureStrictChild(
            ResolveRepairBackupRootBase(maintenanceBase),
            root,
            "ByeDpiRepairBackupTransactionOutsideBoundary");
        return root;
    }

    private static string ResolveRepairBackupRoot(string maintenanceBase, string transactionId) =>
        NormalizeDirectory(Path.Combine(
            ResolveRepairBackupTransactionRoot(maintenanceBase, transactionId),
            "current-x64"));

    private static string ResolveUninstallTombstoneRootBase(string maintenanceBase) =>
        NormalizeDirectory(Path.Combine(maintenanceBase, UninstallTombstoneDirectoryName));

    private static string ResolveUninstallTombstoneTransactionRoot(
        string maintenanceBase,
        string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = NormalizeDirectory(
            Path.Combine(ResolveUninstallTombstoneRootBase(maintenanceBase), transactionId));
        EnsureStrictChild(
            ResolveUninstallTombstoneRootBase(maintenanceBase),
            root,
            "ByeDpiUninstallTombstoneTransactionOutsideBoundary");
        return root;
    }

    private static string ResolveUninstallTombstoneRoot(string maintenanceBase, string transactionId) =>
        NormalizeDirectory(Path.Combine(
            ResolveUninstallTombstoneTransactionRoot(maintenanceBase, transactionId),
            "byedpi"));

    private static string CreateTransactionId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool SameVolume(string left, string right)
    {
        var leftRoot = Path.GetPathRoot(Path.GetFullPath(left));
        var rightRoot = Path.GetPathRoot(Path.GetFullPath(right));
        return !string.IsNullOrWhiteSpace(leftRoot)
            && !string.IsNullOrWhiteSpace(rightRoot)
            && string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureNoReparsePointsBetween(string root, string target)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedTarget = NormalizeDirectory(target);
        if (!string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            && !normalizedTarget.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceDirectoryOutsideBoundary",
                "Maintenance directory chain вышел за Program Files boundary.");
        }

        RejectReparsePoint(normalizedRoot, "ByeDpiMaintenanceProgramFilesReparsePoint");
        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            current = NormalizeDirectory(Path.Combine(current, segment));
            RejectReparsePoint(current, "ByeDpiMaintenanceDirectoryChainReparsePoint");
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
            throw new WindowsByeDpiMaintenanceException(
                code,
                "ByeDPI maintenance path вышел за fixed boundary.");
        }
    }

    private static void RejectReparsePoint(string path, string code)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsByeDpiMaintenanceException(
                code,
                "Reparse point в ByeDPI maintenance boundary запрещён.");
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static void ValidateTransactionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 32
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceTransactionIdInvalid",
                "Maintenance transaction id должен быть 32-character lowercase hex.");
        }
    }

    private static void ValidateVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceVersionInvalid",
                "Maintenance version invalid.");
        }
        var parts = value.Split('.');
        if (parts.Length != 3
            || parts.Any(static part =>
                part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !part.All(static c => c is >= '0' and <= '9')))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceVersionInvalid",
                "Maintenance version должна иметь canonical major.minor.patch format.");
        }
    }

    private static void ValidateDigest(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiMaintenanceException(
                "ByeDpiMaintenanceDigestInvalid",
                "Maintenance manifest digest должен быть lowercase SHA-256.");
        }
    }

    private static void InjectTestFault(
        WindowsByeDpiMaintenanceTestFaultPoint actual,
        WindowsByeDpiMaintenanceTestFaultPoint expected)
    {
        if (actual == expected)
        {
            throw new WindowsByeDpiMaintenanceException(
                "SyntheticTestFault",
                "Synthetic R42.4 crash point.");
        }
    }

    private sealed record ManagedVerificationEvidence(
        int VerifiedFileCount,
        int VerifiedSecurityObjectCount);

    private sealed record MaintenanceJournalRecord(
        int SchemaVersion,
        string TransactionId,
        ByeDpiPackageLifecycleOperation Operation,
        WindowsByeDpiMaintenancePhase Phase,
        string Version,
        string? ManifestSha256,
        string? SourceTransactionId,
        string? PreviousCurrentVersion,
        string? PreviousRetainedVersion,
        DateTimeOffset UpdatedUtc);

    private sealed class MaintenanceJournalDto
    {
        public int SchemaVersion { get; set; }
        public string? TransactionId { get; set; }
        public string? Operation { get; set; }
        public string? Phase { get; set; }
        public string? Version { get; set; }
        public string? ManifestSha256 { get; set; }
        public string? SourceTransactionId { get; set; }
        public string? PreviousCurrentVersion { get; set; }
        public string? PreviousRetainedVersion { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    private sealed class ManifestDto
    {
        public int SchemaVersion { get; set; }
        public string? EngineId { get; set; }
        public string? Version { get; set; }
        public string? Architecture { get; set; }
        public string? ExecutableRelativePath { get; set; }
        public string? LicenseRelativePath { get; set; }
        public string? ManifestSha256 { get; set; }
        public ManifestFileDto[]? Files { get; set; }
    }

    private sealed class ManifestFileDto
    {
        public string? RelativePath { get; set; }
        public long SizeBytes { get; set; }
        public string? Sha256Hex { get; set; }
    }
}