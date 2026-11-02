using System.Globalization;
using NetPilot.Automation;

namespace NetPilot.Platform.Windows;

public static class ByeDpiUserModeLaunchPolicy
{
    private sealed record Profile(
        StrategyDescriptor Descriptor,
        IReadOnlyList<string> Arguments);

    private static readonly Profile[] Profiles =
    [
        new(
            new StrategyDescriptor(
                "byedpi-windows-recommended",
                "ByeDPI · Windows recommended"),
            ["--disorder", "1", "--fake", "-1"]),
        new(
            new StrategyDescriptor(
                "byedpi-auto-tlsrec",
                "ByeDPI · auto + TLS record"),
            ["--disorder", "1", "--auto=torst", "--tlsrec", "1+s"])
    ];

    public static IReadOnlyList<StrategyDescriptor> Strategies { get; } =
        Profiles.Select(static profile => profile.Descriptor).ToArray();

    public static IReadOnlyList<string> BuildArguments(
        string strategyId,
        int proxyPort,
        int maxConnections)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        if (proxyPort is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(proxyPort));
        }

        if (maxConnections is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnections));
        }

        var profile = Profiles.FirstOrDefault(profile =>
            string.Equals(profile.Descriptor.Id, strategyId, StringComparison.Ordinal));
        if (profile is null)
        {
            throw new StrategyRuntimeException(
                "ByeDpiUnknownStrategy",
                "NetPilot отклонил неизвестную стратегию ByeDPI.");
        }

        var arguments = new List<string>(10 + profile.Arguments.Count)
        {
            "-i",
            "127.0.0.1",
            "-p",
            proxyPort.ToString(CultureInfo.InvariantCulture),
            "-U",
            "-c",
            maxConnections.ToString(CultureInfo.InvariantCulture),
            "-x",
            "0"
        };
        arguments.AddRange(profile.Arguments);
        return arguments;
    }
}
