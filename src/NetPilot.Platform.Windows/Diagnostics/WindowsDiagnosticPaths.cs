namespace NetPilot.Platform.Windows.Diagnostics;

public static class WindowsDiagnosticPaths
{
    public static string GetUserRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetPilot");

    public static string GetUserLogDirectory() =>
        Path.Combine(GetUserRoot(), "Logs");

    public static string GetSupportBundleDirectory() =>
        Path.Combine(GetUserRoot(), "Support");

    public static string GetServiceRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NetPilot");

    public static string GetServiceLogDirectory() =>
        Path.Combine(GetServiceRoot(), "Logs");
}
