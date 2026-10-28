using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;

namespace NetPilot.Health;

/// <summary>
/// Классифицирует уже возникшую ошибку HTTPS health-check без дополнительных
/// сетевых запросов и без чтения пользовательского трафика.
/// </summary>
public static class PublicHealthDiagnosticClassifier
{
    public const string DnsResolutionFailed = "DnsResolutionFailed";
    public const string TcpConnectFailed = "TcpConnectFailed";
    public const string TlsHandshakeFailed = "TlsHandshakeFailed";
    public const string NetworkError = "NetworkError";

    public static string Classify(HttpRequestException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var byHttpError = exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError => DnsResolutionFailed,
            HttpRequestError.ConnectionError => TcpConnectFailed,
            HttpRequestError.SecureConnectionError => TlsHandshakeFailed,
            _ => null
        };

        if (byHttpError is not null)
        {
            return byHttpError;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return TlsHandshakeFailed;
            }

            if (current is SocketException socketException)
            {
                return socketException.SocketErrorCode switch
                {
                    SocketError.HostNotFound
                        or SocketError.NoData
                        or SocketError.TryAgain => DnsResolutionFailed,
                    SocketError.ConnectionRefused
                        or SocketError.NetworkDown
                        or SocketError.NetworkUnreachable
                        or SocketError.HostDown
                        or SocketError.HostUnreachable
                        or SocketError.TimedOut
                        or SocketError.ConnectionAborted
                        or SocketError.ConnectionReset => TcpConnectFailed,
                    _ => NetworkError
                };
            }
        }

        return NetworkError;
    }
}
