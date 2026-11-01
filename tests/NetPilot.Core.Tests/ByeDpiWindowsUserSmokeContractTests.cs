using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiWindowsUserSmokeContractTests
{
    [Fact]
    public void Contract_is_isolated_from_production_catalogs_and_uses_exact_fixed_profile()
    {
        var contract = ByeDpiWindowsUserSmokeContract.Create(39036);

        Assert.Equal(
            EngineLabProfileCatalog.ByeDpiWindowsUserSmokeV1Id,
            contract.LaunchIdentityBinding.ProfileId);
        Assert.True(EngineLabProfileCatalog.IsTrustedLabProfile(
            contract.LaunchIdentityBinding.ProfileId));
        Assert.Empty(BuiltInEngineArgumentPlanCatalog.All);
        Assert.Empty(BuiltInEngineExecutableIdentityCatalog.All);
        Assert.Empty(BuiltInEnginePackageLayoutCatalog.All);

        var tokens = contract.ArgumentPlan.Arguments;
        Assert.Equal(8, tokens.Count);

        AssertToken(tokens[0], EngineArgumentTokenKind.ShortLoopbackIpv4, "-i", 0);
        AssertToken(tokens[1], EngineArgumentTokenKind.ShortFixedPositiveUInt, "-p", 39036);
        AssertToken(tokens[2], EngineArgumentTokenKind.ShortFlag, "-N", 0);
        AssertToken(tokens[3], EngineArgumentTokenKind.ShortFlag, "-U", 0);
        AssertToken(tokens[4], EngineArgumentTokenKind.ShortFixedPositiveUInt, "-c", 8);
        AssertToken(tokens[5], EngineArgumentTokenKind.ShortFixedNonNegativeUInt, "-x", 0);
        AssertToken(tokens[6], EngineArgumentTokenKind.ShortFixedSniRelativePosition, "-s", 1);
        AssertToken(tokens[7], EngineArgumentTokenKind.ShortFixedSniRelativePosition, "-d", 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(443)]
    [InlineData(1023)]
    public void Contract_rejects_privileged_proxy_ports(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ByeDpiWindowsUserSmokeContract.Create((ushort)port));
    }

    private static void AssertToken(
        EngineArgumentTokenTemplate token,
        EngineArgumentTokenKind kind,
        string option,
        ulong value)
    {
        Assert.Equal(kind, token.Kind);
        Assert.Equal(option, token.OptionName);
        Assert.Equal(value, token.FixedPositiveUInt);
        Assert.Null(token.ComponentId);
    }
}
