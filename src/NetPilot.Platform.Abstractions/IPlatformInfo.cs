namespace NetPilot.Platform.Abstractions;

public interface IPlatformInfo
{
    string PlatformId { get; }
    string DisplayName { get; }
    bool SupportsPrivilegedService { get; }
    bool SupportsSystemTray { get; }
}
