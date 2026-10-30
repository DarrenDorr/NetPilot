using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiR36LabClient;

internal static class Program
{
    private const int ProxyPort = 39036;
    private const string SuccessMarker = "NETPILOT_R36_OK";
    private const string StartForServiceStopMode = "--start-for-service-stop";
    private const string StatusOnlyMode = "--status-only";

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R36 lab client is Windows-only.");
            return 2;
        }

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            Console.Error.WriteLine("R36 lab client must run in an interactive Windows session.");
            return 2;
        }

        var mode = ParseMode(args);
        if (mode is null)
        {
            Console.Error.WriteLine("R36 lab client accepts no arguments or one fixed lab mode.");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var cancellationToken = timeout.Token;
        var client = new WindowsPrivilegedServiceClient();

        try
        {
            await WaitForServiceAsync(client, cancellationToken);
            await CompleteEnrollmentIfNeededAsync(client, cancellationToken);

            var ping = await client.PingAsync(cancellationToken);
            RequireSuccess(ping, "PING");
            Console.WriteLine("AUTHENTICATED_IPC=PASS");

            if (mode == StatusOnlyMode)
            {
                var statusOnly = await client.GetByeDpiStatusAsync(cancellationToken);
                RequireSuccess(statusOnly, "STATUS_ONLY");
                Console.WriteLine($"BYEDPI_STATUS={RequireStatus(statusOnly)}");
                Console.WriteLine("RESULT=R36_STATUS_PASS");
                return 0;
            }

            var initial = await client.GetByeDpiStatusAsync(cancellationToken);
            RequireSuccess(initial, "INITIAL_STATUS");
            if (RequireStatus(initial) != IpcByeDpiProcessState.Stopped)
            {
                throw new InvalidOperationException("R36 expected ByeDPI to be stopped before client start.");
            }

            var started = await client.StartByeDpiAsync(cancellationToken);
            RequireSuccess(started, "START");
            if (RequireStatus(started) != IpcByeDpiProcessState.Running)
            {
                throw new InvalidOperationException("R36 IPC start did not report Running.");
            }

            await WaitForLoopbackListenerAsync(ProxyPort, cancellationToken);
            Console.WriteLine("IPC_START=PASS");

            if (mode == StartForServiceStopMode)
            {
                Console.WriteLine("START_FOR_SERVICE_STOP=PASS");
                Console.WriteLine("RESULT=R36_START_FOR_SERVICE_STOP_PASS");
                return 0;
            }

            var httpPort = ReserveDistinctLoopbackPort(ProxyPort);
            await using var httpServer = new LoopbackHttpServer(httpPort);
            var serverTask = httpServer.ServeOnceAsync(SuccessMarker, cancellationToken);
            var response = await SocksHttpRoundTripAsync(ProxyPort, httpPort, cancellationToken);
            var request = await serverTask;

            if (!response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal)
                || !response.Contains(SuccessMarker, StringComparison.Ordinal)
                || !request.StartsWith("GET /r36 HTTP/1.1\r\n", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("R36 IPC-controlled SOCKS/HTTP path failed.");
            }

            Console.WriteLine("IPC_SOCKS_HTTP=PASS");

            var stopped = await client.StopByeDpiAsync(cancellationToken);
            RequireSuccess(stopped, "STOP");
            if (RequireStatus(stopped) != IpcByeDpiProcessState.Stopped)
            {
                throw new InvalidOperationException("R36 IPC stop did not report Stopped.");
            }

            await WaitForNoListenerAsync(ProxyPort, cancellationToken);
            Console.WriteLine("IPC_STOP_CLEANUP=PASS");
            Console.WriteLine("RESULT=BYEDPI_LOCALSYSTEM_IPC_R36_PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"R36_FAILURE_TYPE={exception.GetType().Name}");
            Console.Error.WriteLine($"R36_FAILURE_MESSAGE={exception.Message}");
            Console.Error.WriteLine("RESULT=BYEDPI_LOCALSYSTEM_IPC_R36_FAIL");
            return 1;
        }
    }

    private static string? ParseMode(string[] args)
    {
        if (args.Length == 0)
        {
            return string.Empty;
        }

        if (args.Length == 1
            && (string.Equals(args[0], StartForServiceStopMode, StringComparison.Ordinal)
                || string.Equals(args[0], StatusOnlyMode, StringComparison.Ordinal)))
        {
            return args[0];
        }

        return null;
    }

    private static async Task WaitForServiceAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var response = await client.PingAsync(cancellationToken);
                if (response.Success || response.Code == "EnrollmentRequired")
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is IpcProtocolException or IOException)
            {
                last = exception;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new InvalidOperationException("R36 lab service did not become reachable.", last);
    }

    private static async Task CompleteEnrollmentIfNeededAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        var ping = await client.PingAsync(cancellationToken);
        if (ping.Success)
        {
            return;
        }

        if (!string.Equals(ping.Code, "EnrollmentRequired", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected pre-enrollment response: {ping.Code}.");
        }

        var noncePath = Path.Combine(
            WindowsZapretEngineLocation.GetDefaultInstallationRoot(),
            "Config",
            "enrollment.nonce");

        if (!File.Exists(noncePath))
        {
            throw new FileNotFoundException("R36 enrollment nonce was not created by LocalSystem service.", noncePath);
        }

        var nonce = File.ReadAllText(noncePath).Trim();
        var enrollment = await client.EnrollAuthorizedUserAsync(nonce, cancellationToken);
        RequireSuccess(enrollment, "ENROLLMENT");
        Console.WriteLine("ENROLLMENT=PASS");
    }

    private static IpcByeDpiProcessState RequireStatus(IpcResponse response) =>
        response.ByeDpiStatus?.State
        ?? throw new InvalidOperationException("ByeDPI IPC response has no status payload.");

    private static void RequireSuccess(IpcResponse response, string stage)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException($"{stage} failed: {response.Code}: {response.Message}");
        }
    }

    private static int ReserveDistinctLoopbackPort(int excludedPort)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                if (port != excludedPort)
                {
                    return port;
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        throw new InvalidOperationException("Unable to reserve distinct loopback HTTP port.");
    }

    private static async Task<string> SocksHttpRoundTripAsync(
        int proxyPort,
        int targetPort,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, proxyPort, cancellationToken);
        await using var stream = client.GetStream();

        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, cancellationToken);
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, cancellationToken);
        if (greeting[0] != 0x05 || greeting[1] != 0x00)
        {
            throw new InvalidOperationException("Unexpected SOCKS5 greeting response.");
        }

        var connect = new byte[]
        {
            0x05, 0x01, 0x00, 0x01,
            127, 0, 0, 1,
            checked((byte)(targetPort >> 8)),
            checked((byte)(targetPort & 0xff))
        };
        await stream.WriteAsync(connect, cancellationToken);

        var replyHeader = new byte[4];
        await stream.ReadExactlyAsync(replyHeader, cancellationToken);
        if (replyHeader[0] != 0x05 || replyHeader[1] != 0x00 || replyHeader[2] != 0x00)
        {
            throw new InvalidOperationException("SOCKS5 CONNECT failed.");
        }

        await ConsumeBindAddressAsync(stream, replyHeader[3], cancellationToken);

        var request = Encoding.ASCII.GetBytes(
            "GET /r36 HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(request, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        using var response = new MemoryStream();
        var buffer = new byte[2048];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            response.Write(buffer, 0, read);
        }

        return Encoding.ASCII.GetString(response.ToArray());
    }

    private static async Task ConsumeBindAddressAsync(
        NetworkStream stream,
        byte addressType,
        CancellationToken cancellationToken)
    {
        var addressLength = addressType switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadDomainLengthAsync(stream, cancellationToken),
            _ => throw new InvalidOperationException("Unknown SOCKS5 bind address type.")
        };

        var remaining = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(remaining, cancellationToken);
    }

    private static async Task<int> ReadDomainLengthAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var length = new byte[1];
        await stream.ReadExactlyAsync(length, cancellationToken);
        return length[0];
    }

    private static async Task WaitForLoopbackListenerAsync(
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        throw new InvalidOperationException("ByeDPI loopback listener did not appear.");
    }

    private static async Task WaitForNoListenerAsync(
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 80; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var client = new TcpClient(AddressFamily.InterNetwork);
                await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            }
            catch (SocketException)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException("ByeDPI loopback listener remained after stop.");
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;

        public LoopbackHttpServer(int port)
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start(1);
        }

        public async Task<string> ServeOnceAsync(
            string responseMarker,
            CancellationToken cancellationToken)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();
            var buffer = new byte[4096];
            var request = new MemoryStream();

            while (request.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                request.Write(buffer, 0, read);
                var text = Encoding.ASCII.GetString(request.ToArray());
                if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            var requestText = Encoding.ASCII.GetString(request.ToArray());
            var body = Encoding.ASCII.GetBytes(responseMarker);
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(body, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return requestText;
        }

        public ValueTask DisposeAsync()
        {
            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
