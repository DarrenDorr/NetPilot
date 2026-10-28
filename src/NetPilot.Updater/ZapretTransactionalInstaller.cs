namespace NetPilot.Updater;

public sealed record ZapretInstallResult(
    string Version,
    bool PreviousVersionAvailable);

public sealed class ZapretTransactionalInstaller
{
    private readonly string _baseRoot;
    private readonly string _currentRoot;
    private readonly string _previousRoot;
    private readonly string _rollbackPendingRoot;

    public ZapretTransactionalInstaller(string engineBaseRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineBaseRoot);

        _baseRoot = Path.GetFullPath(engineBaseRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _currentRoot = Path.Combine(_baseRoot, "current");
        _previousRoot = Path.Combine(_baseRoot, "previous");
        _rollbackPendingRoot = Path.Combine(_baseRoot, ".rollback-pending");
    }

    public async Task<ZapretInstallResult> ApplyAsync(
        GithubZapretRelease release,
        string extractedStagingRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractedStagingRoot);

        EnsureDeploymentRoot();
        RecoverPendingTransaction();

        var packageRoot = ZapretPackageLayoutValidator.ResolvePackageRoot(extractedStagingRoot);
        EnsureSameVolume(packageRoot, _baseRoot);
        EnsurePackageIsOutsideActiveSlots(packageRoot);

        var targetVersion = ParseVersion(release.Version, "InvalidTargetVersion");
        var currentExists = Directory.Exists(_currentRoot);

        if (currentExists)
        {
            await ValidateInstalledTreeAsync(_currentRoot, cancellationToken);
            var installed = await EngineInstallationManifest.ReadAsync(_currentRoot, cancellationToken);
            var installedVersion = ParseVersion(installed.Version, "InstalledVersionUnknown");

            if (targetVersion <= installedVersion)
            {
                throw new UpdateSecurityException(
                    targetVersion == installedVersion ? "VersionAlreadyInstalled" : "DowngradeRejected",
                    targetVersion == installedVersion
                        ? "Эта версия Zapret уже установлена."
                        : "Автоматический downgrade Zapret запрещён.");
            }
        }

        await EngineInstallationManifest.WriteNewAsync(
            packageRoot,
            EngineInstallationManifest.CreateZapret(release),
            cancellationToken);

        var movedPreviousToPending = false;
        var movedNewToCurrent = false;

        try
        {
            if (currentExists)
            {
                if (Directory.Exists(_rollbackPendingRoot))
                {
                    throw new UpdateSecurityException(
                        "RollbackPendingExists",
                        "Нельзя начать обновление при незавершённом rollback состоянии.");
                }

                Directory.Move(_currentRoot, _rollbackPendingRoot);
                movedPreviousToPending = true;
            }

            Directory.Move(packageRoot, _currentRoot);
            movedNewToCurrent = true;

            await ValidateInstalledTreeAsync(_currentRoot, cancellationToken);
            var installedManifest = await EngineInstallationManifest.ReadAsync(
                _currentRoot,
                cancellationToken);

            if (!string.Equals(installedManifest.Version, release.Version, StringComparison.Ordinal)
                || !string.Equals(installedManifest.Sha256Hex, release.Asset.Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateSecurityException(
                    "InstalledManifestMismatch",
                    "Manifest новой установленной версии не совпадает с проверенным release asset.");
            }

            if (movedPreviousToPending)
            {
                ReplacePreviousSlotFromPending();
            }

            return new ZapretInstallResult(
                release.Version,
                Directory.Exists(_previousRoot));
        }
        catch
        {
            TryRestoreAfterFailedApply(movedPreviousToPending, movedNewToCurrent);
            throw;
        }
    }

    public async Task<ZapretInstallResult> RollbackAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureDeploymentRoot();
        RecoverPendingTransaction();

        if (!Directory.Exists(_currentRoot) || !Directory.Exists(_previousRoot))
        {
            throw new UpdateSecurityException(
                "RollbackUnavailable",
                "Предыдущая версия Zapret для отката отсутствует.");
        }

        await ValidateInstalledTreeAsync(_currentRoot, cancellationToken);
        await ValidateInstalledTreeAsync(_previousRoot, cancellationToken);
        var previousManifest = await EngineInstallationManifest.ReadAsync(
            _previousRoot,
            cancellationToken);

        Directory.Move(_currentRoot, _rollbackPendingRoot);
        var previousMovedToCurrent = false;

        try
        {
            Directory.Move(_previousRoot, _currentRoot);
            previousMovedToCurrent = true;

            await ValidateInstalledTreeAsync(_currentRoot, cancellationToken);
            await EngineInstallationManifest.ReadAsync(_currentRoot, cancellationToken);

            Directory.Move(_rollbackPendingRoot, _previousRoot);

            return new ZapretInstallResult(
                previousManifest.Version,
                PreviousVersionAvailable: true);
        }
        catch
        {
            if (previousMovedToCurrent && Directory.Exists(_currentRoot))
            {
                SafeDeleteTree(_currentRoot);
            }

            if (Directory.Exists(_rollbackPendingRoot) && !Directory.Exists(_currentRoot))
            {
                Directory.Move(_rollbackPendingRoot, _currentRoot);
            }

            throw;
        }
    }

    public void RecoverPendingTransaction()
    {
        if (!Directory.Exists(_rollbackPendingRoot))
        {
            return;
        }

        ValidateTreeNoReparse(_rollbackPendingRoot);

        if (!Directory.Exists(_currentRoot))
        {
            Directory.Move(_rollbackPendingRoot, _currentRoot);
            return;
        }

        try
        {
            ValidateInstalledTree(_currentRoot);
            ReadManifestSynchronously(_currentRoot);
            ReplacePreviousSlotFromPending();
        }
        catch (UpdateSecurityException)
        {
            SafeDeleteTree(_currentRoot);
            Directory.Move(_rollbackPendingRoot, _currentRoot);
        }
    }

    private void EnsureDeploymentRoot()
    {
        Directory.CreateDirectory(_baseRoot);
        RejectReparsePoint(_baseRoot);
    }

    private void ReplacePreviousSlotFromPending()
    {
        if (!Directory.Exists(_rollbackPendingRoot))
        {
            return;
        }

        ValidateTreeNoReparse(_rollbackPendingRoot);

        if (Directory.Exists(_previousRoot))
        {
            SafeDeleteTree(_previousRoot);
        }

        Directory.Move(_rollbackPendingRoot, _previousRoot);
    }

    private void TryRestoreAfterFailedApply(bool previousMovedToPending, bool newMovedToCurrent)
    {
        try
        {
            if (newMovedToCurrent && Directory.Exists(_currentRoot))
            {
                SafeDeleteTree(_currentRoot);
            }

            if (previousMovedToPending
                && Directory.Exists(_rollbackPendingRoot)
                && !Directory.Exists(_currentRoot))
            {
                Directory.Move(_rollbackPendingRoot, _currentRoot);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UpdateSecurityException)
        {
            throw new UpdateSecurityException(
                "RollbackRestoreFailed",
                "Обновление не удалось, и автоматическое восстановление предыдущего current также завершилось ошибкой.",
                exception);
        }
    }

    private async Task ValidateInstalledTreeAsync(
        string root,
        CancellationToken cancellationToken)
    {
        ValidateInstalledTree(root);
        await EngineInstallationManifest.ReadAsync(root, cancellationToken);
    }

    private static void ValidateInstalledTree(string root)
    {
        var resolved = ZapretPackageLayoutValidator.ResolvePackageRoot(root);
        if (!string.Equals(
                Path.GetFullPath(resolved),
                Path.GetFullPath(root),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "UnexpectedInstalledLayout",
                "Установленный движок должен находиться непосредственно в current/previous без дополнительной обёртки.");
        }
    }

    private static EngineInstallationManifest ReadManifestSynchronously(string root) =>
        EngineInstallationManifest.ReadAsync(root).GetAwaiter().GetResult();

    private void SafeDeleteTree(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureChildOfBase(fullPath);
        ValidateTreeNoReparse(fullPath);
        Directory.Delete(fullPath, recursive: true);
    }

    private static void ValidateTreeNoReparse(string root)
    {
        RejectReparsePoint(root);

        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            RejectReparsePoint(entry);
        }
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ReparsePointRejected",
                "Reparse point в каталоге установки Zapret запрещён.");
        }
    }

    private void EnsureChildOfBase(string path)
    {
        var baseWithSeparator = _baseRoot + Path.DirectorySeparatorChar;
        if (!path.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "DeploymentPathEscape",
                "Операция с каталогом вышла за защищённый корень движка Zapret.");
        }
    }

    private void EnsurePackageIsOutsideActiveSlots(string packageRoot)
    {
        var full = Path.GetFullPath(packageRoot);
        if (IsSameOrChild(full, _currentRoot)
            || IsSameOrChild(full, _previousRoot)
            || IsSameOrChild(full, _rollbackPendingRoot))
        {
            throw new UpdateSecurityException(
                "InvalidDeploymentStaging",
                "Staging-пакет не может находиться внутри active/previous/rollback slot.");
        }
    }

    private static bool IsSameOrChild(string path, string root)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSameVolume(string first, string second)
    {
        var firstRoot = Path.GetPathRoot(Path.GetFullPath(first));
        var secondRoot = Path.GetPathRoot(Path.GetFullPath(second));

        if (!string.Equals(firstRoot, secondRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "StagingVolumeMismatch",
                "Транзакционное переключение требует staging на том же томе, что и установленный движок.");
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
