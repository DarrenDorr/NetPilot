namespace NetPilot.Updater;

public sealed record ZapretUpdateApplyResult(
    string Version,
    bool PreviousVersionAvailable,
    bool TemporaryCleanupCompleted);

public sealed class ZapretUpdateOrchestrator
{
    private readonly string _engineBaseRoot;
    private readonly string _currentRoot;
    private readonly string _stagingBaseRoot;
    private readonly IZapretReleaseSource _releaseSource;
    private readonly IZapretUpdateRuntimeGate _runtimeGate;
    private readonly ZapretTransactionalInstaller _installer;

    public ZapretUpdateOrchestrator(
        string engineBaseRoot,
        IZapretReleaseSource releaseSource,
        IZapretUpdateRuntimeGate runtimeGate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineBaseRoot);
        ArgumentNullException.ThrowIfNull(releaseSource);
        ArgumentNullException.ThrowIfNull(runtimeGate);

        _engineBaseRoot = Path.GetFullPath(engineBaseRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _currentRoot = Path.Combine(_engineBaseRoot, "current");
        _stagingBaseRoot = Path.Combine(_engineBaseRoot, ".staging");
        _releaseSource = releaseSource;
        _runtimeGate = runtimeGate;
        _installer = new ZapretTransactionalInstaller(_engineBaseRoot);
    }

    public Task<GithubZapretRelease> CheckLatestAsync(
        CancellationToken cancellationToken = default) =>
        _releaseSource.GetLatestReleaseAsync(cancellationToken);

    public async Task<ZapretUpdateApplyResult> ApplyLatestAsync(
        CancellationToken cancellationToken = default)
    {
        var release = await _releaseSource.GetLatestReleaseAsync(cancellationToken);
        await RejectNonUpgradeBeforeDownloadAsync(release, cancellationToken);

        EnsureProtectedStagingBase();
        var transactionId = Guid.NewGuid().ToString("N");
        var downloadRoot = Path.Combine(_stagingBaseRoot, $"txn-{transactionId}-download");
        var extractRoot = Path.Combine(_stagingBaseRoot, $"txn-{transactionId}-extract");

        ZapretInstallResult installResult;
        try
        {
            CreateTransactionRoot(downloadRoot);
            CreateTransactionRoot(extractRoot);

            var downloadedPath = await _releaseSource.DownloadVerifiedAssetAsync(
                release,
                downloadRoot,
                cancellationToken);

            var trustedZipPath = ValidateDownloadedPath(downloadRoot, downloadedPath);
            await VerifyDownloadedAssetAgainAsync(
                trustedZipPath,
                release.Asset,
                cancellationToken);

            await using (var zipStream = new FileStream(
                trustedZipPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await SafeZipExtractor.ExtractAsync(
                    zipStream,
                    extractRoot,
                    cancellationToken);
            }

            // Validate the complete extracted package before interrupting active connectivity.
            ZapretPackageLayoutValidator.ResolvePackageRoot(extractRoot);

            await _runtimeGate.EnsureStoppedForUpdateAsync(cancellationToken);

            installResult = await _installer.ApplyAsync(
                release,
                extractRoot,
                cancellationToken);
        }
        catch
        {
            TryCleanupTransactionRoot(downloadRoot);
            TryCleanupTransactionRoot(extractRoot);
            throw;
        }

        var cleanupCompleted = TryCleanupTransactionRoot(downloadRoot)
            & TryCleanupTransactionRoot(extractRoot);

        return new ZapretUpdateApplyResult(
            installResult.Version,
            installResult.PreviousVersionAvailable,
            cleanupCompleted);
    }

    private async Task RejectNonUpgradeBeforeDownloadAsync(
        GithubZapretRelease release,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_currentRoot))
        {
            return;
        }

        var resolvedCurrent = ZapretPackageLayoutValidator.ResolvePackageRoot(_currentRoot);
        if (!string.Equals(
                Path.GetFullPath(resolvedCurrent),
                Path.GetFullPath(_currentRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "UnexpectedInstalledLayout",
                "Установленный current Zapret имеет неожиданную структуру.");
        }

        var installed = await EngineInstallationManifest.ReadAsync(
            _currentRoot,
            cancellationToken);

        var installedVersion = ParseVersion(installed.Version, "InstalledVersionUnknown");
        var targetVersion = ParseVersion(release.Version, "InvalidTargetVersion");

        if (targetVersion <= installedVersion)
        {
            throw new UpdateSecurityException(
                targetVersion == installedVersion ? "VersionAlreadyInstalled" : "DowngradeRejected",
                targetVersion == installedVersion
                    ? "Эта версия Zapret уже установлена."
                    : "Автоматический downgrade Zapret запрещён.");
        }
    }

    private void EnsureProtectedStagingBase()
    {
        Directory.CreateDirectory(_engineBaseRoot);
        RejectReparsePoint(_engineBaseRoot);

        Directory.CreateDirectory(_stagingBaseRoot);
        RejectReparsePoint(_stagingBaseRoot);
    }

    private void CreateTransactionRoot(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureDirectChildOfStaging(fullPath);
        Directory.CreateDirectory(fullPath);
        RejectReparsePoint(fullPath);
    }

    private string ValidateDownloadedPath(string downloadRoot, string downloadedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadedPath);

        var root = Path.GetFullPath(downloadRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        EnsureDirectChildOfStaging(root);

        var expected = Path.Combine(root, "source.zip");
        var actual = Path.GetFullPath(downloadedPath);

        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(actual))
        {
            throw new UpdateSecurityException(
                "UntrustedDownloadedAssetPath",
                "Загрузчик вернул asset вне созданного NetPilot download staging либо под неожиданным именем.");
        }

        RejectReparsePoint(actual);
        return actual;
    }

    private static async Task VerifyDownloadedAssetAgainAsync(
        string zipPath,
        ReleaseAsset asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(zipPath);
        if (!info.Exists || info.Length != asset.SizeBytes)
        {
            throw new UpdateSecurityException(
                "ReleaseAssetSizeMismatch",
                "Локальный ZIP не совпадает по размеру с проверенной metadata релиза.");
        }

        await using var stream = new FileStream(
            zipPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await Sha256Verifier.VerifyAsync(
            stream,
            asset.Sha256Hex,
            cancellationToken);
    }

    private bool TryCleanupTransactionRoot(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            EnsureDirectChildOfStaging(fullPath);

            if (!Directory.Exists(fullPath))
            {
                return true;
            }

            ValidateTreeNoReparse(fullPath);
            Directory.Delete(fullPath, recursive: true);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or UpdateSecurityException)
        {
            // Never follow or force-delete a suspicious tree. A later repair/cleanup layer can
            // report and remove a trusted leftover after re-validation.
            return false;
        }
    }

    private void EnsureDirectChildOfStaging(string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (parent is null
            || !string.Equals(
                Path.GetFullPath(parent)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                _stagingBaseRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "TransactionPathEscape",
                "Временный каталог обновления вышел за защищённый .staging NetPilot.");
        }
    }

    private static void ValidateTreeNoReparse(string root)
    {
        RejectReparsePoint(root);

        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new UpdateSecurityException(
                        "ReparsePointRejected",
                        "Reparse point во временном каталоге обновления Zapret запрещён.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ReparsePointRejected",
                "Reparse point во временном каталоге обновления Zapret запрещён.");
        }
    }

    private static Version ParseVersion(string value, string code)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 64
            || value.Any(static character => !(char.IsAsciiDigit(character) || character == '.'))
            || !Version.TryParse(value, out var version)
            || version.Major < 0
            || version.Minor < 0)
        {
            throw new UpdateSecurityException(code, "Версия Zapret имеет неподдерживаемый формат.");
        }

        return version;
    }
}
