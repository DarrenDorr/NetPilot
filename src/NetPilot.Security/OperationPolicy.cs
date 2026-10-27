namespace NetPilot.Security;

public sealed class OperationPolicy
{
    private static readonly IReadOnlySet<PrivilegedOperation> DefaultAllowed =
        new HashSet<PrivilegedOperation>(Enum.GetValues<PrivilegedOperation>());

    public bool IsAllowed(PrivilegedOperation operation) => DefaultAllowed.Contains(operation);

    // Deliberately no API exists for executing arbitrary shell text.
}
