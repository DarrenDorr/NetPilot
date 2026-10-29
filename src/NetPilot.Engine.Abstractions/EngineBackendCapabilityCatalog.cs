namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Известные native backend-кандидаты Net.Pilot.
/// Значение описывает тип backend и само по себе не означает security admission.
/// </summary>
public enum BuiltInEngineBackendKind
{
    Zapret2,
    ByeDpi
}

/// <summary>
/// Платформа, для которой описывается прямая транспортная возможность backend.
/// Composite-слои вроде Android VpnService здесь намеренно не учитываются.
/// </summary>
public enum EngineHostPlatform
{
    Windows,
    Linux,
    Android
}

/// <summary>
/// Доверенная декларация прямых сетевых возможностей конкретной пары backend/platform.
/// Она не содержит executable, путей, аргументов, URL, команды запуска или статуса admission.
/// </summary>
public sealed record EngineBackendCapabilityDeclaration
{
    internal EngineBackendCapabilityDeclaration(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineCapabilitySet directCapabilities)
    {
        Backend = backend;
        Platform = platform;
        DirectCapabilities = directCapabilities;
    }

    public BuiltInEngineBackendKind Backend { get; }

    public EngineHostPlatform Platform { get; }

    public EngineCapabilitySet DirectCapabilities { get; }
}

/// <summary>
/// Минимальный встроенный каталог архитектурных возможностей backend-кандидатов.
/// ВАЖНО: наличие записи здесь не разрешает скачивание, установку или запуск native-компонента.
/// Runtime admission остаётся отдельным security gate.
/// </summary>
public static class BuiltInEngineBackendCapabilityCatalog
{
    private static readonly IReadOnlyList<EngineBackendCapabilityDeclaration> Declarations =
        Array.AsReadOnly(
        [
            new EngineBackendCapabilityDeclaration(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                new EngineCapabilitySet(
                    EngineTrafficCapability.TransparentTcp
                    | EngineTrafficCapability.TransparentUdp)),
            new EngineBackendCapabilityDeclaration(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Linux,
                new EngineCapabilitySet(
                    EngineTrafficCapability.TransparentTcp
                    | EngineTrafficCapability.TransparentUdp)),
            new EngineBackendCapabilityDeclaration(
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows,
                new EngineCapabilitySet(
                    EngineTrafficCapability.ExplicitProxyTcp
                    | EngineTrafficCapability.ExplicitProxyUdp))
        ]);

    public static IReadOnlyList<EngineBackendCapabilityDeclaration> All => Declarations;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        out EngineBackendCapabilityDeclaration? declaration)
    {
        declaration = null;

        foreach (var candidate in Declarations)
        {
            if (candidate.Backend == backend && candidate.Platform == platform)
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    public static EngineBackendCapabilityDeclaration Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        if (TryGet(backend, platform, out var declaration) && declaration is not null)
        {
            return declaration;
        }

        throw new InvalidOperationException(
            "Для указанной пары backend/platform отсутствует доверенная capability-декларация Net.Pilot.");
    }
}
