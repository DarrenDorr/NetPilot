namespace NetPilot.Engine.Abstractions;

public enum BuiltInEngineProfileKind
{
    Standard,
    Discord,
    YouTube,
    DiscordYouTube
}

/// <summary>
/// Типизированный план пользовательского намерения Net.Pilot.
/// Это ещё не план запуска процесса: здесь нет executable, путей, аргументов,
/// скриптов, URL или кода конкретного движка.
/// </summary>
public sealed record BuiltInEngineProfilePlan
{
    internal BuiltInEngineProfilePlan(
        EngineProfileId profileId,
        BuiltInEngineProfileKind kind)
    {
        ProfileId = profileId;
        Kind = kind;
    }

    public EngineProfileId ProfileId { get; }

    public BuiltInEngineProfileKind Kind { get; }
}

/// <summary>
/// Детерминированно переводит только встроенный profile ID в типизированный план.
/// Неизвестный ID завершается fail-closed.
/// </summary>
public static class BuiltInEngineProfileCompiler
{
    public static BuiltInEngineProfilePlan Compile(EngineProfileId profileId)
    {
        if (profileId == BuiltInEngineProfileCatalog.StandardId)
        {
            return new BuiltInEngineProfilePlan(
                profileId,
                BuiltInEngineProfileKind.Standard);
        }

        if (profileId == BuiltInEngineProfileCatalog.DiscordId)
        {
            return new BuiltInEngineProfilePlan(
                profileId,
                BuiltInEngineProfileKind.Discord);
        }

        if (profileId == BuiltInEngineProfileCatalog.YouTubeId)
        {
            return new BuiltInEngineProfilePlan(
                profileId,
                BuiltInEngineProfileKind.YouTube);
        }

        if (profileId == BuiltInEngineProfileCatalog.DiscordYouTubeId)
        {
            return new BuiltInEngineProfilePlan(
                profileId,
                BuiltInEngineProfileKind.DiscordYouTube);
        }

        throw new EngineProfileRuntimeException(
            "UnknownProfile",
            "Профиль отсутствует во встроенном каталоге Net.Pilot.");
    }
}
