namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Сетевые возможности конкретного backend Net.Pilot.
/// Это описание способа доставки трафика, а не список параметров запуска.
/// </summary>
[Flags]
public enum EngineTrafficCapability
{
    None = 0,
    ExplicitProxyTcp = 1 << 0,
    ExplicitProxyUdp = 1 << 1,
    TransparentTcp = 1 << 2,
    TransparentUdp = 1 << 3
}

/// <summary>
/// Валидированный набор возможностей backend. Неизвестные флаги отклоняются,
/// чтобы новая возможность не считалась поддержанной старым кодом случайно.
/// </summary>
public readonly record struct EngineCapabilitySet
{
    private const EngineTrafficCapability KnownCapabilities =
        EngineTrafficCapability.ExplicitProxyTcp
        | EngineTrafficCapability.ExplicitProxyUdp
        | EngineTrafficCapability.TransparentTcp
        | EngineTrafficCapability.TransparentUdp;

    public EngineCapabilitySet(EngineTrafficCapability value)
    {
        if ((value & ~KnownCapabilities) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                "Набор возможностей движка содержит неизвестные флаги.");
        }

        Value = value;
    }

    public EngineTrafficCapability Value { get; }

    public bool Supports(EngineTrafficCapability required)
    {
        if ((required & ~KnownCapabilities) != 0)
        {
            return false;
        }

        return (Value & required) == required;
    }

    public static EngineCapabilitySet None { get; } =
        new(EngineTrafficCapability.None);
}

/// <summary>
/// Общий capability-контракт. Он подходит для диагностических/no-admit runtime,
/// но сам по себе недостаточен для публикации реальных пользовательских профилей:
/// набор флагов может быть создан вручную.
/// </summary>
public interface IEngineCapabilityProvider
{
    EngineCapabilitySet Capabilities { get; }
}

/// <summary>
/// Усиленный контракт для runtime, публикующего реальные профили.
/// Декларация имеет internal-конструктор и должна быть получена из доверенного
/// BuiltInEngineBackendCapabilityCatalog, поэтому adapter не принимает произвольные флаги.
/// </summary>
public interface IEngineBackendCapabilityProvider
{
    EngineBackendCapabilityDeclaration CapabilityDeclaration { get; }
}

/// <summary>
/// Центральные минимальные требования встроенных полноценных профилей.
/// Proxy-only режим не должен молча выдавать себя за transparent профиль.
/// </summary>
public static class BuiltInEngineProfileCapabilityPolicy
{
    public static EngineTrafficCapability GetRequiredCapabilities(
        EngineProfileId profileId)
    {
        if (profileId == BuiltInEngineProfileCatalog.StandardId
            || profileId == BuiltInEngineProfileCatalog.YouTubeId)
        {
            return EngineTrafficCapability.TransparentTcp;
        }

        if (profileId == BuiltInEngineProfileCatalog.DiscordId
            || profileId == BuiltInEngineProfileCatalog.DiscordYouTubeId)
        {
            return EngineTrafficCapability.TransparentTcp
                | EngineTrafficCapability.TransparentUdp;
        }

        throw new EngineProfileRuntimeException(
            "UnknownProfile",
            "Для профиля отсутствует доверенная capability-политика Net.Pilot.");
    }

    public static bool IsSupported(
        EngineProfileId profileId,
        EngineCapabilitySet capabilities) =>
        capabilities.Supports(GetRequiredCapabilities(profileId));

    /// <summary>
    /// Возвращает только те встроенные полнофункциональные профили, которые
    /// покрываются конкретной доверенной backend declaration.
    /// Метод не выполняет admission и не разрешает запуск native-компонента.
    /// </summary>
    public static IReadOnlyList<EngineProfileDescriptor> GetSupportedProfiles(
        EngineBackendCapabilityDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        var supported = BuiltInEngineProfileCatalog.All
            .Where(profile => IsSupported(profile.Id, declaration.DirectCapabilities))
            .ToArray();

        return Array.AsReadOnly(supported);
    }
}
