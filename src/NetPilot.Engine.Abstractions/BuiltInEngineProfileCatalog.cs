namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Встроенный продуктовый каталог профилей Net.Pilot.
/// Каталог описывает только пользовательское намерение и не содержит
/// параметров конкретного сетевого движка.
/// </summary>
public static class BuiltInEngineProfileCatalog
{
    public const int SchemaVersion = 1;

    public static EngineProfileId StandardId { get; } =
        EngineProfileId.Parse("standard");

    public static EngineProfileId DiscordId { get; } =
        EngineProfileId.Parse("discord");

    public static EngineProfileId YouTubeId { get; } =
        EngineProfileId.Parse("youtube");

    public static EngineProfileId DiscordYouTubeId { get; } =
        EngineProfileId.Parse("discord-youtube");

    private static readonly IReadOnlyList<EngineProfileDescriptor> Profiles =
        Array.AsReadOnly(
        [
            new EngineProfileDescriptor(StandardId, "Стандартный"),
            new EngineProfileDescriptor(DiscordId, "Discord"),
            new EngineProfileDescriptor(YouTubeId, "YouTube"),
            new EngineProfileDescriptor(DiscordYouTubeId, "Discord + YouTube")
        ]);

    public static IReadOnlyList<EngineProfileDescriptor> All => Profiles;

    public static bool TryGet(
        EngineProfileId profileId,
        out EngineProfileDescriptor? descriptor)
    {
        descriptor = null;
        if (profileId.IsEmpty)
        {
            return false;
        }

        foreach (var candidate in Profiles)
        {
            if (candidate.Id == profileId)
            {
                descriptor = candidate;
                return true;
            }
        }

        return false;
    }

    public static bool IsBuiltIn(EngineProfileId profileId) =>
        TryGet(profileId, out _);
}
