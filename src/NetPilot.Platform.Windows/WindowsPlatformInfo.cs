using NetPilot.Platform.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsPlatformInfo : IPlatformInfo
{
    public string PlatformId => "windows";
    public string DisplayName => "Windows";
    public bool SupportsPrivilegedService => true;
    public bool SupportsSystemTray => true;
}
