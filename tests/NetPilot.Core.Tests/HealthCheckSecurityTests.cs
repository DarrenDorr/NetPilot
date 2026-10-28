using System.Net;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Core.Tests;

public sealed class HealthCheckSecurityTests
{
    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://localhost/")]
    [InlineData("https://sub.localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://10.0.0.1/")]
    [InlineData("https://172.16.0.1/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://169.254.1.1/")]
    [InlineData("https://100.64.0.1/")]
    [InlineData("https://[::1]/")]
    [InlineData("https://[fc00::1]/")]
    [InlineData("https://[fe80::1]/")]
    [InlineData("https://example.com:8443/")]
    [InlineData("https://user:password@example.com/")]
    public void Unsafe_health_endpoint_is_rejected(string value)
    {
        var exception = Assert.Throws<HealthCheckSecurityException>(
            () => PublicEndpointPolicy.ValidateUri(new Uri(value)));

        Assert.False(string.IsNullOrWhiteSpace(exception.Code));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:4700:4700::1111")]
    public void Known_public_address_is_allowed(string value)
    {
        Assert.True(PublicEndpointPolicy.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("127.0.0.1")]
    [InlineData("10.10.10.10")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.10.20")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("2001:db8::1")]
    public void Non_public_address_is_rejected(string value)
    {
        Assert.False(PublicEndpointPolicy.IsPublicAddress(IPAddress.Parse(value)));
    }

    [Fact]
    public void Mixed_dns_answer_with_private_address_is_rejected()
    {
        var exception = Assert.Throws<HealthCheckSecurityException>(
            () => PublicEndpointPolicy.EnsureAllResolvedAddressesArePublic(
            [
                IPAddress.Parse("1.1.1.1"),
                IPAddress.Parse("127.0.0.1")
            ]));

        Assert.Equal("NonPublicAddressRejected", exception.Code);
    }

    [Fact]
    public void Built_in_endpoints_pass_public_https_policy()
    {
        Assert.NotEmpty(BuiltInResourceCatalog.All);

        foreach (var resource in BuiltInResourceCatalog.All)
        {
            Assert.NotEmpty(resource.HealthCheckUris);
            Assert.Empty(resource.StrategyDomains);
            foreach (var uri in resource.HealthCheckUris)
            {
                PublicEndpointPolicy.ValidateUri(uri);
            }
        }
    }

    [Fact]
    public async Task Healthy_response_does_not_read_body_and_reports_healthy()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var checker = new PublicHttpsHealthChecker(client);
        var resource = new ResourceDefinition(
            "test",
            "Test",
            [new Uri("https://example.com/health")],
            []);

        var result = await checker.CheckAsync(resource, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceHealthState.Healthy, result.State);
        Assert.Null(result.DiagnosticCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Redirect_to_loopback_is_rejected_before_second_request()
    {
        var handler = new FakeHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://127.0.0.1/private");
            return response;
        });

        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var checker = new PublicHttpsHealthChecker(client);
        var resource = new ResourceDefinition(
            "test",
            "Test",
            [new Uri("https://example.com/")],
            []);

        var result = await checker.CheckAsync(resource, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceHealthState.Unavailable, result.State);
        Assert.Equal("EndpointRejected", result.DiagnosticCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Resource_with_any_unsafe_configured_endpoint_is_rejected_without_network()
    {
        var handler = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var checker = new PublicHttpsHealthChecker(client);
        var resource = new ResourceDefinition(
            "test",
            "Test",
            [
                new Uri("https://example.com/"),
                new Uri("https://192.168.1.1/")
            ],
            []);

        var result = await checker.CheckAsync(resource, TestContext.Current.CancellationToken);

        Assert.Equal(ResourceHealthState.Unavailable, result.State);
        Assert.Equal("EndpointRejected", result.DiagnosticCode);
        Assert.Equal(0, handler.Calls);
    }

    private sealed class FakeHttpHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(responseFactory(request));
        }
    }
}
