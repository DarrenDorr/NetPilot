namespace NetPilot.Platform.Windows;

public static class WindowsZapretEngineLocation
{
    public static string GetDefaultEngineRoot()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
        {
            throw new InvalidOperationException("Не удалось определить системный каталог Program Files.");
        }

        return Path.Combine(
            Path.GetFullPath(programFiles),
            "NetPilot",
            "Engines",
            "Zapret",
            "current");
    }
}
