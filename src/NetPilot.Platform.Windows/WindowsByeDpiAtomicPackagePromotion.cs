using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

public enum WindowsByeDpiPromotionPhase
{
    Prepared = 1,
    PackageMoved = 2,
    StateCommitted = 3
}

public sealed record WindowsByeDpiManagedPackageState(
    string? CurrentVersion,
    string? RetainedPreviousVersion);

public sealed record WindowsByeDpiPromotionResult(
    ByeDpiPackageLifecycleOperation Operation,
    string CurrentVersion,
    string? RetainedPreviousVersion,
    string ManagedPackageRoot,
    int VerifiedFileCount,
    int VerifiedSecurityObjectCount);

public sealed record WindowsByeDpiPromotionRecoveryReport(
    int AbortedPreparedPromotions,
    int CompletedMovedPromotions,
    int CompletedRollbacks,
    int RemovedTemporaryFiles);

public sealed class WindowsByeDpiPromotionException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Capability-like proof created only after the R42.2 staging result, exact manifest,
/// fixed staging journal and (in production) Windows ACL/owner boundary have all been
/// revalidated. It is still not runtime admission and cannot execute native code.
/// </summary>
public sealed class WindowsByeDpiVerifiedPromotionSource
{
    internal WindowsByeDpiVerifiedPromotionSource(
        string transactionId,
        string stagingRoot,
        ByeDpiPackageManifest manifest,
        bool productionProtected)
    {
        TransactionId = transactionId;
        StagingRoot = stagingRoot;
        Manifest = manifest;
        ProductionProtected = productionProtected;
    }

    public string TransactionId { get; }

    public string Version => Manifest.Version;

    public string ManifestSha256 => Manifest.CanonicalManifestSha256;

    internal string StagingRoot { get; }

    internal ByeDpiPackageManifest Manifest { get; }

    internal bool ProductionProtected { get; }
}

/// <summary>
/// Converts an R42.2 result into a trusted promotion source without accepting a raw path.
/// The caller supplies the exact manifest metadata, but the manifest digest, fixed journal,
/// exact file tree and protected staging ACL are all rechecked before trust is granted.
/// </summary>
public static class WindowsByeDpiPromotionSourceVerifier
{
    private const int MaxJournalBytes = 8 * 1024;

    public static Task<WindowsByeDpiVerifiedPromotionSource> VerifyAsync(
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stagedTransaction);
        ArgumentNullException.ThrowIfNull(manifest);
        EnsureWindowsAndLocalSystem();

        return VerifyCoreAsync(
            stagedTransaction,
            manifest,
            ResolveProductionStagingBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    internal static Task<WindowsByeDpiVerifiedPromotionSource> VerifyForTestingAsync(
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageManifest manifest,
        string stagingBase,
        CancellationToken cancellationToken = default) =>
        VerifyCoreAsync(
            stagedTransaction,
            manifest,
            stagingBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    private static async Task<WindowsByeDpiVerifiedPromotionSource> VerifyCoreAsync(
        WindowsByeDpiStagingTransactionResult stagedTransaction,
        ByeDpiPackageManifest manifest,
        string stagingBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stagedTransaction);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);

        ValidateTransactionId(stagedTransaction.TransactionId);
        var normalizedBase = NormalizeDirectory(stagingBase);
        var expectedRoot = NormalizeDirectory(
            Path.Combine(normalizedBase, stagedTransaction.TransactionId));
        EnsureStrictChild(normalizedBase, expectedRoot, "ByeDpiPromotionSourceOutsideStaging");

        if (!string.Equals(
                expectedRoot,
                NormalizeDirectory(stagedTransaction.StagingRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionSourcePathMismatch",
                "R42.2 staging result не соответствует exact transaction root.");
        }

        if (!string.Equals(
                stagedTransaction.ManifestSha256,
                manifest.CanonicalManifestSha256,
                StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionManifestMismatch",
                "R42.2 result и supplied exact manifest имеют разные digest.");
        }

        var journalRoot = NormalizeDirectory(Path.Combine(normalizedBase, "Journal"));
        var journalPath = Path.GetFullPath(
            Path.Combine(journalRoot, stagedTransaction.TransactionId + ".json"));
        EnsureStrictChild(journalRoot, journalPath, "ByeDpiPromotionJournalOutsideBoundary");
        ValidateVerifiedStagingJournal(
            journalPath,
            stagedTransaction.TransactionId,
            manifest);

        var exact = await ByeDpiPackageManifestVerifier.VerifyAsync(
            expectedRoot,
            manifest,
            cancellationToken);
        if (exact.VerifiedFileCount != stagedTransaction.VerifiedFileCount)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionVerifiedFileCountMismatch",
                "R42.2 result и повторная exact verification расходятся по file count.");
        }

        if (enforceWindowsSecurity)
        {
            var security = WindowsByeDpiProtectedStagingSecurityVerifier.Verify(
                stagedTransaction.TransactionId,
                manifest.Files.Select(static file => file.RelativePath).ToArray());
            if (security.VerifiedObjectCount <= 0
                || stagedTransaction.VerifiedSecurityObjectCount <= 0)
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionStagingSecurityMissing",
                    "Protected staging ACL evidence отсутствует.");
            }
        }

        return new WindowsByeDpiVerifiedPromotionSource(
            stagedTransaction.TransactionId,
            expectedRoot,
            manifest,
            enforceWindowsSecurity);
    }

    private static void ValidateVerifiedStagingJournal(
        string journalPath,
        string transactionId,
        ByeDpiPackageManifest manifest)
    {
        if (!File.Exists(journalPath))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiVerifiedStagingJournalMissing",
                "Verified R42.2 staging journal отсутствует.");
        }

        RejectReparsePoint(journalPath, "ByeDpiVerifiedStagingJournalReparsePoint");
        var info = new FileInfo(journalPath);
        if (info.Length is <= 0 or > MaxJournalBytes)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiVerifiedStagingJournalSizeInvalid",
                "R42.2 staging journal имеет недопустимый размер.");
        }

        using var stream = new FileStream(
            journalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 8
        });

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiVerifiedStagingJournalShapeInvalid",
                "R42.2 staging journal должен быть JSON object.");
        }

        var properties = document.RootElement.EnumerateObject()
            .ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
        string[] expected =
        [
            "schemaVersion",
            "transactionId",
            "manifestSha256",
            "version",
            "phase",
            "updatedUtc"
        ];
        if (properties.Count != expected.Length
            || expected.Any(name => !properties.ContainsKey(name))
            || !properties["schemaVersion"].TryGetInt32(out var schemaVersion)
            || schemaVersion != 1
            || properties["transactionId"].ValueKind != JsonValueKind.String
            || properties["manifestSha256"].ValueKind != JsonValueKind.String
            || properties["version"].ValueKind != JsonValueKind.String
            || properties["phase"].ValueKind != JsonValueKind.String
            || !string.Equals(properties["transactionId"].GetString(), transactionId, StringComparison.Ordinal)
            || !string.Equals(properties["manifestSha256"].GetString(), manifest.CanonicalManifestSha256, StringComparison.Ordinal)
            || !string.Equals(properties["version"].GetString(), manifest.Version, StringComparison.Ordinal)
            || !string.Equals(properties["phase"].GetString(), "Verified", StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiVerifiedStagingJournalMismatch",
                "R42.2 staging journal не доказывает exact Verified transaction.");
        }
    }

    internal static string ResolveProductionStagingBase()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsByeDpiPromotionException(
                "ProgramFilesUnavailable",
                "Не удалось определить Program Files.");
        }

        return NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Staging", "ByeDPI"));
    }

    internal static void ValidateTransactionId(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId)
            || transactionId.Length != 32
            || transactionId.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionTransactionIdInvalid",
                "Promotion transaction id должен быть 32-character lowercase hex.");
        }
    }

    internal static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static void EnsureStrictChild(string root, string candidate, string code)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedCandidate = Path.GetFullPath(candidate);
        if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase)
            || !normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiPromotionException(
                code,
                "ByeDPI promotion path вышел за fixed boundary.");
        }
    }

    internal static void RejectReparsePoint(string path, string code)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsByeDpiPromotionException(
                code,
                "Reparse point в ByeDPI promotion boundary запрещён.");
        }
    }

    internal static void EnsureWindowsAndLocalSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ByeDPI atomic promotion доступен только в Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem)
        {
            throw new UnauthorizedAccessException(
                "ByeDPI atomic promotion разрешён только LocalSystem-службе.");
        }
    }
}

/// <summary>
/// R42.3 service-owned package promotion boundary. Install/upgrade atomically rename a
/// verified R42.2 transaction into the canonical versioned managed root. Rollback never
/// accepts a user target: it can only swap to the exact retained previous version recorded
/// by protected package state. No method launches native code or changes runtime admission.
/// </summary>
public static class WindowsByeDpiAtomicPackagePromoter
{
    private const int StateSchemaVersion = 1;
    private const int PromotionJournalSchemaVersion = 1;
    private const int MaxMetadataBytes = 64 * 1024;
    private const string StateDirectoryName = ".state";
    private const string JournalDirectoryName = "journal";
    private const string IncomingManifestDirectoryName = "incoming-manifests";
    private const string ManifestDirectoryName = "manifests";
    private const string StateFileName = "package-state.json";

    private static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static Task<WindowsByeDpiPromotionResult> PromoteAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        ByeDpiPackageLifecycleOperation operation,
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        if (!source.ProductionProtected)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionSourceNotProductionProtected",
                "Production promotion требует source, повторно проверенный в protected Program Files staging.");
        }

        return PromoteCoreAsync(
            source,
            operation,
            engineLeaseActive,
            ResolveProductionManagedBase(),
            WindowsByeDpiPromotionSourceVerifier.ResolveProductionStagingBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    public static Task<WindowsByeDpiPromotionResult> RollbackAsync(
        bool engineLeaseActive,
        CancellationToken cancellationToken = default)
    {
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        return RollbackCoreAsync(
            engineLeaseActive,
            ResolveProductionManagedBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    public static Task<WindowsByeDpiPromotionRecoveryReport> RecoverIncompleteOperationsAsync(
        CancellationToken cancellationToken = default)
    {
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        return RecoverCoreAsync(
            ResolveProductionManagedBase(),
            WindowsByeDpiPromotionSourceVerifier.ResolveProductionStagingBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    public static WindowsByeDpiManagedPackageState GetState()
    {
        WindowsByeDpiPromotionSourceVerifier.EnsureWindowsAndLocalSystem();
        var managedBase = ResolveProductionManagedBase();
        if (!Directory.Exists(managedBase))
        {
            return new WindowsByeDpiManagedPackageState(null, null);
        }

        PrepareManagedBase(managedBase, enforceWindowsSecurity: true);
        return ReadState(managedBase, enforceWindowsSecurity: true);
    }

    internal static Task<WindowsByeDpiPromotionResult> PromoteForTestingAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        ByeDpiPackageLifecycleOperation operation,
        bool engineLeaseActive,
        string managedBase,
        string stagingBase,
        CancellationToken cancellationToken = default) =>
        PromoteCoreAsync(
            source,
            operation,
            engineLeaseActive,
            managedBase,
            stagingBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    internal static Task<WindowsByeDpiPromotionResult> RollbackForTestingAsync(
        bool engineLeaseActive,
        string managedBase,
        CancellationToken cancellationToken = default) =>
        RollbackCoreAsync(
            engineLeaseActive,
            managedBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    internal static Task<WindowsByeDpiPromotionRecoveryReport> RecoverForTestingAsync(
        string managedBase,
        string stagingBase,
        CancellationToken cancellationToken = default) =>
        RecoverCoreAsync(
            managedBase,
            stagingBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    internal static WindowsByeDpiManagedPackageState GetStateForTesting(string managedBase)
    {
        PrepareManagedBase(managedBase, enforceWindowsSecurity: false);
        return ReadState(managedBase, enforceWindowsSecurity: false);
    }

    private static async Task<WindowsByeDpiPromotionResult> PromoteCoreAsync(
        WindowsByeDpiVerifiedPromotionSource source,
        ByeDpiPackageLifecycleOperation operation,
        bool engineLeaseActive,
        string managedBase,
        string stagingBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (operation is not ByeDpiPackageLifecycleOperation.Install
            and not ByeDpiPackageLifecycleOperation.Upgrade)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionOperationUnsupported",
                "R42.3 atomic promotion поддерживает только Install/Upgrade; rollback имеет отдельный target-free API.");
        }

        var normalizedManagedBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(managedBase);
        var normalizedStagingBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(stagingBase);
        PrepareManagedBase(normalizedManagedBase, enforceWindowsSecurity);
        ValidateSourceForPromotion(source, normalizedStagingBase, enforceWindowsSecurity);

        if (!SameVolume(normalizedManagedBase, source.StagingRoot))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionCrossVolume",
                "Atomic package promotion требует staging и managed root на одном volume.");
        }

        var before = ReadState(normalizedManagedBase, enforceWindowsSecurity);
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            new ByeDpiPackageLifecycleRequest(
                operation,
                source.Version,
                before.CurrentVersion,
                before.RetainedPreviousVersion,
                PackageVerified: true,
                ProtectedStaging: true,
                SameVolumeStaging: true,
                EngineLeaseActive: engineLeaseActive));
        if (!decision.Allowed || decision.CanonicalTargetVersion is null)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionLifecycleBlocked_" + decision.Code,
                "ByeDPI lifecycle policy заблокировала atomic promotion.");
        }

        var targetVersion = decision.CanonicalTargetVersion;
        var targetRoot = ResolveManagedPackageRoot(normalizedManagedBase, targetVersion);
        if (Directory.Exists(targetRoot) || File.Exists(targetRoot))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionTargetAlreadyExists",
                "Atomic promotion не перезаписывает существующую immutable ByeDPI version.");
        }

        var desired = operation == ByeDpiPackageLifecycleOperation.Install
            ? new WindowsByeDpiManagedPackageState(targetVersion, null)
            : new WindowsByeDpiManagedPackageState(targetVersion, before.CurrentVersion);

        WriteIncomingManifest(normalizedManagedBase, source.TransactionId, source.Manifest, enforceWindowsSecurity);
        WritePromotionJournal(
            normalizedManagedBase,
            new PromotionJournalRecord(
                PromotionJournalSchemaVersion,
                source.TransactionId,
                operation,
                source.ManifestSha256,
                targetVersion,
                before.CurrentVersion,
                before.RetainedPreviousVersion,
                WindowsByeDpiPromotionPhase.Prepared,
                DateTimeOffset.UtcNow),
            enforceWindowsSecurity);

        try
        {
            var versionRoot = Path.GetDirectoryName(targetRoot)
                ?? throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionVersionRootMissing",
                    "Не удалось определить managed version root.");
            EnsureProtectedDirectory(versionRoot, enforceWindowsSecurity);
            if (Directory.EnumerateFileSystemEntries(versionRoot).Any())
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionVersionRootNotEmpty",
                    "Новая managed version directory должна быть пустой до atomic rename.");
            }

            Directory.Move(source.StagingRoot, targetRoot);
            WritePromotionJournal(
                normalizedManagedBase,
                new PromotionJournalRecord(
                    PromotionJournalSchemaVersion,
                    source.TransactionId,
                    operation,
                    source.ManifestSha256,
                    targetVersion,
                    before.CurrentVersion,
                    before.RetainedPreviousVersion,
                    WindowsByeDpiPromotionPhase.PackageMoved,
                    DateTimeOffset.UtcNow),
                enforceWindowsSecurity);

            var evidence = await VerifyManagedPackageAsync(
                normalizedManagedBase,
                targetVersion,
                source.Manifest,
                enforceWindowsSecurity,
                cancellationToken);

            WriteCanonicalManifest(normalizedManagedBase, source.Manifest, enforceWindowsSecurity);
            WriteState(normalizedManagedBase, desired, enforceWindowsSecurity);
            WritePromotionJournal(
                normalizedManagedBase,
                new PromotionJournalRecord(
                    PromotionJournalSchemaVersion,
                    source.TransactionId,
                    operation,
                    source.ManifestSha256,
                    targetVersion,
                    before.CurrentVersion,
                    before.RetainedPreviousVersion,
                    WindowsByeDpiPromotionPhase.StateCommitted,
                    DateTimeOffset.UtcNow),
                enforceWindowsSecurity);

            CleanupCompletedPromotion(
                normalizedManagedBase,
                normalizedStagingBase,
                source.TransactionId,
                enforceWindowsSecurity);

            return new WindowsByeDpiPromotionResult(
                operation,
                desired.CurrentVersion!,
                desired.RetainedPreviousVersion,
                targetRoot,
                evidence.VerifiedFileCount,
                evidence.VerifiedSecurityObjectCount);
        }
        catch
        {
            // Recovery journal is intentionally retained after any mutation boundary.
            // If the package was not moved yet, recovery aborts and preserves R42.2 staging.
            throw;
        }
    }

    private static async Task<WindowsByeDpiPromotionResult> RollbackCoreAsync(
        bool engineLeaseActive,
        string managedBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        var normalizedManagedBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(managedBase);
        PrepareManagedBase(normalizedManagedBase, enforceWindowsSecurity);
        var before = ReadState(normalizedManagedBase, enforceWindowsSecurity);
        if (before.CurrentVersion is null || before.RetainedPreviousVersion is null)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiRollbackUnavailable",
                "Rollback невозможен без current и retained previous package.");
        }

        var targetManifest = ReadCanonicalManifest(
            normalizedManagedBase,
            before.RetainedPreviousVersion,
            enforceWindowsSecurity);
        var targetRoot = ResolveManagedPackageRoot(
            normalizedManagedBase,
            before.RetainedPreviousVersion);
        var sameVolume = SameVolume(normalizedManagedBase, targetRoot);
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            new ByeDpiPackageLifecycleRequest(
                ByeDpiPackageLifecycleOperation.Rollback,
                before.RetainedPreviousVersion,
                before.CurrentVersion,
                before.RetainedPreviousVersion,
                PackageVerified: true,
                ProtectedStaging: true,
                SameVolumeStaging: sameVolume,
                EngineLeaseActive: engineLeaseActive));
        if (!decision.Allowed || decision.CanonicalTargetVersion is null)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiRollbackLifecycleBlocked_" + decision.Code,
                "ByeDPI lifecycle policy заблокировала rollback.");
        }

        var evidence = await VerifyManagedPackageAsync(
            normalizedManagedBase,
            decision.CanonicalTargetVersion,
            targetManifest,
            enforceWindowsSecurity,
            cancellationToken);

        var transactionId = CreateTransactionId();
        WritePromotionJournal(
            normalizedManagedBase,
            new PromotionJournalRecord(
                PromotionJournalSchemaVersion,
                transactionId,
                ByeDpiPackageLifecycleOperation.Rollback,
                targetManifest.CanonicalManifestSha256,
                decision.CanonicalTargetVersion,
                before.CurrentVersion,
                before.RetainedPreviousVersion,
                WindowsByeDpiPromotionPhase.Prepared,
                DateTimeOffset.UtcNow),
            enforceWindowsSecurity);

        var desired = new WindowsByeDpiManagedPackageState(
            before.RetainedPreviousVersion,
            before.CurrentVersion);
        WriteState(normalizedManagedBase, desired, enforceWindowsSecurity);
        WritePromotionJournal(
            normalizedManagedBase,
            new PromotionJournalRecord(
                PromotionJournalSchemaVersion,
                transactionId,
                ByeDpiPackageLifecycleOperation.Rollback,
                targetManifest.CanonicalManifestSha256,
                decision.CanonicalTargetVersion,
                before.CurrentVersion,
                before.RetainedPreviousVersion,
                WindowsByeDpiPromotionPhase.StateCommitted,
                DateTimeOffset.UtcNow),
            enforceWindowsSecurity);
        DeletePromotionJournal(normalizedManagedBase, transactionId);

        return new WindowsByeDpiPromotionResult(
            ByeDpiPackageLifecycleOperation.Rollback,
            desired.CurrentVersion!,
            desired.RetainedPreviousVersion,
            ResolveManagedPackageRoot(normalizedManagedBase, desired.CurrentVersion!),
            evidence.VerifiedFileCount,
            evidence.VerifiedSecurityObjectCount);
    }

    private static async Task<WindowsByeDpiPromotionRecoveryReport> RecoverCoreAsync(
        string managedBase,
        string stagingBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        var normalizedManagedBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(managedBase);
        var normalizedStagingBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(stagingBase);
        if (!Directory.Exists(normalizedManagedBase))
        {
            return new WindowsByeDpiPromotionRecoveryReport(0, 0, 0, 0);
        }

        PrepareManagedBase(normalizedManagedBase, enforceWindowsSecurity);
        var removedTemps = RemoveTemporaryMetadataFiles(normalizedManagedBase);
        var aborted = 0;
        var completed = 0;
        var rollbacks = 0;

        foreach (var journalPath in Directory.EnumerateFiles(
                     ResolveJournalRoot(normalizedManagedBase),
                     "*.json",
                     SearchOption.TopDirectoryOnly)
                 .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = ReadPromotionJournal(journalPath, enforceWindowsSecurity);
            var currentState = ReadState(normalizedManagedBase, enforceWindowsSecurity);

            if (record.Operation == ByeDpiPackageLifecycleOperation.Rollback)
            {
                if (record.PreviousCurrentVersion is null || record.PreviousRetainedVersion is null)
                {
                    throw new WindowsByeDpiPromotionException(
                        "ByeDpiRollbackJournalStateInvalid",
                        "Rollback journal не содержит exact prior state.");
                }

                var desired = new WindowsByeDpiManagedPackageState(
                    record.PreviousRetainedVersion,
                    record.PreviousCurrentVersion);
                EnsureStateIsPriorOrDesired(
                    currentState,
                    new WindowsByeDpiManagedPackageState(
                        record.PreviousCurrentVersion,
                        record.PreviousRetainedVersion),
                    desired);
                var manifest = ReadCanonicalManifest(
                    normalizedManagedBase,
                    desired.CurrentVersion!,
                    enforceWindowsSecurity);
                await VerifyManagedPackageAsync(
                    normalizedManagedBase,
                    desired.CurrentVersion!,
                    manifest,
                    enforceWindowsSecurity,
                    cancellationToken);
                WriteState(normalizedManagedBase, desired, enforceWindowsSecurity);
                DeletePromotionJournal(normalizedManagedBase, record.TransactionId);
                rollbacks++;
                continue;
            }

            if (record.Operation is not ByeDpiPackageLifecycleOperation.Install
                and not ByeDpiPackageLifecycleOperation.Upgrade)
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionJournalOperationInvalid",
                    "Recovery journal содержит неподдерживаемую lifecycle operation.");
            }

            var stagingRoot = ResolveStagingTransactionRoot(
                normalizedStagingBase,
                record.TransactionId);
            var targetRoot = ResolveManagedPackageRoot(
                normalizedManagedBase,
                record.TargetVersion);
            var stagingExists = Directory.Exists(stagingRoot);
            var targetExists = Directory.Exists(targetRoot);

            if (record.Phase == WindowsByeDpiPromotionPhase.Prepared
                && stagingExists
                && !targetExists)
            {
                DeleteIncomingManifest(normalizedManagedBase, record.TransactionId);
                DeletePromotionJournal(normalizedManagedBase, record.TransactionId);
                aborted++;
                continue;
            }

            if (stagingExists || !targetExists)
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionRecoveryStateAmbiguous",
                    "Promotion recovery обнаружил неоднозначное сочетание staging/managed roots.");
            }

            var incomingManifest = ReadIncomingManifest(
                normalizedManagedBase,
                record.TransactionId,
                enforceWindowsSecurity);
            if (!string.Equals(
                    incomingManifest.CanonicalManifestSha256,
                    record.ManifestSha256,
                    StringComparison.Ordinal)
                || !string.Equals(
                    incomingManifest.Version,
                    record.TargetVersion,
                    StringComparison.Ordinal))
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiPromotionRecoveryManifestMismatch",
                    "Incoming manifest не соответствует promotion recovery journal.");
            }

            await VerifyManagedPackageAsync(
                normalizedManagedBase,
                record.TargetVersion,
                incomingManifest,
                enforceWindowsSecurity,
                cancellationToken);

            var prior = new WindowsByeDpiManagedPackageState(
                record.PreviousCurrentVersion,
                record.PreviousRetainedVersion);
            var desiredState = record.Operation == ByeDpiPackageLifecycleOperation.Install
                ? new WindowsByeDpiManagedPackageState(record.TargetVersion, null)
                : new WindowsByeDpiManagedPackageState(
                    record.TargetVersion,
                    record.PreviousCurrentVersion);
            EnsureStateIsPriorOrDesired(currentState, prior, desiredState);
            WriteCanonicalManifest(normalizedManagedBase, incomingManifest, enforceWindowsSecurity);
            WriteState(normalizedManagedBase, desiredState, enforceWindowsSecurity);
            CleanupCompletedPromotion(
                normalizedManagedBase,
                normalizedStagingBase,
                record.TransactionId,
                enforceWindowsSecurity);
            completed++;
        }

        return new WindowsByeDpiPromotionRecoveryReport(
            aborted,
            completed,
            rollbacks,
            removedTemps);
    }

    private static void ValidateSourceForPromotion(
        WindowsByeDpiVerifiedPromotionSource source,
        string stagingBase,
        bool enforceWindowsSecurity)
    {
        WindowsByeDpiPromotionSourceVerifier.ValidateTransactionId(source.TransactionId);
        var expected = ResolveStagingTransactionRoot(stagingBase, source.TransactionId);
        if (!string.Equals(
                expected,
                WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(source.StagingRoot),
                StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(expected))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionSourceNoLongerValid",
                "Verified promotion source больше не соответствует exact staging transaction.");
        }

        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            expected,
            "ByeDpiPromotionSourceReparsePoint");
        if (enforceWindowsSecurity && !source.ProductionProtected)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionSourceNotProtected",
                "Production source не имеет protected staging proof.");
        }
    }

    private static async Task<ManagedVerificationEvidence> VerifyManagedPackageAsync(
        string managedBase,
        string version,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(version, manifest.Version, StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedManifestVersionMismatch",
                "Managed package version и exact manifest расходятся.");
        }

        var root = ResolveManagedPackageRoot(managedBase, version);
        var exact = await ByeDpiPackageManifestVerifier.VerifyAsync(
            root,
            manifest,
            cancellationToken);
        var securityCount = 0;
        if (enforceWindowsSecurity)
        {
            securityCount = VerifyManagedSecurity(
                managedBase,
                root,
                manifest.Files.Select(static file => file.RelativePath).ToArray());
        }

        return new ManagedVerificationEvidence(
            exact.VerifiedFileCount,
            securityCount);
    }

    private static int VerifyManagedSecurity(
        string managedBase,
        string packageRoot,
        IReadOnlyCollection<string> relativeFiles)
    {
        var programFiles = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDirectoryChain(programFiles, packageRoot, directories);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in relativeFiles)
        {
            var fullPath = Path.GetFullPath(
                Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            WindowsByeDpiPromotionSourceVerifier.EnsureStrictChild(
                packageRoot,
                fullPath,
                "ByeDpiManagedFileOutsideBoundary");
            if (!files.Add(fullPath))
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiManagedFileCollision",
                    "Managed package содержит duplicate/case-collision file path.");
            }

            var parent = Path.GetDirectoryName(fullPath)
                ?? throw new WindowsByeDpiPromotionException(
                    "ByeDpiManagedFileParentMissing",
                    "Не удалось определить managed package parent directory.");
            AddDirectoryChain(packageRoot, parent, directories);
        }

        foreach (var directory in directories)
        {
            VerifyFilesystemSecurity(directory, expectDirectory: true);
        }
        foreach (var file in files)
        {
            VerifyFilesystemSecurity(file, expectDirectory: false);
        }

        return directories.Count + files.Count;
    }

    private static void VerifyFilesystemSecurity(string path, bool expectDirectory)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedSecurityObjectMissing",
                "Managed package filesystem object исчез до ACL verification.");
        }

        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            path,
            "ByeDpiManagedReparsePoint");
        var attributes = File.GetAttributes(path);
        if (((attributes & FileAttributes.Directory) != 0) != expectDirectory)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedSecurityObjectTypeMismatch",
                "Managed package filesystem object имеет неожиданный type.");
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

    private static void AddDirectoryChain(string root, string target, ISet<string> output)
    {
        var normalizedRoot = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(root);
        var normalizedTarget = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(target);
        if (!string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase)
            && !normalizedTarget.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedDirectoryOutsideBoundary",
                "Managed directory chain вышел за fixed boundary.");
        }

        output.Add(normalizedRoot);
        if (string.Equals(normalizedRoot, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or "..")
            {
                throw new WindowsByeDpiPromotionException(
                    "ByeDpiManagedDirectorySegmentInvalid",
                    "Managed directory chain содержит неоднозначный segment.");
            }

            current = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
                Path.Combine(current, segment));
            output.Add(current);
        }
    }

    private static void PrepareManagedBase(string managedBase, bool enforceWindowsSecurity)
    {
        var normalized = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(managedBase);
        if (!enforceWindowsSecurity)
        {
            Directory.CreateDirectory(normalized);
            Directory.CreateDirectory(ResolveStateRoot(normalized));
            Directory.CreateDirectory(ResolveJournalRoot(normalized));
            Directory.CreateDirectory(ResolveIncomingManifestRoot(normalized));
            Directory.CreateDirectory(ResolveManifestRoot(normalized));
            return;
        }

        var expected = ResolveProductionManagedBase();
        if (!string.Equals(normalized, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedBaseMismatch",
                "Production promotion может использовать только fixed Program Files managed root.");
        }

        var programFiles = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var enginesRoot = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines"));
        Directory.CreateDirectory(enginesRoot);
        EnsureNoReparsePointsBetween(programFiles, enginesRoot);

        var managedRoot = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(enginesRoot, "Managed"));
        EnsureProtectedDirectory(managedRoot, enforceWindowsSecurity: true);
        EnsureProtectedDirectory(normalized, enforceWindowsSecurity: true);
        EnsureProtectedDirectory(ResolveStateRoot(normalized), enforceWindowsSecurity: true);
        EnsureProtectedDirectory(ResolveJournalRoot(normalized), enforceWindowsSecurity: true);
        EnsureProtectedDirectory(ResolveIncomingManifestRoot(normalized), enforceWindowsSecurity: true);
        EnsureProtectedDirectory(ResolveManifestRoot(normalized), enforceWindowsSecurity: true);
    }

    private static void EnsureProtectedDirectory(string path, bool enforceWindowsSecurity)
    {
        var fullPath = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(path);
        if (!enforceWindowsSecurity)
        {
            Directory.CreateDirectory(fullPath);
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                fullPath,
                "ByeDpiManagedDirectoryReparsePoint");
            return;
        }

        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedParentMissing",
                "Не удалось определить parent managed directory.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(parent);
        }
        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            parent,
            "ByeDpiManagedParentReparsePoint");

        if (Directory.Exists(fullPath))
        {
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                fullPath,
                "ByeDpiManagedDirectoryReparsePoint");
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

    private static WindowsByeDpiManagedPackageState ReadState(
        string managedBase,
        bool enforceWindowsSecurity)
    {
        var path = ResolveStatePath(managedBase);
        if (!File.Exists(path))
        {
            return new WindowsByeDpiManagedPackageState(null, null);
        }
        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            path,
            "ByeDpiStateReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }

        var dto = ReadJsonStrict<PackageStateDto>(path, "ByeDpiStateInvalid");
        if (dto.SchemaVersion != StateSchemaVersion)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiStateSchemaInvalid",
                "Package state использует неизвестную schema version.");
        }
        ValidateOptionalVersion(dto.CurrentVersion, "ByeDpiStateCurrentVersionInvalid");
        ValidateOptionalVersion(dto.RetainedPreviousVersion, "ByeDpiStatePreviousVersionInvalid");
        if (dto.CurrentVersion is null && dto.RetainedPreviousVersion is not null)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiStateShapeInvalid",
                "Package state не может иметь previous без current.");
        }
        if (dto.CurrentVersion is not null
            && string.Equals(dto.CurrentVersion, dto.RetainedPreviousVersion, StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiStateVersionCollision",
                "Current и retained previous version не могут совпадать.");
        }

        return new WindowsByeDpiManagedPackageState(
            dto.CurrentVersion,
            dto.RetainedPreviousVersion);
    }

    private static void WriteState(
        string managedBase,
        WindowsByeDpiManagedPackageState state,
        bool enforceWindowsSecurity)
    {
        ValidateOptionalVersion(state.CurrentVersion, "ByeDpiStateCurrentVersionInvalid");
        ValidateOptionalVersion(state.RetainedPreviousVersion, "ByeDpiStatePreviousVersionInvalid");
        var dto = new PackageStateDto
        {
            SchemaVersion = StateSchemaVersion,
            CurrentVersion = state.CurrentVersion,
            RetainedPreviousVersion = state.RetainedPreviousVersion,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };
        WriteJsonAtomic(
            ResolveStateRoot(managedBase),
            StateFileName,
            dto,
            enforceWindowsSecurity);
    }

    private static void WriteIncomingManifest(
        string managedBase,
        string transactionId,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity) =>
        WriteManifestFile(
            ResolveIncomingManifestRoot(managedBase),
            transactionId + ".json",
            manifest,
            enforceWindowsSecurity);

    private static void WriteCanonicalManifest(
        string managedBase,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity) =>
        WriteManifestFile(
            ResolveManifestRoot(managedBase),
            manifest.Version + ".json",
            manifest,
            enforceWindowsSecurity);

    private static ByeDpiPackageManifest ReadIncomingManifest(
        string managedBase,
        string transactionId,
        bool enforceWindowsSecurity) =>
        ReadManifestFile(
            Path.Combine(ResolveIncomingManifestRoot(managedBase), transactionId + ".json"),
            enforceWindowsSecurity);

    private static ByeDpiPackageManifest ReadCanonicalManifest(
        string managedBase,
        string version,
        bool enforceWindowsSecurity)
    {
        ValidateVersion(version, "ByeDpiManifestVersionInvalid");
        return ReadManifestFile(
            Path.Combine(ResolveManifestRoot(managedBase), version + ".json"),
            enforceWindowsSecurity);
    }

    private static void WriteManifestFile(
        string root,
        string fileName,
        ByeDpiPackageManifest manifest,
        bool enforceWindowsSecurity)
    {
        var dto = new ManifestDto
        {
            SchemaVersion = 1,
            EngineId = manifest.EngineId,
            Version = manifest.Version,
            Architecture = manifest.Architecture,
            ExecutableRelativePath = manifest.ExecutableRelativePath,
            LicenseRelativePath = manifest.LicenseRelativePath,
            ManifestSha256 = manifest.CanonicalManifestSha256,
            Files = manifest.Files.Select(static file => new ManifestFileDto
            {
                RelativePath = file.RelativePath,
                SizeBytes = file.SizeBytes,
                Sha256Hex = file.Sha256Hex
            }).ToArray()
        };
        WriteJsonAtomic(root, fileName, dto, enforceWindowsSecurity);
    }

    private static ByeDpiPackageManifest ReadManifestFile(
        string path,
        bool enforceWindowsSecurity)
    {
        if (!File.Exists(path))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedManifestMissing",
                "Exact managed package manifest отсутствует.");
        }
        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            path,
            "ByeDpiManagedManifestReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }

        var dto = ReadJsonStrict<ManifestDto>(path, "ByeDpiManagedManifestInvalid");
        if (dto.SchemaVersion != 1
            || dto.Files is null
            || dto.EngineId is null
            || dto.Version is null
            || dto.Architecture is null
            || dto.ExecutableRelativePath is null
            || dto.LicenseRelativePath is null
            || dto.ManifestSha256 is null)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedManifestShapeInvalid",
                "Managed package manifest имеет неверную shape.");
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
        if (!string.Equals(
                manifest.CanonicalManifestSha256,
                dto.ManifestSha256,
                StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedManifestDigestMismatch",
                "Persisted managed manifest digest не совпадает с canonical manifest.");
        }
        return manifest;
    }

    private static void WritePromotionJournal(
        string managedBase,
        PromotionJournalRecord record,
        bool enforceWindowsSecurity)
    {
        ValidatePromotionJournal(record);
        var dto = new PromotionJournalDto
        {
            SchemaVersion = record.SchemaVersion,
            TransactionId = record.TransactionId,
            Operation = record.Operation.ToString(),
            ManifestSha256 = record.ManifestSha256,
            TargetVersion = record.TargetVersion,
            PreviousCurrentVersion = record.PreviousCurrentVersion,
            PreviousRetainedVersion = record.PreviousRetainedVersion,
            Phase = record.Phase.ToString(),
            UpdatedUtc = record.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)
        };
        WriteJsonAtomic(
            ResolveJournalRoot(managedBase),
            record.TransactionId + ".json",
            dto,
            enforceWindowsSecurity);
    }

    private static PromotionJournalRecord ReadPromotionJournal(
        string path,
        bool enforceWindowsSecurity)
    {
        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            path,
            "ByeDpiPromotionJournalReparsePoint");
        if (enforceWindowsSecurity)
        {
            VerifyFilesystemSecurity(path, expectDirectory: false);
        }
        var fileName = Path.GetFileName(path);
        if (!fileName.EndsWith(".json", StringComparison.Ordinal))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionJournalNameInvalid",
                "Promotion journal filename invalid.");
        }
        var transactionId = fileName[..^5];
        WindowsByeDpiPromotionSourceVerifier.ValidateTransactionId(transactionId);

        var dto = ReadJsonStrict<PromotionJournalDto>(path, "ByeDpiPromotionJournalInvalid");
        if (dto.SchemaVersion != PromotionJournalSchemaVersion
            || !string.Equals(dto.TransactionId, transactionId, StringComparison.Ordinal)
            || !Enum.TryParse<ByeDpiPackageLifecycleOperation>(dto.Operation, false, out var operation)
            || !Enum.IsDefined(operation)
            || !Enum.TryParse<WindowsByeDpiPromotionPhase>(dto.Phase, false, out var phase)
            || !Enum.IsDefined(phase)
            || dto.ManifestSha256 is null
            || dto.TargetVersion is null
            || dto.UpdatedUtc is null
            || !DateTimeOffset.TryParseExact(
                dto.UpdatedUtc,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedUtc))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionJournalShapeInvalid",
                "Promotion recovery journal имеет неверную shape.");
        }

        var record = new PromotionJournalRecord(
            dto.SchemaVersion,
            transactionId,
            operation,
            dto.ManifestSha256,
            dto.TargetVersion,
            dto.PreviousCurrentVersion,
            dto.PreviousRetainedVersion,
            phase,
            updatedUtc);
        ValidatePromotionJournal(record);
        return record;
    }

    private static void ValidatePromotionJournal(PromotionJournalRecord record)
    {
        if (record.SchemaVersion != PromotionJournalSchemaVersion)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionJournalSchemaInvalid",
                "Promotion journal schema version invalid.");
        }
        WindowsByeDpiPromotionSourceVerifier.ValidateTransactionId(record.TransactionId);
        ValidateDigest(record.ManifestSha256, "ByeDpiPromotionJournalDigestInvalid");
        ValidateVersion(record.TargetVersion, "ByeDpiPromotionJournalTargetInvalid");
        ValidateOptionalVersion(record.PreviousCurrentVersion, "ByeDpiPromotionJournalCurrentInvalid");
        ValidateOptionalVersion(record.PreviousRetainedVersion, "ByeDpiPromotionJournalPreviousInvalid");
        if (!Enum.IsDefined(record.Operation) || !Enum.IsDefined(record.Phase))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionJournalEnumInvalid",
                "Promotion journal enum value invalid.");
        }
    }

    private static T ReadJsonStrict<T>(string path, string code)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > MaxMetadataBytes)
        {
            throw new WindowsByeDpiPromotionException(code, "Protected metadata file size invalid.");
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
                ?? throw new WindowsByeDpiPromotionException(code, "Protected metadata deserialized to null.");
        }
        catch (JsonException exception)
        {
            throw new WindowsByeDpiPromotionException(code, "Protected metadata JSON invalid.", exception);
        }
    }

    private static void WriteJsonAtomic<T>(
        string root,
        string fileName,
        T value,
        bool enforceWindowsSecurity)
    {
        var normalizedRoot = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(root);
        EnsureProtectedDirectory(normalizedRoot, enforceWindowsSecurity);
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar)
            || fileName is "." or "..")
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiMetadataFileNameInvalid",
                "Protected metadata filename invalid.");
        }

        var finalPath = Path.GetFullPath(Path.Combine(normalizedRoot, fileName));
        WindowsByeDpiPromotionSourceVerifier.EnsureStrictChild(
            normalizedRoot,
            finalPath,
            "ByeDpiMetadataOutsideBoundary");
        var tempPath = Path.Combine(
            normalizedRoot,
            fileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        WindowsByeDpiPromotionSourceVerifier.EnsureStrictChild(
            normalizedRoot,
            tempPath,
            "ByeDpiMetadataTempOutsideBoundary");

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, StrictJsonOptions);
        if (bytes.Length is <= 0 or > MaxMetadataBytes)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiMetadataSizeInvalid",
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
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                tempPath,
                "ByeDpiMetadataTempReparsePoint");

            if (File.Exists(finalPath))
            {
                File.Replace(tempPath, finalPath, destinationBackupFileName: null, ignoreMetadataErrors: false);
            }
            else
            {
                File.Move(tempPath, finalPath);
            }
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                finalPath,
                "ByeDpiMetadataReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyFilesystemSecurity(finalPath, expectDirectory: false);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                    tempPath,
                    "ByeDpiMetadataTempReparsePoint");
                File.Delete(tempPath);
            }
        }
    }

    private static void CleanupCompletedPromotion(
        string managedBase,
        string stagingBase,
        string transactionId,
        bool enforceWindowsSecurity)
    {
        DeleteIncomingManifest(managedBase, transactionId);
        DeletePromotionJournal(managedBase, transactionId);

        var stagingJournal = Path.Combine(
            stagingBase,
            "Journal",
            transactionId + ".json");
        if (File.Exists(stagingJournal))
        {
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                stagingJournal,
                "ByeDpiStagingJournalCleanupReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyFilesystemSecurity(stagingJournal, expectDirectory: false);
            }
            File.Delete(stagingJournal);
        }
    }

    private static void DeleteIncomingManifest(string managedBase, string transactionId)
    {
        var path = Path.Combine(
            ResolveIncomingManifestRoot(managedBase),
            transactionId + ".json");
        if (File.Exists(path))
        {
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                path,
                "ByeDpiIncomingManifestCleanupReparsePoint");
            File.Delete(path);
        }
    }

    private static void DeletePromotionJournal(string managedBase, string transactionId)
    {
        var path = Path.Combine(
            ResolveJournalRoot(managedBase),
            transactionId + ".json");
        if (File.Exists(path))
        {
            WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                path,
                "ByeDpiPromotionJournalCleanupReparsePoint");
            File.Delete(path);
        }
    }

    private static int RemoveTemporaryMetadataFiles(string managedBase)
    {
        var removed = 0;
        foreach (var root in new[]
                 {
                     ResolveStateRoot(managedBase),
                     ResolveJournalRoot(managedBase),
                     ResolveIncomingManifestRoot(managedBase),
                     ResolveManifestRoot(managedBase)
                 })
        {
            foreach (var path in Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly))
            {
                WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                    path,
                    "ByeDpiMetadataTempReparsePoint");
                File.Delete(path);
                removed++;
            }
        }
        return removed;
    }

    private static void EnsureStateIsPriorOrDesired(
        WindowsByeDpiManagedPackageState actual,
        WindowsByeDpiManagedPackageState prior,
        WindowsByeDpiManagedPackageState desired)
    {
        if (actual != prior && actual != desired)
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiPromotionStateDiverged",
                "Protected package state изменился вне ожидаемой transaction boundary.");
        }
    }

    private static string ResolveProductionManagedBase()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsByeDpiPromotionException(
                "ProgramFilesUnavailable",
                "Не удалось определить Program Files.");
        }
        return WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(
                programFiles,
                "NetPilot",
                "Engines",
                "Managed",
                ByeDpiPackageManifest.RequiredEngineId));
    }

    private static string ResolveManagedPackageRoot(string managedBase, string version)
    {
        ValidateVersion(version, "ByeDpiManagedVersionInvalid");
        var normalizedBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(managedBase);
        var root = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(normalizedBase, version, ByeDpiPackageManifest.RequiredArchitecture));
        WindowsByeDpiPromotionSourceVerifier.EnsureStrictChild(
            normalizedBase,
            root,
            "ByeDpiManagedPackageOutsideBoundary");
        return root;
    }

    private static string ResolveStagingTransactionRoot(string stagingBase, string transactionId)
    {
        WindowsByeDpiPromotionSourceVerifier.ValidateTransactionId(transactionId);
        var normalizedBase = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(stagingBase);
        var root = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(normalizedBase, transactionId));
        WindowsByeDpiPromotionSourceVerifier.EnsureStrictChild(
            normalizedBase,
            root,
            "ByeDpiStagingTransactionOutsideBoundary");
        return root;
    }

    private static string ResolveStateRoot(string managedBase) =>
        WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(managedBase, StateDirectoryName));

    private static string ResolveJournalRoot(string managedBase) =>
        WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(ResolveStateRoot(managedBase), JournalDirectoryName));

    private static string ResolveIncomingManifestRoot(string managedBase) =>
        WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(ResolveStateRoot(managedBase), IncomingManifestDirectoryName));

    private static string ResolveManifestRoot(string managedBase) =>
        WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
            Path.Combine(ResolveStateRoot(managedBase), ManifestDirectoryName));

    private static string ResolveStatePath(string managedBase) =>
        Path.Combine(ResolveStateRoot(managedBase), StateFileName);

    private static bool SameVolume(string left, string right)
    {
        var leftRoot = Path.GetPathRoot(Path.GetFullPath(left));
        var rightRoot = Path.GetPathRoot(Path.GetFullPath(right));
        return !string.IsNullOrWhiteSpace(leftRoot)
            && !string.IsNullOrWhiteSpace(rightRoot)
            && string.Equals(leftRoot, rightRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTransactionId() =>
        Guid.NewGuid().ToString("N");

    private static void ValidateVersion(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 32
            || value.Any(static character =>
                character is not (>= '0' and <= '9') and not '.'))
        {
            throw new WindowsByeDpiPromotionException(code, "ByeDPI version invalid.");
        }
        var parts = value.Split('.');
        if (parts.Length != 3
            || parts.Any(static part =>
                part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || !int.TryParse(
                    part,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out _)))
        {
            throw new WindowsByeDpiPromotionException(code, "ByeDPI version invalid.");
        }
    }

    private static void ValidateOptionalVersion(string? value, string code)
    {
        if (value is not null)
        {
            ValidateVersion(value, code);
        }
    }

    private static void ValidateDigest(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiPromotionException(code, "SHA-256 digest invalid.");
        }
    }

    private static void EnsureNoReparsePointsBetween(string root, string target)
    {
        var normalizedRoot = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(root);
        var normalizedTarget = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(target);
        if (!normalizedTarget.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiPromotionException(
                "ByeDpiManagedPathOutsideProgramFiles",
                "Managed path вышел за Program Files boundary.");
        }

        WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
            normalizedRoot,
            "ByeDpiProgramFilesReparsePoint");
        var current = normalizedRoot;
        foreach (var segment in Path.GetRelativePath(normalizedRoot, normalizedTarget)
                     .Split(Path.DirectorySeparatorChar))
        {
            current = WindowsByeDpiPromotionSourceVerifier.NormalizeDirectory(
                Path.Combine(current, segment));
            if (Directory.Exists(current))
            {
                WindowsByeDpiPromotionSourceVerifier.RejectReparsePoint(
                    current,
                    "ByeDpiManagedParentReparsePoint");
            }
        }
    }

    private sealed record ManagedVerificationEvidence(
        int VerifiedFileCount,
        int VerifiedSecurityObjectCount);

    private sealed record PromotionJournalRecord(
        int SchemaVersion,
        string TransactionId,
        ByeDpiPackageLifecycleOperation Operation,
        string ManifestSha256,
        string TargetVersion,
        string? PreviousCurrentVersion,
        string? PreviousRetainedVersion,
        WindowsByeDpiPromotionPhase Phase,
        DateTimeOffset UpdatedUtc);

    private sealed class PackageStateDto
    {
        public int SchemaVersion { get; set; }
        public string? CurrentVersion { get; set; }
        public string? RetainedPreviousVersion { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    private sealed class PromotionJournalDto
    {
        public int SchemaVersion { get; set; }
        public string? TransactionId { get; set; }
        public string? Operation { get; set; }
        public string? ManifestSha256 { get; set; }
        public string? TargetVersion { get; set; }
        public string? PreviousCurrentVersion { get; set; }
        public string? PreviousRetainedVersion { get; set; }
        public string? Phase { get; set; }
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
