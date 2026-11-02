namespace NetPilot.Platform.Windows;

public sealed record ByeDpiUserModeOptions
{
    public const string ReviewedVersion = "0.17.3";
    public const string ReviewedSha256 = "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd";
    public const long ReviewedSize = 495685;

    public required string PackageRoot { get; init; }
    public required string ExecutablePath { get; init; }
    public string ExpectedSha256 { get; init; } = ReviewedSha256;
    public long ExpectedSize { get; init; } = ReviewedSize;
    public int MaxConnections { get; init; } = 64;
    public TimeSpan ListenerStartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    public static ByeDpiUserModeOptions CreateInstalledDefault()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var installRoot = Directory.GetParent(appDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))?.FullName
            ?? appDirectory;
        var packageRoot = Path.Combine(
            installRoot,
            "Engines",
            "Managed",
            "byedpi",
            ReviewedVersion,
            "x64");

        return new ByeDpiUserModeOptions
        {
            PackageRoot = packageRoot,
            ExecutablePath = Path.Combine(packageRoot, "bin", "ciadpi.exe")
        };
    }

    public void Validate()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ByeDPI user-mode runtime is Windows-only.");
        }

        if (string.IsNullOrWhiteSpace(PackageRoot) || !Path.IsPathFullyQualified(PackageRoot))
        {
            throw new ArgumentException("ByeDPI package root must be an absolute path.", nameof(PackageRoot));
        }

        if (string.IsNullOrWhiteSpace(ExecutablePath) || !Path.IsPathFullyQualified(ExecutablePath))
        {
            throw new ArgumentException("ByeDPI executable path must be an absolute path.", nameof(ExecutablePath));
        }

        if (ExpectedSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpectedSize));
        }

        if (ExpectedSha256.Length != 64 || ExpectedSha256.Any(static ch => !Uri.IsHexDigit(ch)))
        {
            throw new ArgumentException("ByeDPI SHA-256 must contain exactly 64 hexadecimal characters.", nameof(ExpectedSha256));
        }

        if (MaxConnections is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConnections));
        }

        if (ListenerStartupTimeout < TimeSpan.FromSeconds(1)
            || ListenerStartupTimeout > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(ListenerStartupTimeout));
        }

        var root = Path.GetFullPath(PackageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var executable = Path.GetFullPath(ExecutablePath);
        if (!executable.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("ByeDPI executable must stay inside the configured package root.");
        }

        if (!string.Equals(Path.GetFileName(executable), "ciadpi.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("ByeDPI executable must be named ciadpi.exe.");
        }
    }
}
