namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Составная доверенная metadata-декларация для будущего stateful runtime adapter.
/// Она объединяет backend capability и resource-budget declarations одной пары
/// backend/platform, но сама по себе НЕ является runtime admission и не содержит
/// executable, путей, аргументов, URL или process/driver controls.
/// </summary>
public sealed record EngineRuntimePrerequisiteDeclaration
{
    internal EngineRuntimePrerequisiteDeclaration(
        EngineBackendCapabilityDeclaration capabilityDeclaration,
        EngineResourceBudgetDeclaration resourceBudgetDeclaration)
    {
        ArgumentNullException.ThrowIfNull(capabilityDeclaration);
        ArgumentNullException.ThrowIfNull(resourceBudgetDeclaration);

        if (capabilityDeclaration.Backend != resourceBudgetDeclaration.Backend
            || capabilityDeclaration.Platform != resourceBudgetDeclaration.Platform)
        {
            throw new ArgumentException(
                "Capability и resource-budget declarations должны относиться к одной паре backend/platform.");
        }

        CapabilityDeclaration = capabilityDeclaration;
        ResourceBudgetDeclaration = resourceBudgetDeclaration;
    }

    public string EngineId => CapabilityDeclaration.EngineId;

    public BuiltInEngineBackendKind Backend => CapabilityDeclaration.Backend;

    public EngineHostPlatform Platform => CapabilityDeclaration.Platform;

    public EngineBackendCapabilityDeclaration CapabilityDeclaration { get; }

    public EngineResourceBudgetDeclaration ResourceBudgetDeclaration { get; }
}

/// <summary>
/// Контракт будущего stateful adapter/runtime: потребитель получает одну составную
/// trusted declaration вместо независимых weak flags/numbers.
/// Наличие этого объекта всё равно требует отдельного IEngineRuntimeAdmissionPolicy
/// на уровне Automation/production composition.
/// </summary>
public interface IEngineRuntimePrerequisiteProvider
{
    EngineRuntimePrerequisiteDeclaration RuntimePrerequisites { get; }
}

/// <summary>
/// Собирает составную declaration только из двух встроенных доверенных каталогов.
/// Если хотя бы capability или resource budget не promoted — результат отсутствует
/// fail-closed. В R28.11 resource-budget catalog пуст, поэтому All также пуст.
/// </summary>
public static class BuiltInEngineRuntimePrerequisiteCatalog
{
    private static readonly IReadOnlyList<EngineRuntimePrerequisiteDeclaration> Declarations =
        BuildDeclarations();

    public static IReadOnlyList<EngineRuntimePrerequisiteDeclaration> All => Declarations;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        out EngineRuntimePrerequisiteDeclaration? declaration)
    {
        declaration = null;

        if (!BuiltInEngineBackendCapabilityCatalog.TryGet(
                backend,
                platform,
                out var capabilityDeclaration)
            || capabilityDeclaration is null)
        {
            return false;
        }

        if (!BuiltInEngineResourceBudgetCatalog.TryGet(
                backend,
                platform,
                out var resourceBudgetDeclaration)
            || resourceBudgetDeclaration is null)
        {
            return false;
        }

        declaration = new EngineRuntimePrerequisiteDeclaration(
            capabilityDeclaration,
            resourceBudgetDeclaration);
        return true;
    }

    public static EngineRuntimePrerequisiteDeclaration Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        if (TryGet(backend, platform, out var declaration) && declaration is not null)
        {
            return declaration;
        }

        throw new InvalidOperationException(
            "Для указанной пары backend/platform отсутствует полный trusted prerequisite set Net.Pilot.");
    }

    private static IReadOnlyList<EngineRuntimePrerequisiteDeclaration> BuildDeclarations()
    {
        var declarations = new List<EngineRuntimePrerequisiteDeclaration>();

        foreach (var capabilityDeclaration in BuiltInEngineBackendCapabilityCatalog.All)
        {
            if (!BuiltInEngineResourceBudgetCatalog.TryGet(
                    capabilityDeclaration.Backend,
                    capabilityDeclaration.Platform,
                    out var resourceBudgetDeclaration)
                || resourceBudgetDeclaration is null)
            {
                continue;
            }

            declarations.Add(
                new EngineRuntimePrerequisiteDeclaration(
                    capabilityDeclaration,
                    resourceBudgetDeclaration));
        }

        return Array.AsReadOnly(declarations.ToArray());
    }
}
