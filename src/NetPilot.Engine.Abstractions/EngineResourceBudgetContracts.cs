namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Доверенная декларация положительных resource budgets для stateful native backend.
/// Это только типизированные числовые пределы: здесь намеренно нет executable, путей,
/// аргументов, URL, CLI/config-текста или статуса runtime admission.
///
/// Нулевые значения не представимы в доверенной declaration: для native patchset
/// zapret2 ноль означает unconfigured/fail-closed состояние и не должен молча
/// превращаться в unlimited budget.
/// </summary>
public sealed record EngineResourceBudgetDeclaration
{
    internal EngineResourceBudgetDeclaration(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        uint conntrackMaxEntries,
        ulong delayedQueueMaxBytes)
    {
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform));
        }

        if (conntrackMaxEntries == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(conntrackMaxEntries),
                "Доверенный conntrack budget обязан быть положительным.");
        }

        if (delayedQueueMaxBytes == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delayedQueueMaxBytes),
                "Доверенный delayed-queue byte budget обязан быть положительным.");
        }

        Backend = backend;
        Platform = platform;
        ConntrackMaxEntries = conntrackMaxEntries;
        DelayedQueueMaxBytes = delayedQueueMaxBytes;
    }

    public BuiltInEngineBackendKind Backend { get; }

    public EngineHostPlatform Platform { get; }

    public uint ConntrackMaxEntries { get; }

    public ulong DelayedQueueMaxBytes { get; }
}

/// <summary>
/// Усиленный контракт для будущего stateful runtime, который использует resource budgets.
/// Реализация runtime не должна создавать declaration вручную: объект должен приходить
/// из доверенного каталога после отдельного evidence/admission решения.
/// </summary>
public interface IEngineResourceBudgetProvider
{
    EngineResourceBudgetDeclaration ResourceBudgetDeclaration { get; }
}

/// <summary>
/// Центральный каталог принятых resource budgets.
///
/// R28.10: каталог намеренно пуст. Положительные lab/production значения ещё не выбраны
/// из workload evidence, поэтому отсутствие записи является единственным безопасным
/// состоянием. Добавление любой записи требует отдельного review и не означает само по
/// себе разрешение запуска native engine.
/// </summary>
public static class BuiltInEngineResourceBudgetCatalog
{
    private static readonly IReadOnlyList<EngineResourceBudgetDeclaration> Declarations =
        Array.AsReadOnly(Array.Empty<EngineResourceBudgetDeclaration>());

    public static IReadOnlyList<EngineResourceBudgetDeclaration> All => Declarations;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        out EngineResourceBudgetDeclaration? declaration)
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

    public static EngineResourceBudgetDeclaration Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        if (TryGet(backend, platform, out var declaration) && declaration is not null)
        {
            return declaration;
        }

        throw new InvalidOperationException(
            "Для указанной пары backend/platform отсутствует доверенная resource-budget declaration Net.Pilot.");
    }
}
