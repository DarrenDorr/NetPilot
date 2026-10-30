using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiPackageLifecyclePolicyTests
{
    [Fact]
    public void Any_mutation_is_blocked_while_engine_lease_is_active()
    {
        foreach (var operation in Enum.GetValues<ByeDpiPackageLifecycleOperation>())
        {
            var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
                Request(
                    operation,
                    target: operation == ByeDpiPackageLifecycleOperation.Uninstall ? null : "0.17.3",
                    current: operation == ByeDpiPackageLifecycleOperation.Install ? null : "0.17.3",
                    previous: "0.17.2",
                    engineLeaseActive: true));

            Assert.False(decision.Allowed);
            Assert.Equal(
                ByeDpiPackageLifecycleDecisionCode.BlockActiveEngineLease,
                decision.Code);
        }
    }

    [Fact]
    public void Fresh_verified_package_can_be_installed_from_protected_same_volume_staging()
    {
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Install,
                target: "0.17.3"));

        Assert.True(decision.Allowed);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.AllowInstall, decision.Code);
        Assert.Equal("0.17.3", decision.CanonicalTargetVersion);
    }

    [Theory]
    [InlineData(false, true, true, ByeDpiPackageLifecycleDecisionCode.BlockPackageNotVerified)]
    [InlineData(true, false, true, ByeDpiPackageLifecycleDecisionCode.BlockUnprotectedStaging)]
    [InlineData(true, true, false, ByeDpiPackageLifecycleDecisionCode.BlockCrossVolumeStaging)]
    public void Install_requires_verified_protected_same_volume_package(
        bool verified,
        bool protectedStaging,
        bool sameVolume,
        ByeDpiPackageLifecycleDecisionCode expected)
    {
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Install,
                target: "0.17.3",
                verified: verified,
                protectedStaging: protectedStaging,
                sameVolume: sameVolume));

        Assert.False(decision.Allowed);
        Assert.Equal(expected, decision.Code);
    }

    [Fact]
    public void Upgrade_requires_strictly_newer_canonical_version()
    {
        var allowed = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Upgrade,
                target: "0.17.4",
                current: "0.17.3"));
        var equal = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Upgrade,
                target: "0.17.3",
                current: "0.17.3"));
        var downgrade = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Upgrade,
                target: "0.17.2",
                current: "0.17.3"));

        Assert.True(allowed.Allowed);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.AllowUpgrade, allowed.Code);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.BlockUpgradeNotNewer, equal.Code);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.BlockUpgradeNotNewer, downgrade.Code);
    }

    [Fact]
    public void Repair_is_exact_version_only()
    {
        var allowed = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Repair,
                target: "0.17.3",
                current: "0.17.3"));
        var rejected = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Repair,
                target: "0.17.4",
                current: "0.17.3"));

        Assert.True(allowed.Allowed);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.AllowRepair, allowed.Code);
        Assert.False(rejected.Allowed);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockRepairVersionMismatch,
            rejected.Code);
    }

    [Fact]
    public void Rollback_is_only_to_exact_retained_previous_version()
    {
        var allowed = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Rollback,
                target: "0.17.2",
                current: "0.17.3",
                previous: "0.17.2"));
        var arbitrary = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Rollback,
                target: "0.17.1",
                current: "0.17.3",
                previous: "0.17.2"));
        var unavailable = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Rollback,
                target: "0.17.2",
                current: "0.17.3",
                previous: null));

        Assert.True(allowed.Allowed);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.AllowRollback, allowed.Code);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockRollbackTargetMismatch,
            arbitrary.Code);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockRollbackUnavailable,
            unavailable.Code);
    }

    [Fact]
    public void Uninstall_requires_installed_package_and_rejects_target_payload()
    {
        var allowed = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Uninstall,
                target: null,
                current: "0.17.3",
                verified: false,
                protectedStaging: false,
                sameVolume: false));
        var noCurrent = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Uninstall,
                target: null,
                current: null));
        var unexpectedTarget = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Uninstall,
                target: "0.17.3",
                current: "0.17.3"));

        Assert.True(allowed.Allowed);
        Assert.Equal(ByeDpiPackageLifecycleDecisionCode.AllowUninstall, allowed.Code);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockCurrentPackageMissing,
            noCurrent.Code);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockUnexpectedTargetVersion,
            unexpectedTarget.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v0.17.3")]
    [InlineData("0.017.3")]
    [InlineData("0.17.3-beta")]
    [InlineData("0.17.3/evil")]
    [InlineData("0..17")]
    public void Target_version_must_be_canonical_numeric_token(string target)
    {
        var decision = ByeDpiPackageLifecyclePolicy.Evaluate(
            Request(
                ByeDpiPackageLifecycleOperation.Install,
                target: target));

        Assert.False(decision.Allowed);
        Assert.Equal(
            ByeDpiPackageLifecycleDecisionCode.BlockInvalidTargetVersion,
            decision.Code);
    }

    private static ByeDpiPackageLifecycleRequest Request(
        ByeDpiPackageLifecycleOperation operation,
        string? target,
        string? current = null,
        string? previous = null,
        bool verified = true,
        bool protectedStaging = true,
        bool sameVolume = true,
        bool engineLeaseActive = false) =>
        new(
            operation,
            target,
            current,
            previous,
            verified,
            protectedStaging,
            sameVolume,
            engineLeaseActive);
}
