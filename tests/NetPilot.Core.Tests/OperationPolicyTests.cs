using NetPilot.Security;

namespace NetPilot.Core.Tests;

public sealed class OperationPolicyTests
{
    [Fact]
    public void Known_privileged_operation_is_explicitly_allowed()
    {
        var policy = new OperationPolicy();
        Assert.True(policy.IsAllowed(PrivilegedOperation.EngineStart));
    }
}
