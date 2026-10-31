using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPilot.Core.Models;

namespace NetPilot.Health;

/// <summary>
/// Experimental, fixed-loopback health transport used only by the ByeDPI alpha build.
/// Public endpoint validation is performed before the SOCKS5 CONNECT request and the
/// proxy endpoint is fixed to IPv4 loopback. This is not a generic user-configurable proxy.
/// </summary>
public sealed class ExperimentalByeDpiHttpsHealthChecker : IResourceHealthChecker
{
    private const int MaxRedirects = 3;
    private const int ByeDpiPort = 39036;
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public ExperimentalByeDpiHttpsHealthChecker()
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
            ConnectCallback = ConnectViaByeDpiAsync
        };

        _httpClient = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public async Task<ResourceHealth> CheckAsync(
        ResourceDefinition resource,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.HealthCheckUris.Count == 0)
        {
            return CreateHealth(resource.Id, ResourceHealthState.Unavailable, null, "NoHealthEndpoints");
        }

        foreach (var uri in resource.HealthCheckUris)
        {
            try
            {
                PublicEndpointPolicy.ValidateUri(uri);
            }
            catch (HealthCheckSecurityException)
            {
                return CreateHealth(resource.Id, ResourceHealthState.Unavailable, null, "EndpointRejected");
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
        _httpClient.Dispose();
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
                request.Headers.UserAgent.ParseAdd("NetPilot-ByeDPI-HealthCheck/1.0");
                request.Headers.Accept.ParseAdd("*/*");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirectCount == MaxRedirects)
                    {
                        return CreateHealth(resourceId, ResourceHealthState.Degraded, stopwatch.Elapsed, "TooManyRedirects");
                    }

                    if (response.Headers.Location is null)
                    {
                        return CreateHealth(resourceId, ResourceHealthState.Degraded, stopwatch.Elapsed, "RedirectWithoutLocation");
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
                    return CreateHealth(resourceId, ResourceHealthState.Healthy, stopwatch.Elapsed, null);
                }

                if (statusCode is 401 or 403 or 408 or 429)
                {
                    return CreateHealth(resourceId, ResourceHealthState.Degraded, stopwatch.Elapsed, $"Http{statusCode}");
                }

                return CreateHealth(
                    resourceId,
                    statusCode >= 500 ? ResourceHealthState.Unavailable : ResourceHealthState.Degraded,
                    stopwatch.Elapsed,
                    $"Http{statusCode}");
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "Timeout");
        }
        catch (HealthCheckSecurityException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "EndpointRejected");
        }
        catch (HttpRequestException exception) when (ContainsSecurityException(exception))
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "EndpointRejected");
        }
        catch (HttpRequestException exception)
        {
            return CreateHealth(
                resourceId,
                ResourceHealthState.Unavailable,
                stopwatch.Elapsed,
                PublicHealthDiagnosticClassifier.Classify(exception));
        }
        catch (IOException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyTransportFailure");
        }
        catch (SocketException)
        {
            return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "ProxyTransportFailure");
        }

        return CreateHealth(resourceId, ResourceHealthState.Unavailable, stopwatch.Elapsed, "HealthCheckFailed");
    }

    private static async ValueTask<Stream> ConnectViaByeDpiAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        if (context.DnsEndPoint.Port != 443)
        {
            throw new HealthCheckSecurityException(
                "NonStandardPortRejected",
                "ByeDPI health-check разрешает соединения только с HTTPS-портом 443.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        }
        catch (SocketException exception)
        {
            throw new HttpRequestException("DNS health-check endpoint недоступен.", exception);
        }

        PublicEndpointPolicy.EnsureAllResolvedAddressesArePublic(addresses);
        var target = addresses.FirstOrDefault(static address => address.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses.First();

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true
        };

        try
        {
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, ByeDpiPort), cancellationToken);
            var stream = new NetworkStream(socket, ownsSocket: true);
            await PerformSocks5HandshakeAsync(stream, target, 443, cancellationToken);
            return stream;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task PerformSocks5HandshakeAsync(
        Stream stream,
        IPAddress targetAddress,
        ushort targetPort,
        CancellationToken cancellationToken)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cancellationToken);
        var greeting = new byte[2];
        await ReadExactlyAsync(stream, greeting, cancellationToken);
        if (greeting[0] != 0x05 || greeting[1] != 0x00)
        {
            throw new IOException("ByeDPI SOCKS5 proxy rejected no-authentication negotiation.");
        }

        var addressBytes = targetAddress.GetAddressBytes();
        var atyp = targetAddress.AddressFamily switch
        {
            AddressFamily.InterNetwork => (byte)0x01,
            AddressFamily.InterNetworkV6 => (byte)0x04,
            _ => throw new HealthCheckSecurityException("UnsupportedAddressFamily", "Неподдерживаемое семейство адресов.")
        };

        var request = new byte[4 + addressBytes.Length + 2];
        request[0] = 0x05;
        request[1] = 0x01;
        request[2] = 0x00;
        request[3] = atyp;
        addressBytes.CopyTo(request, 4);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(4 + addressBytes.Length, 2), targetPort);
        await stream.WriteAsync(request, cancellationToken);

        var header = new byte[4];
        await ReadExactlyAsync(stream, header, cancellationToken);
        if (header[0] != 0x05 || header[1] != 0x00 || header[2] != 0x00)
        {
            throw new IOException($"ByeDPI SOCKS5 CONNECT failed with code 0x{header[1]:X2}.");
        }

        var bindLength = header[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadDomainLengthAsync(stream, cancellationToken),
            _ => throw new IOException("ByeDPI SOCKS5 returned an invalid address type.")
        };

        var discard = new byte[bindLength + 2];
        await ReadExactlyAsync(stream, discard, cancellationToken);
    }

    private static async Task<int> ReadDomainLengthAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[1];
        await ReadExactlyAsync(stream, length, cancellationToken);
        return length[0];
    }

    private static async Task ReadExactlyAsync(
        Stream stream,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer[read..], cancellationToken);
            if (count == 0)
            {
                throw new IOException("ByeDPI SOCKS5 proxy closed the connection during handshake.");
            }

            read += count;
        }
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
        new(resourceId, state, DateTimeOffset.UtcNow, latency, diagnosticCode);
}
