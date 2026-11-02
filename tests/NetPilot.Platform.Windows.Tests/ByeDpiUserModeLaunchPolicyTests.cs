using NetPilot.Automation;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class ByeDpiUserModeLaunchPolicyTests
{
    [Theory]
    [InlineData("byedpi-windows-recommended")]
    [InlineData("byedpi-auto-tlsrec")]
    public void BuildArguments_AlwaysUsesLoopbackSocksWithoutPrivilegedModes(string strategyId)
    {
        var arguments = ByeDpiUserModeLaunchPolicy.BuildArguments(strategyId, 32123, 64).ToArray();

        Assert.Equal("127.0.0.1", arguments[Array.IndexOf(arguments, "-i") + 1]);
        Assert.Equal("32123", arguments[Array.IndexOf(arguments, "-p") + 1]);
        Assert.Contains("-U", arguments);
        Assert.DoesNotContain("-N", arguments);
        Assert.DoesNotContain("-E", arguments);
        Assert.DoesNotContain("--transparent", arguments);
        Assert.DoesNotContain("-D", arguments);
        Assert.DoesNotContain("--daemon", arguments);
        Assert.DoesNotContain("runas", arguments, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("service", arguments, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildArguments_RejectsUnknownStrategyFailClosed()
    {
        var exception = Assert.Throws<StrategyRuntimeException>(() =>
            ByeDpiUserModeLaunchPolicy.BuildArguments("unknown", 32123, 64));

        Assert.Equal("ByeDpiUnknownStrategy", exception.Code);
    }

    [Fact]
    public void ReviewedPackageIdentity_RemainsPinned()
    {
        Assert.Equal("0.17.3", ByeDpiUserModeOptions.ReviewedVersion);
        Assert.Equal(495685, ByeDpiUserModeOptions.ReviewedSize);
        Assert.Equal(
            "866e99c3c7690326cc6e365fe02980f06ae2b394c045555674fb95db52b0dbdd",
            ByeDpiUserModeOptions.ReviewedSha256);
    }
}
