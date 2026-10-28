using NetPilot.Core.Models;

namespace NetPilot.Automation;

internal readonly record struct StrategyRecoveryDecision(
    bool MayTryAlternativeProfiles,
    string? DiagnosticCode)
{
    public static StrategyRecoveryDecision Allowed { get; } = new(true, null);

    public static StrategyRecoveryDecision Blocked(string diagnosticCode) =>
        new(false, diagnosticCode);
}

internal static class StrategyRecoveryPolicy
{
    public static StrategyRecoveryDecision Evaluate(
        IReadOnlyList<StrategyEvaluation> confirmationEvaluations)
    {
        ArgumentNullException.ThrowIfNull(confirmationEvaluations);

        var failures = confirmationEvaluations
            .SelectMany(static evaluation => evaluation.ResourceResults)
            .Where(static result => result.State != ResourceHealthState.Healthy)
            .ToArray();

        if (failures.Length == 0)
        {
            return StrategyRecoveryDecision.Allowed;
        }

        return ResourceHealthFailureClassifier.ClassifyAggregate(failures) switch
        {
            ResourceHealthFailureClass.RecoverableNetworkPath =>
                StrategyRecoveryDecision.Allowed,
            ResourceHealthFailureClass.SecurityOrConfiguration =>
                StrategyRecoveryDecision.Blocked("RecoverySuppressedSecurityOrConfiguration"),
            ResourceHealthFailureClass.DnsFailure =>
                StrategyRecoveryDecision.Blocked("RecoverySuppressedDnsFailure"),
            ResourceHealthFailureClass.ServiceResponse =>
                StrategyRecoveryDecision.Blocked("RecoverySuppressedHttpResponse"),
            _ => StrategyRecoveryDecision.Blocked("RecoverySuppressedUnclassifiedFailure")
        };
    }
}
