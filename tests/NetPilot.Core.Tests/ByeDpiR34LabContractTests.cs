using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiR34LabContractTests
{
    [Fact]
    public void R34_lab_profile_is_not_a_user_facing_builtin_profile()
    {
        var profileId = EngineLabProfileCatalog.ByeDpiLoopbackR34Id;

        Assert.False(BuiltInEngineProfileCatalog.IsBuiltIn(profileId));
        Assert.DoesNotContain(
            BuiltInEngineProfileCatalog.All,
            descriptor => descriptor.Id == profileId);
        Assert.True(EngineLabProfileCatalog.IsTrustedLabProfile(profileId));
        Assert.Equal(
            EngineTrafficCapability.ExplicitProxyTcp,
            BuiltInEngineProfileCapabilityPolicy.GetRequiredCapabilities(profileId));
    }

    [Fact]
    public void R34_contract_keeps_all_production_promotion_catalogs_empty()
    {
        _ = ByeDpiR34LabContract.Create(19080);

        Assert.Empty(BuiltInEngineExecutableIdentityCatalog.All);
        Assert.Empty(BuiltInEnginePackageLayoutCatalog.All);
        Assert.Empty(BuiltInEngineResourceBudgetCatalog.All);
        Assert.Empty(BuiltInEngineRuntimePrerequisiteCatalog.All);
        Assert.Empty(BuiltInEngineLaunchPlanMetadataCatalog.All);
        Assert.Empty(BuiltInEngineLaunchIdentityBindingCatalog.All);
        Assert.Empty(BuiltInEngineArgumentPlanCatalog.All);
    }

    [Fact]
    public void R34_contract_is_exact_windows_x64_byedpi_loopback_shape()
    {
        const ushort port = 19080;
        var contract = ByeDpiR34LabContract.Create(port);
        var binding = contract.LaunchIdentityBinding;

        Assert.Equal(BuiltInEngineBackendKind.ByeDpi, binding.Backend);
        Assert.Equal(EngineHostPlatform.Windows, binding.Platform);
        Assert.Equal(EngineBinaryArchitecture.X64, binding.Architecture);
        Assert.Equal(EngineLabProfileCatalog.ByeDpiLoopbackR34Id, binding.ProfileId);
        Assert.Equal("byedpi", binding.EngineId);

        var identity = binding.ExecutableIdentity;
        Assert.Equal(ByeDpiR34LabContract.PackageVersion, identity.Version);
        Assert.Equal(ByeDpiR34LabContract.CandidateSha256, identity.PackageSha256);
        var primary = Assert.Single(identity.Components);
        Assert.Equal("ciadpi", primary.ComponentId);
        Assert.Equal(EngineExecutableComponentKind.PrimaryExecutable, primary.Kind);
        Assert.Equal(ByeDpiR34LabContract.CandidateSha256, primary.Sha256);

        var layout = Assert.Single(contract.PackageLayout.Components);
        Assert.Equal("ciadpi", layout.ComponentId);
        Assert.Equal("bin/ciadpi.exe", layout.RelativePath);

        Assert.Collection(
            contract.ArgumentPlan.Arguments,
            token => AssertToken(token, EngineArgumentTokenKind.ShortLoopbackIpv4, "-i", 0),
            token => AssertToken(token, EngineArgumentTokenKind.ShortFixedPositiveUInt, "-p", port),
            token => AssertToken(token, EngineArgumentTokenKind.ShortFlag, "-N", 0),
            token => AssertToken(token, EngineArgumentTokenKind.ShortFlag, "-U", 0),
            token => AssertToken(token, EngineArgumentTokenKind.ShortFixedPositiveUInt, "-c", 8),
            token => AssertToken(token, EngineArgumentTokenKind.ShortFixedNonNegativeUInt, "-x", 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(80)]
    [InlineData(1023)]
    public void R34_contract_rejects_privileged_ports(ushort port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ByeDpiR34LabContract.Create(port));
    }

    private static void AssertToken(
        EngineArgumentTokenTemplate token,
        EngineArgumentTokenKind kind,
        string optionName,
        ulong value)
    {
        Assert.Equal(kind, token.Kind);
        Assert.Equal(optionName, token.OptionName);
        Assert.Equal(value, token.FixedPositiveUInt);
        Assert.Null(token.ComponentId);
    }
}
