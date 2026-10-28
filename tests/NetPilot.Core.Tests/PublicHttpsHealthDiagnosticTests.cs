using System.Net.Http;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Core.Tests;

public sealed class PublicHttpsHealthDiagnosticTests
{
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, PublicHealthDiagnosticClassifier.DnsResolutionFailed)]
    [InlineData(HttpRequestError.ConnectionError, PublicHealthDiagnosticClassifier.TcpConnectFailed)]
    [InlineData(HttpRequestError.SecureConnectionError, PublicHealthDiagnosticClassifier.TlsHandshakeFailed)]
    public async Task Checker_surfaces_specific_network_stage_code(
        HttpRequestError error,
        string expectedCode)
    {
        var handler = new ThrowingHandler(
            new HttpRequestException(error, "simulated probe failure"));
        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        using var checker = new PublicHttpsHealthChecker(client);
        var resource = new ResourceDefinition(
            "test",
            "Test",
            [new Uri("https://example.com/health")],
            []);

        var result = await checker.CheckAsync(
            resource,
            TestContext.Current.CancellationToken);

        Assert.Equal(ResourceHealthState.Unavailable, result.State);
        Assert.Equal(expectedCode, result.DiagnosticCode);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class ThrowingHandler(HttpRequestException exception) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
