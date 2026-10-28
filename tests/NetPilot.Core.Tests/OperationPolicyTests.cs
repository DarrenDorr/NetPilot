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

    [Fact]
    public void Only_explicitly_enabled_operations_are_allowed()
    {
        var policy = new OperationPolicy(
        [
            PrivilegedOperation.EngineStart,
            PrivilegedOperation.EngineStop
        ]);

        Assert.True(policy.IsAllowed(PrivilegedOperation.EngineStart));
        Assert.True(policy.IsAllowed(PrivilegedOperation.EngineStop));
        Assert.False(policy.IsAllowed(PrivilegedOperation.EngineInstall));
        Assert.False(policy.IsAllowed(PrivilegedOperation.UpdateApply));
        Assert.False(policy.IsAllowed(PrivilegedOperation.BackupRestore));
    }

    [Fact]
    public void No_admitted_engine_policy_allows_only_emergency_stop()
    {
        var policy = OperationPolicy.CreateNoAdmittedEnginePolicy();

        Assert.True(policy.IsAllowed(PrivilegedOperation.EngineStop));
        Assert.False(policy.IsAllowed(PrivilegedOperation.EngineInstall));
        Assert.False(policy.IsAllowed(PrivilegedOperation.EngineStart));
        Assert.False(policy.IsAllowed(PrivilegedOperation.UpdateApply));
        Assert.False(policy.IsAllowed(PrivilegedOperation.BackupRestore));
    }
}
