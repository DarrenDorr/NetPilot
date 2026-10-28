namespace NetPilot.Security;

public sealed class OperationPolicy
{
    private static readonly IReadOnlySet<PrivilegedOperation> DefaultAllowed =
        new HashSet<PrivilegedOperation>();

    public bool IsAllowed(PrivilegedOperation operation) =>
        Enum.IsDefined(operation) && DefaultAllowed.Contains(operation);

    // Безопасное значение по умолчанию: ни одна привилегированная операция не разрешена.
    // Каждая будущая операция должна включаться отдельно после реализации авторизации и тестов.
    // API для выполнения произвольного shell-текста намеренно отсутствует.
}
