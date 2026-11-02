using System.Net;
using NetPilot.Updater;

namespace NetPilot.Core.Tests;

public sealed class ByeDpiLiveHttpHandlerFactoryTests
{
    [Fact]
    public void Handler_is_fail_closed_for_redirect_credentials_cookies_proxy_and_decompression()
    {
        using var handler = ByeDpiLiveHttpHandlerFactory.Create();

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(ByeDpiAcquisitionTrustPolicy.MaxRedirects, handler.MaxAutomaticRedirections);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
        Assert.Null(handler.Credentials);
        Assert.False(handler.PreAuthenticate);
        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
    }

    [Fact]
    public void Handler_resource_limits_fit_inside_transport_timeout_boundary()
    {
        using var handler = ByeDpiLiveHttpHandlerFactory.Create();

        Assert.Equal(ByeDpiLiveHttpHandlerFactory.ConnectTimeout, handler.ConnectTimeout);
        Assert.True(
            handler.ConnectTimeout
            < TimeSpan.FromSeconds(ByeDpiAcquisitionTrustPolicy.NetworkTimeoutSeconds));
        Assert.Equal(
            ByeDpiLiveHttpHandlerFactory.PooledConnectionLifetime,
            handler.PooledConnectionLifetime);
        Assert.Equal(
            ByeDpiLiveHttpHandlerFactory.PooledConnectionIdleTimeout,
            handler.PooledConnectionIdleTimeout);
        Assert.Equal(
            ByeDpiLiveHttpHandlerFactory.MaxConnectionsPerServer,
            handler.MaxConnectionsPerServer);
    }

    [Fact]
    public void Handler_has_no_custom_connection_or_trace_propagation_hook()
    {
        using var handler = ByeDpiLiveHttpHandlerFactory.Create();

        Assert.Null(handler.ConnectCallback);
        Assert.Null(handler.PlaintextStreamFilter);
        Assert.Null(handler.ActivityHeadersPropagator);
    }
}
