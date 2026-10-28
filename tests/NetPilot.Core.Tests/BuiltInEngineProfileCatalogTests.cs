using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class BuiltInEngineProfileCatalogTests
{
    [Fact]
    public void Catalog_ContainsOnlyExpectedBuiltInProfiles()
    {
        var ids = BuiltInEngineProfileCatalog.All
            .Select(profile => profile.Id.Value)
            .ToArray();

        Assert.Equal(
            ["standard", "discord", "youtube", "discord-youtube"],
            ids);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(1, BuiltInEngineProfileCatalog.SchemaVersion);
    }

    [Theory]
    [InlineData("standard")]
    [InlineData("discord")]
    [InlineData("youtube")]
    [InlineData("discord-youtube")]
    public void TryGet_ReturnsKnownProfile(string value)
    {
        var id = EngineProfileId.Parse(value);

        var found = BuiltInEngineProfileCatalog.TryGet(id, out var descriptor);

        Assert.True(found);
        Assert.NotNull(descriptor);
        Assert.Equal(id, descriptor!.Id);
    }

    [Fact]
    public void TryGet_RejectsSyntacticallyValidButUnknownProfile()
    {
        var id = EngineProfileId.Parse("unknown-profile");

        var found = BuiltInEngineProfileCatalog.TryGet(id, out var descriptor);

        Assert.False(found);
        Assert.Null(descriptor);
        Assert.False(BuiltInEngineProfileCatalog.IsBuiltIn(id));
    }

    [Fact]
    public void AutoMode_IsNotAnExecutionProfile()
    {
        var autoId = EngineProfileId.Parse("auto");

        Assert.False(BuiltInEngineProfileCatalog.IsBuiltIn(autoId));
    }

    [Fact]
    public void PublicCatalog_IsReadOnly()
    {
        var list = Assert.IsAssignableFrom<IList<EngineProfileDescriptor>>(
            BuiltInEngineProfileCatalog.All);

        Assert.Throws<NotSupportedException>(() =>
            list.Add(new EngineProfileDescriptor(
                EngineProfileId.Parse("extra"),
                "Extra")));
    }
}
