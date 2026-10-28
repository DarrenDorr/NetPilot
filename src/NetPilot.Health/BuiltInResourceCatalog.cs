using NetPilot.Core.Models;

namespace NetPilot.Health;

public static class BuiltInResourceCatalog
{
    public static ResourceDefinition Discord { get; } = new(
        "discord",
        "Discord",
        [new Uri("https://discord.com/")],
        []);

    public static ResourceDefinition YouTube { get; } = new(
        "youtube",
        "YouTube",
        [new Uri("https://www.youtube.com/generate_204")],
        []);

    public static IReadOnlyList<ResourceDefinition> All { get; } =
        [Discord, YouTube];
}
