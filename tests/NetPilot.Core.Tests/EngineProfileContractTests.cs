using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineProfileContractTests
{
    [Theory]
    [InlineData("standard")]
    [InlineData("discord")]
    [InlineData("youtube")]
    [InlineData("discord-youtube")]
    [InlineData("auto-balanced-2")]
    public void EngineProfileId_AcceptsCanonicalIds(string value)
    {
        var id = EngineProfileId.Parse(value);

        Assert.Equal(value, id.Value);
        Assert.False(id.IsEmpty);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Discord")]
    [InlineData("-discord")]
    [InlineData("discord-")]
    [InlineData("discord--youtube")]
    [InlineData("discord youtube")]
    [InlineData("discord/youtube")]
    [InlineData("../profile")]
    [InlineData("profile.lua")]
    [InlineData("--help")]
    [InlineData("luaexec:code")]
    [InlineData("x=1")]
    [InlineData("a;b")]
    public void EngineProfileId_RejectsCommandLikeOrPathLikeInput(string value)
    {
        Assert.False(EngineProfileId.TryParse(value, out var id));
        Assert.True(id.IsEmpty);
        Assert.Throws<ArgumentException>(() => EngineProfileId.Parse(value));
    }

    [Fact]
    public void EngineProfileId_RejectsOverlongValue()
    {
        var value = new string('a', EngineProfileId.MaxLength + 1);

        Assert.False(EngineProfileId.TryParse(value, out _));
    }

    [Fact]
    public void EngineProfileDescriptor_RejectsControlCharacters()
    {
        var id = EngineProfileId.Parse("discord");

        Assert.Throws<ArgumentException>(() =>
            new EngineProfileDescriptor(id, "Discord\nraw-command"));
    }

    [Fact]
    public void EngineProfileRuntimeState_UsesConsistentFactories()
    {
        var id = EngineProfileId.Parse("discord-youtube");

        Assert.False(EngineProfileRuntimeState.Inactive.IsActive);
        Assert.Null(EngineProfileRuntimeState.Inactive.ActiveProfileId);

        var active = EngineProfileRuntimeState.Active(id);
        Assert.True(active.IsActive);
        Assert.Equal(id, active.ActiveProfileId);
    }

    [Fact]
    public void ActivateProfileContract_DoesNotAcceptRawStringInput()
    {
        var method = typeof(IEngineProfileRuntime).GetMethod(
            nameof(IEngineProfileRuntime.ActivateProfileAsync));

        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(EngineProfileId), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.All(parameters, parameter => Assert.NotEqual(typeof(string), parameter.ParameterType));
    }

    [Fact]
    public void PublicProfileDescriptor_DoesNotExposeExecutionSurface()
    {
        var propertyNames = typeof(EngineProfileDescriptor)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain("Arguments", propertyNames);
        Assert.DoesNotContain("Command", propertyNames);
        Assert.DoesNotContain("Script", propertyNames);
        Assert.DoesNotContain("Lua", propertyNames);
        Assert.DoesNotContain("Path", propertyNames);
        Assert.DoesNotContain("Url", propertyNames);
    }

    [Fact]
    public void EngineCapabilitySet_RejectsUnknownFlags()
    {
        var unknown = (EngineTrafficCapability)(1 << 20);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EngineCapabilitySet(unknown));
    }

    [Fact]
    public void DiscordProfile_RequiresTransparentTcpAndUdp()
    {
        var required = BuiltInEngineProfileCapabilityPolicy.GetRequiredCapabilities(
            BuiltInEngineProfileCatalog.DiscordId);

        Assert.Equal(
            EngineTrafficCapability.TransparentTcp
            | EngineTrafficCapability.TransparentUdp,
            required);
    }

    [Fact]
    public void YouTubeProfile_RequiresTransparentTcp()
    {
        var required = BuiltInEngineProfileCapabilityPolicy.GetRequiredCapabilities(
            BuiltInEngineProfileCatalog.YouTubeId);

        Assert.Equal(EngineTrafficCapability.TransparentTcp, required);
    }

    [Fact]
    public void ExplicitProxyCapabilities_DoNotSatisfyTransparentProfile()
    {
        var capabilities = new EngineCapabilitySet(
            EngineTrafficCapability.ExplicitProxyTcp
            | EngineTrafficCapability.ExplicitProxyUdp);

        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.YouTubeId,
            capabilities));
        Assert.False(BuiltInEngineProfileCapabilityPolicy.IsSupported(
            BuiltInEngineProfileCatalog.DiscordId,
            capabilities));
    }
}
