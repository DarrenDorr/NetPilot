namespace NetPilot.Platform.Windows;

public static class WindowsZapretEngineLocation
{
    public static string GetDefaultInstallationRoot()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new InvalidOperationException("Не удалось определить системный каталог Program Files.");
        }

        return Path.Combine(Path.GetFullPath(programFiles), "NetPilot");
    }

    public static string GetDefaultEngineBaseRoot() =>
        Path.Combine(GetDefaultInstallationRoot(), "Engines", "Zapret");

    public static string GetDefaultEngineRoot() =>
        Path.Combine(GetDefaultEngineBaseRoot(), "current");

    public static string GetDefaultPreviousRoot() =>
        Path.Combine(GetDefaultEngineBaseRoot(), "previous");

    public static string GetDefaultStagingBaseRoot() =>
        Path.Combine(GetDefaultEngineBaseRoot(), ".staging");
}
