using System.Net;

namespace NetPilot.Updater;

/// <summary>
/// R42.13 hardened live-network handler factory for the separately gated ByeDPI acquisition lab.
///
/// The handler intentionally does not follow redirects, use cookies, use credentials, use an
/// ambient proxy or transparently decompress content. Redirect policy remains exclusively in
/// <see cref="ByeDpiIsolatedHttpTransport"/> and <see cref="ByeDpiAcquisitionTrustPolicy"/>.
///
/// Creating this handler is not production Service admission. R42.13 guards require that the
/// normal NetPilot.Service DI continues to use NoAdmittedWindowsByeDpiPackageProvisioner.
/// </summary>
public static class ByeDpiLiveHttpHandlerFactory
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30);
    public const int MaxConnectionsPerServer = 2;

    public static SocketsHttpHandler Create() =>
        new()
        {
            AllowAutoRedirect = false,
            MaxAutomaticRedirections = ByeDpiAcquisitionTrustPolicy.MaxRedirects,
            UseCookies = false,
            UseProxy = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = ConnectTimeout,
            PooledConnectionLifetime = PooledConnectionLifetime,
            PooledConnectionIdleTimeout = PooledConnectionIdleTimeout,
            MaxConnectionsPerServer = MaxConnectionsPerServer,
            ActivityHeadersPropagator = null
        };
}
