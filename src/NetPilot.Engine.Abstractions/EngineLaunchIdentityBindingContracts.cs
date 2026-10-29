namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Неисполняемая связь между trusted launch metadata и exact immutable artifact identity.
/// Объект только доказывает согласованность backend/platform/profile/artifact architecture.
/// Он не содержит filesystem path, arguments, process/service/driver controls или admission grant.
/// </summary>
public sealed class EngineLaunchIdentityBinding
{
    internal EngineLaunchIdentityBinding(
        EngineLaunchPlanMetadata launchPlanMetadata,
        EngineExecutableIdentityDeclaration executableIdentity,
        EngineBinaryArchitecture expectedArchitecture)
    {
        ArgumentNullException.ThrowIfNull(launchPlanMetadata);
        ArgumentNullException.ThrowIfNull(executableIdentity);

        if (!Enum.IsDefined(expectedArchitecture))
        {
            throw new ArgumentOutOfRangeException(nameof(expectedArchitecture));
        }

        if (launchPlanMetadata.Backend != executableIdentity.Backend
            || launchPlanMetadata.Platform != executableIdentity.Platform)
        {
            throw new ArgumentException(
                "Launch metadata и immutable executable identity должны относиться к одной паре backend/platform.");
        }

        if (executableIdentity.Architecture != expectedArchitecture)
        {
            throw new ArgumentException(
                "Immutable executable identity не соответствует ожидаемой архитектуре host target.",
                nameof(expectedArchitecture));
        }

        LaunchPlanMetadata = launchPlanMetadata;
        ExecutableIdentity = executableIdentity;
        Architecture = expectedArchitecture;
    }

    public string EngineId => LaunchPlanMetadata.RuntimePrerequisites.EngineId;

    public BuiltInEngineBackendKind Backend => LaunchPlanMetadata.Backend;

    public EngineHostPlatform Platform => LaunchPlanMetadata.Platform;

    public EngineProfileId ProfileId => LaunchPlanMetadata.ProfileId;

    public EngineBinaryArchitecture Architecture { get; }

    public EngineLaunchPlanMetadata LaunchPlanMetadata { get; }

    public EngineExecutableIdentityDeclaration ExecutableIdentity { get; }
}

/// <summary>
/// Будущий adapter получает одну согласованную non-executable binding declaration.
/// Наличие binding не является runtime admission и не разрешает установку/запуск.
/// </summary>
public interface IEngineLaunchIdentityBindingProvider
{
    EngineLaunchIdentityBinding LaunchIdentityBinding { get; }
}

/// <summary>
/// Создаёт binding только из двух trusted каталогов: launch metadata и immutable identity.
/// Пока хотя бы один из них пуст, binding отсутствует fail-closed.
/// </summary>
public static class BuiltInEngineLaunchIdentityBindingCatalog
{
    private static readonly IReadOnlyList<EngineLaunchIdentityBinding> Bindings =
        BuildBindings();

    public static IReadOnlyList<EngineLaunchIdentityBinding> All => Bindings;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        EngineProfileId profileId,
        out EngineLaunchIdentityBinding? binding)
    {
        binding = null;

        if (!Enum.IsDefined(architecture)
            || profileId.IsEmpty
            || !BuiltInEngineProfileCatalog.IsBuiltIn(profileId))
        {
            return false;
        }

        if (!BuiltInEngineLaunchPlanMetadataCatalog.TryGet(
                backend,
                platform,
                profileId,
                out var launchMetadata)
            || launchMetadata is null)
        {
            return false;
        }

        if (!BuiltInEngineExecutableIdentityCatalog.TryGet(
                backend,
                platform,
                architecture,
                out var executableIdentity)
            || executableIdentity is null)
        {
            return false;
        }

        binding = new EngineLaunchIdentityBinding(
            launchMetadata,
            executableIdentity,
            architecture);
        return true;
    }

    public static EngineLaunchIdentityBinding Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        EngineProfileId profileId)
    {
        if (TryGet(
                backend,
                platform,
                architecture,
                profileId,
                out var binding)
            && binding is not null)
        {
            return binding;
        }

        throw new InvalidOperationException(
            "Для указанного backend/platform/architecture/profile отсутствует trusted launch identity binding Net.Pilot.");
    }

    private static IReadOnlyList<EngineLaunchIdentityBinding> BuildBindings()
    {
        var result = new List<EngineLaunchIdentityBinding>();

        foreach (var launchMetadata in BuiltInEngineLaunchPlanMetadataCatalog.All)
        {
            foreach (var identity in BuiltInEngineExecutableIdentityCatalog.All)
            {
                if (launchMetadata.Backend != identity.Backend
                    || launchMetadata.Platform != identity.Platform)
                {
                    continue;
                }

                result.Add(
                    new EngineLaunchIdentityBinding(
                        launchMetadata,
                        identity,
                        identity.Architecture));
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }
}
