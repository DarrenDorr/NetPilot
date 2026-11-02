using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Platform.Windows;

public sealed class ByeDpiProxyResourceHealthProbe(
    WindowsByeDpiStrategyRuntime runtime) : IResourceHealthProbe
{
    private const int MaxRedirects = 3;

    public async Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var port = runtime.ProxyPort;
        if (port is null)
        {
            return CreateHealth(resource.Id, ResourceHealthState.Unavailable, null, "ByeDpiNotRunning");
        }

        ResourceHealth? degraded = null;
        ResourceHealth? unavailable = null;
        foreach (var uri in resource.HealthCheckUris)
        {
            var result = await ProbeAsync(resource.Id, uri, port.Value, cancellationToken);
            if (result.State == ResourceHealthState.Healthy)
            {
                return result;
            }

            if (result.State == ResourceHealthState.Degraded)
            {
                degraded ??= result;
            }
            else
            {
                unavailable ??= result;
            }
        }

        return degraded
            ?? unavailable
            ?? CreateHealth(resource.Id, ResourceHealthState.Unavailable, null, "NoHealthEndpoints");
    }

    private static async Task<ResourceHealth> ProbeAsync(
        string resourceId,
        Uri initialUri,
        int proxyPort,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));

        try
        {
            var currentUri = initialUri;
            for (var redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                await ValidatePublicEndpointAsync(currentUri, timeoutCts.Token);

                using var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false,
                    UseProxy = true,
                    Proxy = new WebProxy(new Uri($"socks5://127.0.0.1:{proxyPort}")),
                    Credentials = null,
                    PreAuthenticate = false,
                    AutomaticDecompression = DecompressionMethods.None,
                    ConnectTimeout = TimeSpan.FromSeconds(3),
                    MaxConnectionsPerServer = 2
                };
                using var client = new HttpClient(handler)
                {
                    Timeout = Timeout.InfiniteTimeSpan
                };
                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.UserAgent.ParseAdd("NetPilot-ByeDPI-HealthCheck/1.0");
                request.Headers.Accept.ParseAdd("*/*");
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == MaxRedirects || response.Headers.Location is null)
                    {
                        return CreateHealth(
                            resourceId,
                            ResourceHealthState.Degraded,
                            stopwatch.Elapsed,
                            "ProxyRedirectRejected");
                    }

                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);
                    continue;
                }

                var status = (int)response.StatusCode;
                if (status is >= 200 and <= 299)
                {
                    return CreateHealth(resourceId, ResourceHealthState.Healthy, stopwatch.Elapsed, null);
                }

                if (status is 401 or 403 or 408 or 429)
                {
                    return CreateHealth(
                        resourceId,
                        ResourceHealthState.Degraded,
                        stopwatch.Elapsed,
                        $"Http{status}");
                }

                return CreateHealth(
                    resourceId,
                    status >= 500 ? ResourceHealthState.Unavailable : ResourceHealthState.Degraded,
                    stopwatch.Elapsed,
                    $"Http{status}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyTimeout");
        }
        catch (HealthCheckSecurityException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "EndpointRejected");
        }
        catch (SocketException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "DnsError");
        }
        catch (HttpRequestException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyNetworkError");
        }

        return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyHealthCheckFailed");
    }

    private static async Task ValidatePublicEndpointAsync(Uri uri, CancellationToken cancellationToken)
    {
        PublicEndpointPolicy.ValidateUri(uri);
        var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
        PublicEndpointPolicy.EnsureAllResolvedAddressesArePublic(addresses);
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static ResourceHealth CreateHealth(
        string resourceId,
        ResourceHealthState state,
        TimeSpan? latency,
        string? diagnosticCode) =>
        new(resourceId, state, DateTimeOffset.UtcNow, latency, diagnosticCode);
}
