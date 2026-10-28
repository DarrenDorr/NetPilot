namespace NetPilot.Security;

public sealed class OperationPolicy
{
    private readonly IReadOnlySet<PrivilegedOperation> _allowed;

    public OperationPolicy(IEnumerable<PrivilegedOperation>? explicitlyAllowed = null)
    {
        _allowed = explicitlyAllowed is null
            ? new HashSet<PrivilegedOperation>()
            : new HashSet<PrivilegedOperation>(
                explicitlyAllowed.Where(Enum.IsDefined));
    }

    public bool IsAllowed(PrivilegedOperation operation) =>
        Enum.IsDefined(operation) && _allowed.Contains(operation);

    // Безопасное значение по умолчанию: ни одна привилегированная операция не разрешена.
    // Служба должна явно передать минимальный набор capabilities текущего этапа.
    // API для выполнения произвольного shell-текста намеренно отсутствует.
}
