using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPilot.Core.Models;

namespace NetPilot.Health;

public sealed class PublicHttpsHealthChecker : IDisposable
{
    private const int MaxRedirects = 3;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private bool _disposed;

    public PublicHttpsHealthChecker()
        : this(CreateSecureHttpClient(), ownsClient: true)
    {
    }

    internal PublicHttpsHealthChecker(HttpClient httpClient, bool ownsClient = false)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _ownsClient = ownsClient;
    }

    public async Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.HealthCheckUris.Count == 0)
        {
            return CreateHealth(
                resource.Id,
                ResourceHealthState.Unavailable,
                null,
                "NoHealthEndpoints");
        }

        foreach (var uri in resource.HealthCheckUris)
        {
            try
            {
                PublicEndpointPolicy.ValidateUri(uri);
            }
            catch (HealthCheckSecurityException)
            {
                return CreateHealth(
                    resource.Id,
                    ResourceHealthState.Unavailable,
                    null,
                    "EndpointRejected");
            }
        }

        ResourceHealth? degraded = null;
        ResourceHealth? unavailable = null;

        foreach (var uri in resource.HealthCheckUris)
        {
            var result = await ProbeAsync(resource.Id, uri, cancellationToken);
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
            ?? CreateHealth(resource.Id, ResourceHealthState.Unavailable, null, "HealthCheckFailed");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<ResourceHealth> ProbeAsync(
        string resourceId,
        Uri initialUri,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            var currentUri = initialUri;
            for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
            {
                PublicEndpointPolicy.ValidateUri(currentUri);

                using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                request.Headers.UserAgent.ParseAdd("NetPilot-HealthCheck/1.0");
                request.Headers.Accept.ParseAdd("*/*");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount == MaxRedirects)
                    {
                        return CreateHealth(
                            resourceId,
                            ResourceHealthState.Degraded,
                            stopwatch.Elapsed,
                            "TooManyRedirects");
                    }

                    if (response.Headers.Location is null)
                    {
                        return CreateHealth(
                            resourceId,
                            ResourceHealthState.Degraded,
                            stopwatch.Elapsed,
                            "RedirectWithoutLocation");
                    }

                    currentUri = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(currentUri, response.Headers.Location);

                    PublicEndpointPolicy.ValidateUri(currentUri);
                    continue;
                }

                var statusCode = (int)response.StatusCode;
                if (statusCode is >= 200 and <= 299)
                {
                    return CreateHealth(
                        resourceId,
                        ResourceHealthState.Healthy,
                        stopwatch.Elapsed,
                        null);
                }

                if (statusCode is 401 or 403 or 408 or 429)
                {
                    return CreateHealth(
                        resourceId,
                        ResourceHealthState.Degraded,
                        stopwatch.Elapsed,
                        $"Http{statusCode}");
                }

                return CreateHealth(
                    resourceId,
                    statusCode >= 500
                        ? ResourceHealthState.Unavailable
                        : ResourceHealthState.Degraded,
                    stopwatch.Elapsed,
                    $"Http{statusCode}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                "Timeout");
        }
        catch (HealthCheckSecurityException)
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                "EndpointRejected");
        }
        catch (HttpRequestException exception) when (ContainsSecurityException(exception))
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                "EndpointRejected");
        }
        catch (HttpRequestException exception)
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                PublicHealthDiagnosticClassifier.Classify(exception));
        }

        return CreateHealth(
            resourceId,
            ResourceHealthState.Unavailable,
            stopwatch.Elapsed,
            "HealthCheckFailed");
    }

    private static HttpClient CreateSecureHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            MaxConnectionsPerServer = 2,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectPublicEndpointAsync
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    private static async ValueTask<Stream> ConnectPublicEndpointAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.DnsEndPoint.Port != 443)
        {
            throw new HealthCheckSecurityException(
                "NonStandardPortRejected",
                "Health-check transport разрешает соединения только с HTTPS-портом 443.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(
                context.DnsEndPoint.Host,
                cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new HttpRequestException("DNS health-check endpoint недоступен.", exception);
        }

        PublicEndpointPolicy.EnsureAllResolvedAddressesArePublic(addresses);

        Exception? lastException = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                lastException = exception;
                socket.Dispose();

                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }
        }

        throw new HttpRequestException(
            "Не удалось подключиться ни к одному публичному адресу health-check endpoint.",
            lastException);
    }

    private static bool ContainsSecurityException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is HealthCheckSecurityException)
            {
                return true;
            }
        }

        return false;
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
        new(
            resourceId,
            state,
            DateTimeOffset.UtcNow,
            latency,
            diagnosticCode);
}
