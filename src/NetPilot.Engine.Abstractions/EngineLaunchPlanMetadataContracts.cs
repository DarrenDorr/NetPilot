namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Неисполняемая metadata будущего launch plan.
///
/// Объект связывает только trusted runtime prerequisites с одним доверенным
/// EngineProfileId. Пользовательские ids приходят из BuiltInEngineProfileCatalog;
/// изолированные lab-only ids — из EngineLabProfileCatalog. Здесь намеренно отсутствуют
/// executable/path, ArgumentList, command line, URL, environment, process/service/driver
/// или network controls. Наличие metadata не является runtime admission.
/// </summary>
public sealed class EngineLaunchPlanMetadata
{
    internal EngineLaunchPlanMetadata(
        EngineRuntimePrerequisiteDeclaration runtimePrerequisites,
        EngineProfileId profileId)
    {
        ArgumentNullException.ThrowIfNull(runtimePrerequisites);

        var trustedProfile =
            BuiltInEngineProfileCatalog.IsBuiltIn(profileId)
            || EngineLabProfileCatalog.IsTrustedLabProfile(profileId);

        if (profileId.IsEmpty || !trustedProfile)
        {
            throw new EngineProfileRuntimeException(
                "UnknownProfile",
                "Launch-plan metadata разрешает только доверенный product или lab-only профиль Net.Pilot.");
        }

        if (!BuiltInEngineProfileCapabilityPolicy.IsSupported(
                profileId,
                runtimePrerequisites.CapabilityDeclaration.DirectCapabilities))
        {
            throw new EngineProfileRuntimeException(
                "UnsupportedProfileCapabilities",
                "Trusted backend prerequisites не покрывают возможности выбранного профиля.");
        }

        RuntimePrerequisites = runtimePrerequisites;
        ProfileId = profileId;
    }

    public EngineRuntimePrerequisiteDeclaration RuntimePrerequisites { get; }

    public EngineProfileId ProfileId { get; }

    public BuiltInEngineBackendKind Backend => RuntimePrerequisites.Backend;

    public EngineHostPlatform Platform => RuntimePrerequisites.Platform;
}

/// <summary>
/// Контракт будущего adapter для публикации только неисполняемой launch metadata.
/// Отдельный runtime admission и отдельная immutable executable identity остаются
/// обязательными последующими границами и в этот контракт не входят.
/// </summary>
public interface IEngineLaunchPlanMetadataProvider
{
    EngineLaunchPlanMetadata LaunchPlanMetadata { get; }
}

/// <summary>
/// Production catalog создаёт metadata только из полного trusted prerequisite set
/// и пользовательского встроенного профиля. Lab-only ids намеренно не добавляются.
///
/// В R28.12 prerequisite catalog пуст, поэтому готовых production launch-plan metadata нет.
/// </summary>
public static class BuiltInEngineLaunchPlanMetadataCatalog
{
    private static readonly IReadOnlyList<EngineLaunchPlanMetadata> Metadata =
        BuildMetadata();

    public static IReadOnlyList<EngineLaunchPlanMetadata> All => Metadata;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineProfileId profileId,
        out EngineLaunchPlanMetadata? metadata)
    {
        metadata = null;

        if (profileId.IsEmpty || !BuiltInEngineProfileCatalog.IsBuiltIn(profileId))
        {
            return false;
        }

        if (!BuiltInEngineRuntimePrerequisiteCatalog.TryGet(
                backend,
                platform,
                out var prerequisites)
            || prerequisites is null)
        {
            return false;
        }

        if (!BuiltInEngineProfileCapabilityPolicy.IsSupported(
                profileId,
                prerequisites.CapabilityDeclaration.DirectCapabilities))
        {
            return false;
        }

        metadata = new EngineLaunchPlanMetadata(prerequisites, profileId);
        return true;
    }

    public static EngineLaunchPlanMetadata Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineProfileId profileId)
    {
        if (TryGet(backend, platform, profileId, out var metadata) && metadata is not null)
        {
            return metadata;
        }

        throw new InvalidOperationException(
            "Для указанной пары backend/platform/profile отсутствует доверенная неисполняемая launch-plan metadata Net.Pilot.");
    }

    private static IReadOnlyList<EngineLaunchPlanMetadata> BuildMetadata()
    {
        var result = new List<EngineLaunchPlanMetadata>();

        foreach (var prerequisites in BuiltInEngineRuntimePrerequisiteCatalog.All)
        {
            foreach (var profile in BuiltInEngineProfileCatalog.All)
            {
                if (!BuiltInEngineProfileCapabilityPolicy.IsSupported(
                        profile.Id,
                        prerequisites.CapabilityDeclaration.DirectCapabilities))
                {
                    continue;
                }

                result.Add(new EngineLaunchPlanMetadata(prerequisites, profile.Id));
            }
        }

        return Array.AsReadOnly(result.ToArray());
    }
}
