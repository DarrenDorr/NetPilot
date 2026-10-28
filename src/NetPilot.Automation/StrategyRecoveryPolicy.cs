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
    private static readonly HashSet<string> RecoverableDiagnosticCodes =
        new(StringComparer.Ordinal)
        {
            "TcpConnectFailed",
            "TlsHandshakeFailed",
            "Timeout"
        };

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

        if (failures.Any(static result =>
                string.Equals(result.DiagnosticCode, "EndpointRejected", StringComparison.Ordinal)
                || string.Equals(result.DiagnosticCode, "NoHealthEndpoints", StringComparison.Ordinal)))
        {
            return StrategyRecoveryDecision.Blocked(
                "RecoverySuppressedSecurityOrConfiguration");
        }

        if (failures.Any(static result =>
                string.Equals(result.DiagnosticCode, "DnsResolutionFailed", StringComparison.Ordinal)))
        {
            return StrategyRecoveryDecision.Blocked("RecoverySuppressedDnsFailure");
        }

        if (failures.Any(static result => IsHttpOrRedirectDiagnostic(result.DiagnosticCode)))
        {
            return StrategyRecoveryDecision.Blocked("RecoverySuppressedHttpResponse");
        }

        if (failures.Any(static result =>
                result.State is ResourceHealthState.Unknown or ResourceHealthState.Checking
                || string.IsNullOrWhiteSpace(result.DiagnosticCode)
                || !RecoverableDiagnosticCodes.Contains(result.DiagnosticCode)))
        {
            return StrategyRecoveryDecision.Blocked(
                "RecoverySuppressedUnclassifiedFailure");
        }

        return StrategyRecoveryDecision.Allowed;
    }

    private static bool IsHttpOrRedirectDiagnostic(string? diagnosticCode)
    {
        if (string.IsNullOrWhiteSpace(diagnosticCode))
        {
            return false;
        }

        return diagnosticCode.StartsWith("Http", StringComparison.Ordinal)
            || string.Equals(diagnosticCode, "TooManyRedirects", StringComparison.Ordinal)
            || string.Equals(diagnosticCode, "RedirectWithoutLocation", StringComparison.Ordinal);
    }
}
