using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class BuiltInEngineProfileCompilerTests
{
    [Theory]
    [InlineData("standard", BuiltInEngineProfileKind.Standard)]
    [InlineData("discord", BuiltInEngineProfileKind.Discord)]
    [InlineData("youtube", BuiltInEngineProfileKind.YouTube)]
    [InlineData("discord-youtube", BuiltInEngineProfileKind.DiscordYouTube)]
    public void Compile_MapsBuiltInProfileToTypedPlan(
        string value,
        BuiltInEngineProfileKind expectedKind)
    {
        var id = EngineProfileId.Parse(value);

        var first = BuiltInEngineProfileCompiler.Compile(id);
        var second = BuiltInEngineProfileCompiler.Compile(id);

        Assert.Equal(id, first.ProfileId);
        Assert.Equal(expectedKind, first.Kind);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Compile_RejectsUnknownButValidProfileId()
    {
        var id = EngineProfileId.Parse("unknown-profile");

        var exception = Assert.Throws<EngineProfileRuntimeException>(() =>
            BuiltInEngineProfileCompiler.Compile(id));

        Assert.Equal("UnknownProfile", exception.Code);
    }

    [Fact]
    public void EveryBuiltInCatalogEntry_HasCompilerMapping()
    {
        foreach (var profile in BuiltInEngineProfileCatalog.All)
        {
            var plan = BuiltInEngineProfileCompiler.Compile(profile.Id);
            Assert.Equal(profile.Id, plan.ProfileId);
        }
    }

    [Fact]
    public void TypedPlan_DoesNotExposeProcessExecutionSurface()
    {
        var properties = typeof(BuiltInEngineProfilePlan)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["ProfileId", "Kind"], properties);
        Assert.DoesNotContain("Executable", properties);
        Assert.DoesNotContain("Arguments", properties);
        Assert.DoesNotContain("Command", properties);
        Assert.DoesNotContain("Path", properties);
        Assert.DoesNotContain("Script", properties);
        Assert.DoesNotContain("Lua", properties);
        Assert.DoesNotContain("Url", properties);
    }

    [Fact]
    public void TypedPlan_CannotBeConstructedByExternalCallers()
    {
        var publicConstructors = typeof(BuiltInEngineProfilePlan)
            .GetConstructors();

        Assert.Empty(publicConstructors);
    }
}
