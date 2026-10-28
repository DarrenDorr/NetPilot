namespace NetPilot.Updater;

public sealed record ZapretInstallationState(
    bool IsInstalled,
    string? Version)
{
    public static ZapretInstallationState NotInstalled { get; } = new(false, null);

    public static ZapretInstallationState Installed(string version) =>
        new(true, version);
}

public sealed class ZapretInstallationInspector
{
    private readonly string _currentRoot;

    public ZapretInstallationInspector(string engineBaseRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(engineBaseRoot);
        _currentRoot = Path.Combine(Path.GetFullPath(engineBaseRoot), "current");
    }

    public async Task<ZapretInstallationState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_currentRoot))
        {
            return ZapretInstallationState.NotInstalled;
        }

        RejectReparsePoint(_currentRoot);

        var resolvedRoot = ZapretPackageLayoutValidator.ResolvePackageRoot(_currentRoot);
        if (!string.Equals(
                Path.GetFullPath(resolvedRoot),
                Path.GetFullPath(_currentRoot),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new UpdateSecurityException(
                "UnexpectedInstalledLayout",
                "Установленный current Zapret имеет неожиданную структуру.");
        }

        var manifest = await EngineInstallationManifest.ReadAsync(
            _currentRoot,
            cancellationToken);

        if (!Version.TryParse(manifest.Version, out var parsed)
            || parsed.Major < 0
            || parsed.Minor < 0
            || manifest.Version.Any(static character =>
                !(char.IsAsciiDigit(character) || character == '.')))
        {
            throw new UpdateSecurityException(
                "InstalledVersionUnknown",
                "Версия установленного Zapret имеет неподдерживаемый формат.");
        }

        return ZapretInstallationState.Installed(manifest.Version);
    }

    private static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UpdateSecurityException(
                "ReparsePointRejected",
                "Каталог установленного Zapret не может быть reparse point.");
        }
    }
}
