namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Внутренние lab-only profile ids. Они не входят в пользовательский
/// BuiltInEngineProfileCatalog и не должны публиковаться GUI/Automation.
/// Наличие lab profile не является runtime admission.
/// </summary>
public static class EngineLabProfileCatalog
{
    public static EngineProfileId ByeDpiLoopbackR34Id { get; } =
        EngineProfileId.Parse("lab-byedpi-loopback-r34");

    public static bool IsTrustedLabProfile(EngineProfileId profileId) =>
        profileId == ByeDpiLoopbackR34Id;
}
