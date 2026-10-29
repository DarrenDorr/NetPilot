using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineBackendCapabilityCatalogTests
{
    [Fact]
    public void Zapret2Windows_DeclaresCanonicalIdentityAndTransparentTcpUdp()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        Assert.Equal(BuiltInEngineBackendKind.Zapret2, declaration.Backend);
        Assert.Equal("zapret2", declaration.EngineId);
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

        Assert.Equal("zapret2", declaration.EngineId);
        Assert.True(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentTcp
            | EngineTrafficCapability.TransparentUdp));
    }

    [Fact]
    public void ByeDpiWindows_DeclaresCanonicalIdentityAndExplicitProxyOnly()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows);

        Assert.Equal("byedpi", declaration.EngineId);
        Assert.True(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.ExplicitProxyTcp
            | EngineTrafficCapability.ExplicitProxyUdp));
        Assert.False(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentTcp));
        Assert.False(declaration.DirectCapabilities.Supports(
            EngineTrafficCapability.TransparentUdp));
    }

    [Fact]
    public void Zapret2Windows_SupportsAllCurrentFullProfiles()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        var ids = BuiltInEngineProfileCapabilityPolicy
            .GetSupportedProfiles(declaration)
            .Select(static profile => profile.Id.Value)
            .ToArray();

        Assert.Equal(
            ["standard", "discord", "youtube", "discord-youtube"],
            ids);
    }

    [Fact]
    public void ByeDpiWindows_SupportsNoCurrentFullProfiles()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows);

        var profiles = BuiltInEngineProfileCapabilityPolicy
            .GetSupportedProfiles(declaration);

        Assert.Empty(profiles);
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
    public void Declaration_HasNoPublicConstructor()
    {
        var publicConstructors = typeof(EngineBackendCapabilityDeclaration)
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public);

        Assert.Empty(publicConstructors);
    }

    [Fact]
    public void Declaration_ExposesOnlyTrustedIdentityPlatformAndCapabilities()
    {
        var propertyNames = typeof(EngineBackendCapabilityDeclaration)
            .GetProperties()
            .Select(static property => property.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Backend", "DirectCapabilities", "EngineId", "Platform"],
            propertyNames);
    }

    [Fact]
    public void SupportedProfiles_ResultIsReadOnly()
    {
        var declaration = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);
        var profiles = BuiltInEngineProfileCapabilityPolicy
            .GetSupportedProfiles(declaration);
        var list = Assert.IsAssignableFrom<IList<EngineProfileDescriptor>>(profiles);

        Assert.Throws<NotSupportedException>(() =>
            list.Add(new EngineProfileDescriptor(
                EngineProfileId.Parse("extra"),
                "Extra")));
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
