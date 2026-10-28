using NetPilot.Core.Models;

namespace NetPilot.Core.Tests;

public sealed class ResourceHealthFailureClassifierTests
{
    [Theory]
    [InlineData("TcpConnectFailed")]
    [InlineData("TlsHandshakeFailed")]
    [InlineData("Timeout")]
    public void Recoverable_network_path_is_explicit_allowlist(string diagnosticCode)
    {
        Assert.Equal(
            ResourceHealthFailureClass.RecoverableNetworkPath,
            ResourceHealthFailureClassifier.Classify(Unavailable(diagnosticCode)));
    }

    [Fact]
    public void Dns_is_separate_from_profile_recovery()
    {
        Assert.Equal(
            ResourceHealthFailureClass.DnsFailure,
            ResourceHealthFailureClassifier.Classify(Unavailable("DnsResolutionFailed")));
    }

    [Theory]
    [InlineData("Http403")]
    [InlineData("Http429")]
    [InlineData("Http503")]
    [InlineData("TooManyRedirects")]
    [InlineData("RedirectWithoutLocation")]
    public void Service_response_is_not_profile_recovery(string diagnosticCode)
    {
        Assert.Equal(
            ResourceHealthFailureClass.ServiceResponse,
            ResourceHealthFailureClassifier.Classify(Unavailable(diagnosticCode)));
    }

    [Theory]
    [InlineData("EndpointRejected")]
    [InlineData("NoHealthEndpoints")]
    public void Security_or_configuration_is_not_profile_recovery(string diagnosticCode)
    {
        Assert.Equal(
            ResourceHealthFailureClass.SecurityOrConfiguration,
            ResourceHealthFailureClassifier.Classify(Unavailable(diagnosticCode)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NetworkError")]
    [InlineData("UnexpectedFutureDiagnostic")]
    [InlineData("Http999")]
    public void Unknown_future_diagnostic_fails_closed(string? diagnosticCode)
    {
        Assert.Equal(
            ResourceHealthFailureClass.Unclassified,
            ResourceHealthFailureClassifier.Classify(Unavailable(diagnosticCode)));
    }

    [Fact]
    public void Aggregate_uses_fail_closed_precedence()
    {
        var results = new[]
        {
            Unavailable("TlsHandshakeFailed", "discord"),
            Unavailable("DnsResolutionFailed", "youtube")
        };

        Assert.Equal(
            ResourceHealthFailureClass.DnsFailure,
            ResourceHealthFailureClassifier.ClassifyAggregate(results));
    }

    [Fact]
    public void Aggregate_all_healthy_is_healthy()
    {
        var results = new[]
        {
            Healthy("discord"),
            Healthy("youtube")
        };

        Assert.Equal(
            ResourceHealthFailureClass.Healthy,
            ResourceHealthFailureClassifier.ClassifyAggregate(results));
    }

    private static ResourceHealth Unavailable(
        string? diagnosticCode,
        string resourceId = "resource") =>
        new(
            resourceId,
            ResourceHealthState.Unavailable,
            DateTimeOffset.UtcNow,
            null,
            diagnosticCode);

    private static ResourceHealth Healthy(string resourceId) =>
        new(
            resourceId,
            ResourceHealthState.Healthy,
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(50));
}
