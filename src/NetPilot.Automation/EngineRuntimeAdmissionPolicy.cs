using NetPilot.Engine.Abstractions;

namespace NetPilot.Automation;

/// <summary>
/// Внешняя политика допуска profile runtime.
/// Capability declaration описывает только архитектурные возможности backend и
/// никогда не должна сама по себе считаться разрешением на запуск.
///
/// Любая будущая production-реализация, возвращающая true, обязана независимо
/// проверить immutable identity допущенного native artifact/build и не может
/// основывать решение только на EngineId, платформе или capability-флагах.
/// </summary>
public interface IEngineRuntimeAdmissionPolicy
{
    bool IsAdmitted(
        IEngineProfileRuntime profileRuntime,
        EngineBackendCapabilityDeclaration declaration);
}

/// <summary>
/// Текущая production-политика: ни один native engine не имеет ADMIT.
/// Статeless singleton не содержит allowlist, путей, hash-исключений или скрытых bypass.
/// </summary>
public sealed class DenyAllEngineRuntimeAdmissionPolicy : IEngineRuntimeAdmissionPolicy
{
    private DenyAllEngineRuntimeAdmissionPolicy()
    {
    }

    public static DenyAllEngineRuntimeAdmissionPolicy Instance { get; } = new();

    public bool IsAdmitted(
        IEngineProfileRuntime profileRuntime,
        EngineBackendCapabilityDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(profileRuntime);
        ArgumentNullException.ThrowIfNull(declaration);
        return false;
    }
}
