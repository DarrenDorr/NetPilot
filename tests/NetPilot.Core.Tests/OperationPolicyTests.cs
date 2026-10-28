using NetPilot.Security;

namespace NetPilot.Core.Tests;

public sealed class OperationPolicyTests
{
    [Fact]
    public void All_privileged_operations_are_denied_by_default()
    {
        var policy = new OperationPolicy();

        foreach (var operation in Enum.GetValues<PrivilegedOperation>())
        {
            Assert.False(policy.IsAllowed(operation));
        }
    }
}
