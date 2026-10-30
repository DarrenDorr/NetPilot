using System.Globalization;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NetPilot.Updater;

namespace NetPilot.Platform.Windows;

public enum WindowsByeDpiStagingTransactionPhase
{
    Created = 1,
    Extracting = 2,
    Verified = 3
}

public sealed record WindowsByeDpiStagingTransactionResult(
    string TransactionId,
    string StagingRoot,
    string ManifestSha256,
    int VerifiedFileCount,
    int VerifiedSecurityObjectCount);

public sealed record WindowsByeDpiStagingRecoveryReport(
    int RecoveredIncompleteTransactions,
    int PreservedVerifiedTransactions,
    int RemovedOrphanTransactions,
    int RemovedTemporaryJournalFiles);

public sealed class WindowsByeDpiStagingTransactionException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// R42.2 fail-closed transaction boundary for ByeDPI package staging.
/// It extracts only into the fixed Program Files staging boundary, records a small
/// path-free recovery journal, verifies the exact manifest and Windows ACL/owner state,
/// and intentionally does not promote, install, admit or execute the package.
/// </summary>
public static class WindowsByeDpiProtectedStagingTransaction
{
    private const int JournalSchemaVersion = 1;
    private const int TransactionIdLength = 32;
    private const int DigestLength = 64;
    private const int MaxJournalBytes = 8 * 1024;
    private const string JournalDirectoryName = "Journal";

    public static Task<WindowsByeDpiStagingTransactionResult> StageAndVerifyAsync(
        Stream zipStream,
        ByeDpiPackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentNullException.ThrowIfNull(manifest);

        EnsureWindowsAndLocalSystem();
        return StageAndVerifyCoreAsync(
            zipStream,
            manifest,
            ResolveProductionStagingBase(),
            enforceWindowsSecurity: true,
            cancellationToken);
    }

    public static WindowsByeDpiStagingRecoveryReport RecoverIncompleteTransactions()
    {
        EnsureWindowsAndLocalSystem();
        return RecoverCore(
            ResolveProductionStagingBase(),
            enforceWindowsSecurity: true);
    }

    internal static Task<WindowsByeDpiStagingTransactionResult> StageAndVerifyForTestingAsync(
        Stream zipStream,
        ByeDpiPackageManifest manifest,
        string stagingBase,
        CancellationToken cancellationToken = default) =>
        StageAndVerifyCoreAsync(
            zipStream,
            manifest,
            stagingBase,
            enforceWindowsSecurity: false,
            cancellationToken);

    internal static WindowsByeDpiStagingRecoveryReport RecoverForTesting(string stagingBase) =>
        RecoverCore(stagingBase, enforceWindowsSecurity: false);

    private static async Task<WindowsByeDpiStagingTransactionResult> StageAndVerifyCoreAsync(
        Stream zipStream,
        ByeDpiPackageManifest manifest,
        string stagingBase,
        bool enforceWindowsSecurity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zipStream);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);

        var normalizedBase = NormalizeDirectory(stagingBase);
        if (enforceWindowsSecurity)
        {
            EnsureProductionBaseMatches(normalizedBase);
        }

        PrepareStagingBase(normalizedBase, enforceWindowsSecurity);

        var transactionId = CreateTransactionId();
        var transactionRoot = ResolveTransactionRoot(normalizedBase, transactionId);
        var journalRoot = ResolveJournalRoot(normalizedBase);
        var journalPath = ResolveJournalPath(journalRoot, transactionId);

        Exception? failure = null;
        try
        {
            WriteJournalAtomic(
                journalRoot,
                new JournalRecord(
                    JournalSchemaVersion,
                    transactionId,
                    manifest.CanonicalManifestSha256,
                    manifest.Version,
                    WindowsByeDpiStagingTransactionPhase.Created,
                    DateTimeOffset.UtcNow),
                enforceWindowsSecurity);

            CreateTransactionRoot(transactionRoot, enforceWindowsSecurity);

            WriteJournalAtomic(
                journalRoot,
                new JournalRecord(
                    JournalSchemaVersion,
                    transactionId,
                    manifest.CanonicalManifestSha256,
                    manifest.Version,
                    WindowsByeDpiStagingTransactionPhase.Extracting,
                    DateTimeOffset.UtcNow),
                enforceWindowsSecurity);

            await SafeZipExtractor.ExtractAsync(
                zipStream,
                transactionRoot,
                cancellationToken);

            var verifiedTree = await ByeDpiPackageManifestVerifier.VerifyAsync(
                transactionRoot,
                manifest,
                cancellationToken);

            var verifiedSecurityObjectCount = 0;
            if (enforceWindowsSecurity)
            {
                var securityVerification = WindowsByeDpiProtectedStagingSecurityVerifier.Verify(
                    transactionId,
                    manifest.Files.Select(static file => file.RelativePath).ToArray());
                verifiedSecurityObjectCount = securityVerification.VerifiedObjectCount;
            }

            WriteJournalAtomic(
                journalRoot,
                new JournalRecord(
                    JournalSchemaVersion,
                    transactionId,
                    manifest.CanonicalManifestSha256,
                    manifest.Version,
                    WindowsByeDpiStagingTransactionPhase.Verified,
                    DateTimeOffset.UtcNow),
                enforceWindowsSecurity);

            return new WindowsByeDpiStagingTransactionResult(
                transactionId,
                transactionRoot,
                manifest.CanonicalManifestSha256,
                verifiedTree.VerifiedFileCount,
                verifiedSecurityObjectCount);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            if (failure is not null)
            {
                try
                {
                    DeleteTransactionRoot(normalizedBase, transactionId);
                    DeleteJournalFile(journalRoot, journalPath, transactionId);
                }
                catch (Exception cleanupException)
                {
                    throw new WindowsByeDpiStagingTransactionException(
                        "ByeDpiStagingCleanupFailed",
                        "ByeDPI staging transaction завершилась ошибкой, а безопасная очистка также не была подтверждена.",
                        new AggregateException(failure, cleanupException));
                }
            }
        }
    }

    private static WindowsByeDpiStagingRecoveryReport RecoverCore(
        string stagingBase,
        bool enforceWindowsSecurity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingBase);
        var normalizedBase = NormalizeDirectory(stagingBase);

        if (enforceWindowsSecurity)
        {
            EnsureProductionBaseMatches(normalizedBase);
        }

        if (!Directory.Exists(normalizedBase))
        {
            return new WindowsByeDpiStagingRecoveryReport(0, 0, 0, 0);
        }

        PrepareStagingBase(normalizedBase, enforceWindowsSecurity);
        RejectReparsePoint(normalizedBase, "ByeDpiStagingBaseReparsePoint");

        var journalRoot = ResolveJournalRoot(normalizedBase);
        var removedTemps = RemoveSafeJournalTemps(journalRoot);
        var recovered = 0;
        var preserved = 0;
        var orphans = 0;
        var journaledTransactionIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var journalPath in Directory.EnumerateFiles(
                     journalRoot,
                     "*.json",
                     SearchOption.TopDirectoryOnly))
        {
            RejectReparsePoint(journalPath, "ByeDpiJournalReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyProtectedFile(journalPath);
            }

            var fileName = Path.GetFileName(journalPath);
            if (!fileName.EndsWith(".json", StringComparison.Ordinal))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalNameInvalid",
                    "Recovery journal имеет неожиданный filename.");
            }

            var transactionId = fileName[..^5];
            ValidateTransactionId(transactionId);
            var exactJournalPath = ResolveJournalPath(journalRoot, transactionId);
            if (!string.Equals(
                    Path.GetFullPath(journalPath),
                    Path.GetFullPath(exactJournalPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalPathMismatch",
                    "Recovery journal вышел за exact journal boundary.");
            }

            var record = ReadJournalStrict(journalPath);
            if (!string.Equals(record.TransactionId, transactionId, StringComparison.Ordinal))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalTransactionMismatch",
                    "Transaction id в recovery journal не совпадает с filename.");
            }

            if (!journaledTransactionIds.Add(transactionId))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalDuplicateTransaction",
                    "Обнаружено несколько recovery journal для одной транзакции.");
            }

            var transactionRoot = ResolveTransactionRoot(normalizedBase, transactionId);
            if (record.Phase == WindowsByeDpiStagingTransactionPhase.Verified)
            {
                if (!Directory.Exists(transactionRoot))
                {
                    throw new WindowsByeDpiStagingTransactionException(
                        "ByeDpiVerifiedStagingMissing",
                        "Verified recovery journal существует без соответствующего staging tree.");
                }

                RejectReparsePoint(transactionRoot, "ByeDpiVerifiedStagingReparsePoint");
                preserved++;
                continue;
            }

            DeleteTransactionRoot(normalizedBase, transactionId);
            DeleteJournalFile(journalRoot, journalPath, transactionId);
            recovered++;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     normalizedBase,
                     "*",
                     SearchOption.TopDirectoryOnly))
        {
            var fullPath = Path.GetFullPath(entry);
            EnsureStrictChild(normalizedBase, fullPath, "ByeDpiRecoveryEntryOutsideBoundary");
            RejectReparsePoint(fullPath, "ByeDpiRecoveryEntryReparsePoint");

            var name = Path.GetFileName(fullPath);
            if (string.Equals(name, JournalDirectoryName, StringComparison.Ordinal))
            {
                if (!Directory.Exists(fullPath))
                {
                    throw new WindowsByeDpiStagingTransactionException(
                        "ByeDpiJournalRootTypeMismatch",
                        "Journal boundary должен быть каталогом.");
                }

                continue;
            }

            if (!Directory.Exists(fullPath))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiUnexpectedStagingFile",
                    "В корне ByeDPI staging обнаружен неожиданный файл.");
            }

            ValidateTransactionId(name);
            if (journaledTransactionIds.Contains(name))
            {
                continue;
            }

            DeleteTransactionRoot(normalizedBase, name);
            orphans++;
        }

        return new WindowsByeDpiStagingRecoveryReport(
            recovered,
            preserved,
            orphans,
            removedTemps);
    }

    private static string CreateTransactionId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ResolveProductionStagingBase()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ByeDPI protected staging доступен только в Windows.");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ProgramFilesUnavailable",
                "Не удалось определить системный Program Files для ByeDPI staging transaction.");
        }

        return NormalizeDirectory(
            Path.Combine(programFiles, "NetPilot", "Engines", "Staging", "ByeDPI"));
    }

    private static void EnsureProductionBaseMatches(string stagingBase)
    {
        var expected = ResolveProductionStagingBase();
        if (!string.Equals(expected, stagingBase, StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiStagingBaseMismatch",
                "Production staging transaction может использовать только fixed Program Files boundary.");
        }
    }

    private static void PrepareStagingBase(string stagingBase, bool enforceWindowsSecurity)
    {
        if (!enforceWindowsSecurity)
        {
            Directory.CreateDirectory(stagingBase);
            RejectReparsePoint(stagingBase, "ByeDpiStagingBaseReparsePoint");
            var journalRoot = ResolveJournalRoot(stagingBase);
            Directory.CreateDirectory(journalRoot);
            RejectReparsePoint(journalRoot, "ByeDpiJournalRootReparsePoint");
            return;
        }

        EnsureWindowsAndLocalSystem();
        var programFiles = NormalizeDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var enginesRoot = NormalizeDirectory(Path.Combine(programFiles, "NetPilot", "Engines"));
        Directory.CreateDirectory(enginesRoot);
        EnsureNoReparsePointsBetween(programFiles, enginesRoot);

        var stagingRoot = NormalizeDirectory(Path.Combine(enginesRoot, "Staging"));
        EnsureServiceOnlyDirectory(stagingRoot);
        EnsureServiceOnlyDirectory(stagingBase);
        EnsureServiceOnlyDirectory(ResolveJournalRoot(stagingBase));
    }

    private static void CreateTransactionRoot(string transactionRoot, bool enforceWindowsSecurity)
    {
        if (Directory.Exists(transactionRoot) || File.Exists(transactionRoot))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiTransactionAlreadyExists",
                "Сгенерированный ByeDPI transaction id уже существует.");
        }

        if (enforceWindowsSecurity)
        {
            EnsureServiceOnlyDirectory(transactionRoot);
        }
        else
        {
            Directory.CreateDirectory(transactionRoot);
            RejectReparsePoint(transactionRoot, "ByeDpiTransactionRootReparsePoint");
        }
    }

    private static void EnsureServiceOnlyDirectory(string path)
    {
        var fullPath = NormalizeDirectory(path);
        var parent = Path.GetDirectoryName(fullPath)
            ?? throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiProtectedParentMissing",
                "Не удалось определить parent protected staging directory.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(
                $"Parent protected staging directory не существует: {parent}");
        }

        RejectReparsePoint(parent, "ByeDpiProtectedParentReparsePoint");

        if (Directory.Exists(fullPath))
        {
            RejectReparsePoint(fullPath, "ByeDpiProtectedDirectoryReparsePoint");
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

        RejectReparsePoint(fullPath, "ByeDpiProtectedDirectoryReparsePoint");
        VerifyProtectedDirectory(fullPath);
    }

    private static void VerifyProtectedDirectory(string path)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new DirectoryInfo(path),
            AccessControlSections.Access | AccessControlSections.Owner);
        if (!security.AreAccessRulesProtected)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiProtectedDirectoryInheritedAcl",
                "Protected staging directory сохранил наследуемый DACL.");
        }

        WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor(security);
    }

    private static void VerifyProtectedFile(string path)
    {
        var security = FileSystemAclExtensions.GetAccessControl(
            new FileInfo(path),
            AccessControlSections.Access | AccessControlSections.Owner);
        WindowsProtectedEngineSecurityVerifier.EvaluateSecurityDescriptor(security);
    }

    private static void WriteJournalAtomic(
        string journalRoot,
        JournalRecord record,
        bool enforceWindowsSecurity)
    {
        ValidateJournalRecord(record);
        var journalPath = ResolveJournalPath(journalRoot, record.TransactionId);
        var tempPath = Path.Combine(
            journalRoot,
            $"{record.TransactionId}.{Guid.NewGuid():N}.tmp");
        EnsureStrictChild(journalRoot, tempPath, "ByeDpiJournalTempOutsideBoundary");

        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = record.SchemaVersion,
            transactionId = record.TransactionId,
            manifestSha256 = record.ManifestSha256,
            version = record.Version,
            phase = record.Phase.ToString(),
            updatedUtc = record.UpdatedUtc.ToString("O", CultureInfo.InvariantCulture)
        });
        var bytes = Encoding.UTF8.GetBytes(payload + "\n");
        if (bytes.Length > MaxJournalBytes)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalTooLarge",
                "Recovery journal превысил фиксированный размерный лимит.");
        }

        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            RejectReparsePoint(tempPath, "ByeDpiJournalTempReparsePoint");
            File.Move(tempPath, journalPath, overwrite: true);
            RejectReparsePoint(journalPath, "ByeDpiJournalReparsePoint");
            if (enforceWindowsSecurity)
            {
                VerifyProtectedFile(journalPath);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                RejectReparsePoint(tempPath, "ByeDpiJournalTempReparsePoint");
                File.Delete(tempPath);
            }
        }
    }

    private static JournalRecord ReadJournalStrict(string journalPath)
    {
        var info = new FileInfo(journalPath);
        if (info.Length is <= 0 or > MaxJournalBytes)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalSizeInvalid",
                "Recovery journal имеет недопустимый размер.");
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
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalRootInvalid",
                "Recovery journal должен содержать JSON object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalDuplicateProperty",
                    "Recovery journal содержит duplicate JSON property.");
            }
        }

        string[] expectedProperties =
        [
            "schemaVersion",
            "transactionId",
            "manifestSha256",
            "version",
            "phase",
            "updatedUtc"
        ];
        if (properties.Count != expectedProperties.Length
            || expectedProperties.Any(property => !properties.ContainsKey(property)))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalShapeInvalid",
                "Recovery journal содержит неизвестные или отсутствующие поля.");
        }

        if (!properties["schemaVersion"].TryGetInt32(out var schemaVersion)
            || schemaVersion != JournalSchemaVersion)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalSchemaInvalid",
                "Recovery journal использует неизвестную schema version.");
        }

        var transactionId = RequireString(properties["transactionId"], "ByeDpiJournalTransactionInvalid");
        var manifestSha256 = RequireString(properties["manifestSha256"], "ByeDpiJournalDigestInvalid");
        var version = RequireString(properties["version"], "ByeDpiJournalVersionInvalid");
        var phaseText = RequireString(properties["phase"], "ByeDpiJournalPhaseInvalid");
        var updatedText = RequireString(properties["updatedUtc"], "ByeDpiJournalTimestampInvalid");

        if (!Enum.TryParse<WindowsByeDpiStagingTransactionPhase>(phaseText, ignoreCase: false, out var phase)
            || !Enum.IsDefined(phase))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalPhaseInvalid",
                "Recovery journal содержит неизвестную transaction phase.");
        }

        if (!DateTimeOffset.TryParseExact(
                updatedText,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var updatedUtc))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalTimestampInvalid",
                "Recovery journal содержит недопустимый timestamp.");
        }

        var record = new JournalRecord(
            schemaVersion,
            transactionId,
            manifestSha256,
            version,
            phase,
            updatedUtc);
        ValidateJournalRecord(record);
        return record;
    }

    private static string RequireString(JsonElement element, string code)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new WindowsByeDpiStagingTransactionException(
                code,
                "Recovery journal содержит поле с неверным JSON type.");
        }

        var value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new WindowsByeDpiStagingTransactionException(
                code,
                "Recovery journal содержит пустое обязательное поле.");
        }

        return value;
    }

    private static void ValidateJournalRecord(JournalRecord record)
    {
        if (record.SchemaVersion != JournalSchemaVersion)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalSchemaInvalid",
                "Recovery journal schema version не поддерживается.");
        }

        ValidateTransactionId(record.TransactionId);
        ValidateDigest(record.ManifestSha256);
        ValidateVersion(record.Version);
        if (!Enum.IsDefined(record.Phase))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalPhaseInvalid",
                "Recovery journal содержит неизвестную transaction phase.");
        }
    }

    private static void ValidateTransactionId(string transactionId)
    {
        if (string.IsNullOrWhiteSpace(transactionId)
            || transactionId.Length != TransactionIdLength
            || transactionId.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiTransactionIdInvalid",
                "ByeDPI transaction id должен быть 32-character lowercase hex token.");
        }
    }

    private static void ValidateDigest(string digest)
    {
        if (string.IsNullOrWhiteSpace(digest)
            || digest.Length != DigestLength
            || digest.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalDigestInvalid",
                "Recovery journal manifest digest должен быть lowercase SHA-256 hex.");
        }
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 32)
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalVersionInvalid",
                "Recovery journal version имеет недопустимый формат.");
        }

        var parts = version.Split('.');
        if (parts.Length != 3
            || parts.Any(static part =>
                part.Length == 0
                || (part.Length > 1 && part[0] == '0')
                || part.Any(static character => character is < '0' or > '9')))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalVersionInvalid",
                "Recovery journal version должна иметь canonical major.minor.patch format.");
        }
    }

    private static int RemoveSafeJournalTemps(string journalRoot)
    {
        var removed = 0;
        foreach (var tempPath in Directory.EnumerateFiles(
                     journalRoot,
                     "*.tmp",
                     SearchOption.TopDirectoryOnly))
        {
            RejectReparsePoint(tempPath, "ByeDpiJournalTempReparsePoint");
            var name = Path.GetFileName(tempPath);
            var parts = name.Split('.');
            if (parts.Length != 3
                || !string.Equals(parts[2], "tmp", StringComparison.Ordinal)
                || !IsLowerHex(parts[0], TransactionIdLength)
                || !IsLowerHex(parts[1], 32))
            {
                throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiJournalTempNameInvalid",
                    "Journal directory содержит неизвестный temporary file.");
            }

            File.Delete(tempPath);
            removed++;
        }

        return removed;
    }

    private static bool IsLowerHex(string value, int expectedLength) =>
        value.Length == expectedLength
        && value.All(static character =>
            character is >= '0' and <= '9'
                or >= 'a' and <= 'f');

    private static void DeleteJournalFile(
        string journalRoot,
        string journalPath,
        string transactionId)
    {
        ValidateTransactionId(transactionId);
        var exact = ResolveJournalPath(journalRoot, transactionId);
        if (!string.Equals(
                Path.GetFullPath(exact),
                Path.GetFullPath(journalPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new WindowsByeDpiStagingTransactionException(
                "ByeDpiJournalDeletePathMismatch",
                "Recovery отказалась удалять journal вне exact transaction path.");
        }

        if (File.Exists(exact))
        {
            RejectReparsePoint(exact, "ByeDpiJournalReparsePoint");
            File.Delete(exact);
        }
    }

    private static void DeleteTransactionRoot(string stagingBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = ResolveTransactionRoot(stagingBase, transactionId);
        if (!Directory.Exists(root))
        {
            return;
        }

        RejectReparsePoint(root, "ByeDpiTransactionRootReparsePoint");
        var directories = new List<string> { root };
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var fullPath = Path.GetFullPath(entry);
                EnsureStrictChild(root, fullPath, "ByeDpiCleanupEntryOutsideBoundary");
                RejectReparsePoint(fullPath, "ByeDpiCleanupReparsePoint");
                if (Directory.Exists(fullPath))
                {
                    directories.Add(fullPath);
                    stack.Push(fullPath);
                }
                else
                {
                    File.Delete(fullPath);
                }
            }
        }

        foreach (var directory in directories
                     .OrderByDescending(static path => path.Length)
                     .ThenByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            Directory.Delete(directory, recursive: false);
        }
    }

    private static string ResolveTransactionRoot(string stagingBase, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var root = NormalizeDirectory(Path.Combine(stagingBase, transactionId));
        EnsureStrictChild(stagingBase, root, "ByeDpiTransactionRootOutsideBoundary");
        return root;
    }

    private static string ResolveJournalRoot(string stagingBase)
    {
        var root = NormalizeDirectory(Path.Combine(stagingBase, JournalDirectoryName));
        EnsureStrictChild(stagingBase, root, "ByeDpiJournalRootOutsideBoundary");
        return root;
    }

    private static string ResolveJournalPath(string journalRoot, string transactionId)
    {
        ValidateTransactionId(transactionId);
        var path = Path.GetFullPath(Path.Combine(journalRoot, transactionId + ".json"));
        EnsureStrictChild(journalRoot, path, "ByeDpiJournalPathOutsideBoundary");
        return path;
    }

    private static void EnsureWindowsAndLocalSystem()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ByeDPI protected staging transaction доступна только в Windows.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem)
        {
            throw new UnauthorizedAccessException(
                "Создавать или восстанавливать protected ByeDPI staging transaction может только LocalSystem-служба.");
        }
    }

    private static void EnsureNoReparsePointsBetween(string root, string target)
    {
        var normalizedRoot = NormalizeDirectory(root);
        var normalizedTarget = NormalizeDirectory(target);
        EnsureInsideOrEqual(normalizedRoot, normalizedTarget, "ByeDpiProtectedPathOutsideProgramFiles");

        var current = normalizedTarget;
        while (true)
        {
            RejectReparsePoint(current, "ByeDpiProtectedPathReparsePoint");
            if (string.Equals(current, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = Path.GetDirectoryName(current)
                ?? throw new WindowsByeDpiStagingTransactionException(
                    "ByeDpiProtectedPathParentMissing",
                    "Не удалось проверить protected staging parent chain.");
        }
    }

    private static void RejectReparsePoint(string path, string code)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new WindowsByeDpiStagingTransactionException(
                code,
                "Reparse point в ByeDPI staging transaction boundary запрещён.");
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
            throw new WindowsByeDpiStagingTransactionException(
                code,
                "Filesystem path вышел за пределы разрешённой staging boundary.");
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
            throw new WindowsByeDpiStagingTransactionException(
                code,
                "Filesystem path не является strict child разрешённой staging boundary.");
        }
    }

    private sealed record JournalRecord(
        int SchemaVersion,
        string TransactionId,
        string ManifestSha256,
        string Version,
        WindowsByeDpiStagingTransactionPhase Phase,
        DateTimeOffset UpdatedUtc);
}
