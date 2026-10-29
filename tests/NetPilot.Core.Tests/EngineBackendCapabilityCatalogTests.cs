using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineBackendCapabilityCatalogTests
{
    [Fact]
    public void Zapret2Windows_DeclaresTransparentTcpAndUdp()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        Assert.Equal(BuiltInEngineBackendKind.Zapret2, declaration.Backend);
        Assert.Equal(EngineHostPlatform.Windows, declaration.Platform);
        Assert.True(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentTcp
            | EngineTrafficCapability.TransparentUdp));
    }

    [Fact]
    public void Zapret2Linux_DeclaresTransparentTcpAndUdp()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Linux);

        Assert.True(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentTcp
            | EngineTrafficCapability.TransparentUdp));
    }

    [Fact]
    public void ByeDpiWindows_DeclaresExplicitProxyOnly()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows);

        Assert.True(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.ExplicitProxyTcp
            | EngineTrafficCapability.ExplicitProxyUdp));
        Assert.False(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentTcp));
        Assert.False(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentUdp));
    }

    [Fact]
    public void ByeDpiWindows_DoesNotSatisfyFullBuiltInProfiles()
    {
        var capabilities = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows).DirectCapabilities;

        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.StandardId,
            capabilities));
        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.YouTubeId,
            capabilities));
        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.DiscordId,
            capabilities));
        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.DiscordYouTubeId,
            capabilities));
    }

    [Theory]
    [InlineData(EngineHostPlatform.Linux)]
    [InlineData(EngineHostPlatform.Android)]
    public void UnreviewedByeDpiPlatform_IsFailClosed(EngineHostPlatform platform)
    {
        Assert.False(BuiltInEngineBackendCapabilityCatalog.TryGet(
            BuiltInEngineBackendKind.ByeDpi,
            platform,
            out var declaration));
        Assert.Null(declaration);

        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineBackendCapabilityCatalog.Get(
                BuiltInEngineBackendKind.ByeDpi,
                platform));
    }

    [Fact]
    public void Declaration_ExposesOnlyIdentityPlatformAndCapabilities()
    {
        var propertyNames = typeof(EngineBackendCapabilityDeclaration)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Backend", "DirectCapabilities", "Platform"],
            propertyNames);
    }

    [Fact]
    public void Catalog_HasUniqueBackendPlatformPairs()
    {
        var declarations = BuiltInEngineBackendCapabilityCatalog.All;
        var uniquePairs = declarations
            .Select(static declaration => (declaration.Backend, declaration.Platform))
            .Distinct()
            .Count();

        Assert.Equal(declarations.Count, uniquePairs);
    }
}
