namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Внутренние lab/user-smoke profile ids. Они не входят в пользовательский
/// BuiltInEngineProfileCatalog и не являются production runtime admission.
/// </summary>
public static class EngineLabProfileCatalog
{
    public static EngineProfileId ByeDpiLoopbackR34Id { get; } =
        EngineProfileId.Parse("lab-byedpi-loopback-r34");

    public static EngineProfileId ByeDpiWindowsUserSmokeV1Id { get; } =
        EngineProfileId.Parse("lab-byedpi-windows-user-smoke-v1");

    public static bool IsTrustedLabProfile(EngineProfileId profileId) =>
        profileId == ByeDpiLoopbackR34Id
        || profileId == ByeDpiWindowsUserSmokeV1Id;
}
