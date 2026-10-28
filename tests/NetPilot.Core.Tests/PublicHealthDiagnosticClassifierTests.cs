using System.Net.Http;
using System.Net.Sockets;
using System.Security.Authentication;
using NetPilot.Health;

namespace NetPilot.Core.Tests;

public sealed class PublicHealthDiagnosticClassifierTests
{
    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, PublicHealthDiagnosticClassifier.DnsResolutionFailed)]
    [InlineData(HttpRequestError.ConnectionError, PublicHealthDiagnosticClassifier.TcpConnectFailed)]
    [InlineData(HttpRequestError.SecureConnectionError, PublicHealthDiagnosticClassifier.TlsHandshakeFailed)]
    public void Known_http_request_error_is_classified(
        HttpRequestError error,
        string expectedCode)
    {
        var exception = new HttpRequestException(error, "probe failed");

        Assert.Equal(expectedCode, PublicHealthDiagnosticClassifier.Classify(exception));
    }

    [Fact]
    public void Authentication_exception_is_classified_as_tls_failure()
    {
        var exception = new HttpRequestException(
            "TLS failed",
            new AuthenticationException("certificate/handshake failure"));

        Assert.Equal(
            PublicHealthDiagnosticClassifier.TlsHandshakeFailed,
            PublicHealthDiagnosticClassifier.Classify(exception));
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, PublicHealthDiagnosticClassifier.DnsResolutionFailed)]
    [InlineData(SocketError.TryAgain, PublicHealthDiagnosticClassifier.DnsResolutionFailed)]
    [InlineData(SocketError.ConnectionRefused, PublicHealthDiagnosticClassifier.TcpConnectFailed)]
    [InlineData(SocketError.NetworkUnreachable, PublicHealthDiagnosticClassifier.TcpConnectFailed)]
    [InlineData(SocketError.TimedOut, PublicHealthDiagnosticClassifier.TcpConnectFailed)]
    public void Nested_socket_error_is_classified(
        SocketError socketError,
        string expectedCode)
    {
        var exception = new HttpRequestException(
            "socket failed",
            new SocketException((int)socketError));

        Assert.Equal(expectedCode, PublicHealthDiagnosticClassifier.Classify(exception));
    }

    [Fact]
    public void Unknown_error_remains_generic_network_error()
    {
        var exception = new HttpRequestException("unknown failure");

        Assert.Equal(
            PublicHealthDiagnosticClassifier.NetworkError,
            PublicHealthDiagnosticClassifier.Classify(exception));
    }
}
