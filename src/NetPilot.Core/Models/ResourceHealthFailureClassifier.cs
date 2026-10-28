namespace NetPilot.Core.Models;

public enum ResourceHealthFailureClass
{
    Healthy,
    RecoverableNetworkPath,
    DnsFailure,
    ServiceResponse,
    SecurityOrConfiguration,
    Unclassified
}

/// <summary>
/// Единая engine-neutral классификация результата health-check.
/// Класс только интерпретирует уже полученный безопасный diagnostic code и не выполняет
/// сетевых, файловых или привилегированных операций.
/// </summary>
public static class ResourceHealthFailureClassifier
{
    public static ResourceHealthFailureClass Classify(ResourceHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        if (health.State == ResourceHealthState.Healthy)
        {
            return ResourceHealthFailureClass.Healthy;
        }

        if (health.State is ResourceHealthState.Unknown or ResourceHealthState.Checking)
        {
            return ResourceHealthFailureClass.Unclassified;
        }

        return health.DiagnosticCode switch
        {
            "TcpConnectFailed" or "TlsHandshakeFailed" or "Timeout" =>
                ResourceHealthFailureClass.RecoverableNetworkPath,
            "DnsResolutionFailed" =>
                ResourceHealthFailureClass.DnsFailure,
            "EndpointRejected" or "NoHealthEndpoints" =>
                ResourceHealthFailureClass.SecurityOrConfiguration,
            "TooManyRedirects" or "RedirectWithoutLocation" =>
                ResourceHealthFailureClass.ServiceResponse,
            { } code when IsHttpDiagnostic(code) =>
                ResourceHealthFailureClass.ServiceResponse,
            _ => ResourceHealthFailureClass.Unclassified
        };
    }

    public static ResourceHealthFailureClass ClassifyAggregate(
        IEnumerable<ResourceHealth> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var classes = results.Select(Classify).ToArray();
        if (classes.Length == 0 || classes.All(static value => value == ResourceHealthFailureClass.Healthy))
        {
            return ResourceHealthFailureClass.Healthy;
        }

        // При смешанном результате приоритет отдаётся причинам, для которых автоматическое
        // вмешательство наиболее опасно или бессмысленно. Это тот же порядок, который
        // использует Automation перед сменой профиля.
        if (classes.Contains(ResourceHealthFailureClass.SecurityOrConfiguration))
        {
            return ResourceHealthFailureClass.SecurityOrConfiguration;
        }

        if (classes.Contains(ResourceHealthFailureClass.DnsFailure))
        {
            return ResourceHealthFailureClass.DnsFailure;
        }

        if (classes.Contains(ResourceHealthFailureClass.ServiceResponse))
        {
            return ResourceHealthFailureClass.ServiceResponse;
        }

        if (classes.Contains(ResourceHealthFailureClass.Unclassified))
        {
            return ResourceHealthFailureClass.Unclassified;
        }

        return ResourceHealthFailureClass.RecoverableNetworkPath;
    }

    private static bool IsHttpDiagnostic(string diagnosticCode)
    {
        if (diagnosticCode.Length != 7
            || !diagnosticCode.StartsWith("Http", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(diagnosticCode.AsSpan(4), out var statusCode)
            && statusCode is >= 100 and <= 599;
    }
}
