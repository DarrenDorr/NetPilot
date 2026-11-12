using System.Diagnostics;
using System.Net;
using NetPilot.Automation;
using NetPilot.Core.Models;
using NetPilot.Health;

namespace NetPilot.Platform.Windows;

public sealed class ByeDpiProxyResourceHealthProbe(
    WindowsByeDpiStrategyRuntime runtime) : IResourceHealthProbe
{
    private const int MaxRedirects = 3;

    private static readonly IReadOnlyDictionary<string, ResourceDefinition> AllowedResources =
        BuiltInResourceCatalog.All.ToDictionary(
            static resource => resource.Id,
            StringComparer.Ordinal);

    public async Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!IsCanonicalBuiltInResource(resource))
        {
            return CreateHealth(
                resource.Id,
                ResourceHealthState.Unavailable,
                null,
                "EndpointRejected");
        }

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
            var allowedHost = initialUri.IdnHost;
            var currentUri = initialUri;
            for (var redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                ValidateProxyEndpoint(currentUri, allowedHost);

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
        catch (HttpRequestException exception)
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                ClassifyNetworkError(exception.HttpRequestError));
        }

        return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyHealthCheckFailed");
    }

    // Diagnostic codes are bounded constants, never exception messages, stack traces,
    // requested URLs, IPs or SOCKS5 protocol payloads.
    public static string ClassifyNetworkError(HttpRequestError error) => error switch
    {
        HttpRequestError.NameResolutionError => "ProxyNameResolutionError",
        HttpRequestError.ConnectionError => "ProxyConnectionError",
        HttpRequestError.SecureConnectionError => "ProxyTlsError",
        HttpRequestError.ProxyTunnelError => "ProxyTunnelError",
        HttpRequestError.HttpProtocolError => "ProxyHttpProtocolError",
        _ => "ProxyNetworkError"
    };

    private static bool IsCanonicalBuiltInResource(ResourceDefinition resource)
    {
        if (!AllowedResources.TryGetValue(resource.Id, out var canonical)
            || resource.HealthCheckUris.Count != canonical.HealthCheckUris.Count)
        {
            return false;
        }

        for (var index = 0; index < canonical.HealthCheckUris.Count; index++)
        {
            if (Uri.Compare(
                    resource.HealthCheckUris[index],
                    canonical.HealthCheckUris[index],
                    UriComponents.AbsoluteUri,
                    UriFormat.SafeUnescaped,
                    StringComparison.Ordinal) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateProxyEndpoint(Uri uri, string allowedHost)
    {
        PublicEndpointPolicy.ValidateUri(uri);
        if (!string.Equals(uri.IdnHost, allowedHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new HealthCheckSecurityException(
                "CrossHostRedirectRejected",
                "ByeDPI health-check запрещает redirect на другой host.");
        }
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
