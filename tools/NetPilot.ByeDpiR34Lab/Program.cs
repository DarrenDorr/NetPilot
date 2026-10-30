using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using NetPilot.Engine.Abstractions;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiR34Lab;

internal static class Program
{
    private const string SuccessMarker = "NETPILOT_R34_OK";

    public static async Task<int> Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R34 lab is Windows-only.");
            return 2;
        }

        if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess)
        {
            Console.Error.WriteLine("R34 lab requires a Windows x64 process on Windows x64.");
            return 2;
        }

        if (Environment.GetCommandLineArgs().Length != 1)
        {
            Console.Error.WriteLine("R34 lab accepts no command-line arguments.");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = timeout.Token;

        try
        {
            var proxyPort = ReserveFreeLoopbackPort();
            var httpPort = ReserveDistinctFreeLoopbackPort(proxyPort);
            var contract = ByeDpiR34LabContract.Create(checked((ushort)proxyPort));

            if (BuiltInEngineArgumentPlanCatalog.All.Count != 0
                || BuiltInEngineExecutableIdentityCatalog.All.Count != 0
                || BuiltInEnginePackageLayoutCatalog.All.Count != 0)
            {
                throw new InvalidOperationException(
                    "Production engine catalogs unexpectedly contain admitted runtime material.");
            }

            Console.WriteLine($"LAB_PROFILE={contract.LaunchIdentityBinding.ProfileId.Value}");
            Console.WriteLine($"PROXY_PORT={proxyPort}");
            Console.WriteLine($"HTTP_PORT={httpPort}");
            Console.WriteLine($"CANDIDATE_SHA256={ByeDpiR34LabContract.CandidateSha256}");
            Console.WriteLine("PRODUCTION_CATALOGS=EMPTY");

            using var verifiedPackage = WindowsImmutableEnginePackageVerifier.Verify(
                contract.LaunchIdentityBinding,
                contract.PackageLayout);

            var securityVerification = WindowsProtectedEngineSecurityVerifier.Verify(
                verifiedPackage);
            Console.WriteLine($"ACL_OBJECTS_VERIFIED={securityVerification.VerifiedObjectCount}");

            var handoff = WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                contract.LaunchIdentityBinding,
                verifiedPackage,
                securityVerification);

            var compiledPlan = WindowsTypedEngineArgumentPlanCompiler.Compile(
                handoff,
                contract.ArgumentPlan);

            var expectedArguments = new[]
            {
                "-i", "127.0.0.1",
                "-p", proxyPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "-N", "-U", "-c", "8", "-x", "0"
            };

            if (!compiledPlan.Arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Compiled R34 argv drifted from the exact lab contract.");
            }

            Console.WriteLine("TYPED_ARGV=PASS");

            await using var controller = new WindowsByeDpiServiceOwnedProcessController(
                new SystemZapretProcessLauncher());
            await using var httpServer = new LoopbackHttpServer(httpPort);

            await RunFunctionalCycleAsync(
                controller,
                compiledPlan,
                httpServer,
                proxyPort,
                httpPort,
                "ROUND1",
                cancellationToken);

            await RunFunctionalCycleAsync(
                controller,
                compiledPlan,
                httpServer,
                proxyPort,
                httpPort,
                "ROUND2",
                cancellationToken);

            var crashStart = await controller.StartAsync(compiledPlan, cancellationToken);
            RequireRunning(crashStart, "CRASH_START");
            await WaitForLoopbackListenerAsync(proxyPort, cancellationToken);

            var crashPid = crashStart.ProcessId
                ?? throw new InvalidOperationException("Crash cycle did not expose the owned process id.");

            using (var crashedProcess = Process.GetProcessById(crashPid))
            {
                crashedProcess.Kill(entireProcessTree: true);
                await crashedProcess.WaitForExitAsync(cancellationToken);
            }

            var reconciled = await controller.GetStatusAsync(cancellationToken);
            if (reconciled.State != WindowsServiceOwnedEngineProcessState.Stopped
                || reconciled.ProcessId is not null)
            {
                throw new InvalidOperationException(
                    "Controller did not reconcile an externally terminated owned process to Stopped.");
            }

            await WaitForNoListenerAsync(proxyPort, cancellationToken);
            Console.WriteLine("CRASH_RECONCILE=PASS");

            var final = await controller.GetStatusAsync(cancellationToken);
            if (final.State != WindowsServiceOwnedEngineProcessState.Stopped)
            {
                throw new InvalidOperationException("R34 final controller state is not Stopped.");
            }

            Console.WriteLine("FINAL_STATE=STOPPED");
            Console.WriteLine("SYSTEM_PROXY_MUTATION=NOT_REQUESTED");
            Console.WriteLine("WINDIVERT=NOT_REQUESTED");
            Console.WriteLine("PRODUCTION_ADMISSION=BLOCK");
            Console.WriteLine("RESULT=BYEDPI_SERVICE_OWNED_FUNCTIONAL_R34_PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"R34_FAILURE_TYPE={exception.GetType().Name}");
            Console.Error.WriteLine($"R34_FAILURE_MESSAGE={exception.Message}");
            Console.Error.WriteLine("RESULT=BYEDPI_SERVICE_OWNED_FUNCTIONAL_R34_FAIL");
            return 1;
        }
    }

    private static async Task RunFunctionalCycleAsync(
        WindowsByeDpiServiceOwnedProcessController controller,
        WindowsCompiledEngineArgumentPlan compiledPlan,
        LoopbackHttpServer httpServer,
        int proxyPort,
        int httpPort,
        string round,
        CancellationToken cancellationToken)
    {
        var started = await controller.StartAsync(compiledPlan, cancellationToken);
        RequireRunning(started, $"{round}_START");
        await WaitForLoopbackListenerAsync(proxyPort, cancellationToken);

        var serverTask = httpServer.ServeOnceAsync(SuccessMarker, cancellationToken);
        var response = await SocksHttpRoundTripAsync(
            proxyPort,
            httpPort,
            cancellationToken);
        var request = await serverTask;

        if (!response.Contains(SuccessMarker, StringComparison.Ordinal)
            || !response.StartsWith("HTTP/1.1 200", StringComparison.Ordinal)
            || !request.StartsWith("GET /r34 HTTP/1.1\r\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{round} did not complete the synthetic SOCKS/HTTP path.");
        }

        Console.WriteLine($"{round}_SOCKS_HTTP=PASS");

        var stopped = await controller.StopAsync(cancellationToken);
        if (stopped.State != WindowsServiceOwnedEngineProcessState.Stopped
            || stopped.ProcessId is not null)
        {
            throw new InvalidOperationException($"{round} did not stop cleanly.");
        }

        await WaitForNoListenerAsync(proxyPort, cancellationToken);
        Console.WriteLine($"{round}_CLEANUP=PASS");
    }

    private static void RequireRunning(
        WindowsServiceOwnedEngineProcessSnapshot snapshot,
        string stage)
    {
        if (snapshot.State != WindowsServiceOwnedEngineProcessState.Running
            || snapshot.ProcessId is null)
        {
            throw new InvalidOperationException($"{stage} did not enter Running state.");
        }
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
            throw new InvalidOperationException(
                $"Unexpected SOCKS5 greeting response: {Convert.ToHexString(greeting)}");
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
            throw new InvalidOperationException(
                $"SOCKS5 CONNECT failed: {Convert.ToHexString(replyHeader)}");
        }

        await ConsumeSocksBindAddressAsync(stream, replyHeader[3], cancellationToken);

        var request = Encoding.ASCII.GetBytes(
            "GET /r34 HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n");
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
            if (response.Length > 64 * 1024)
            {
                throw new InvalidOperationException("Synthetic HTTP response exceeded R34 bound.");
            }
        }

        return Encoding.ASCII.GetString(response.ToArray());
    }

    private static async Task ConsumeSocksBindAddressAsync(
        NetworkStream stream,
        byte addressType,
        CancellationToken cancellationToken)
    {
        var addressLength = addressType switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => -1,
            _ => throw new InvalidOperationException($"Unknown SOCKS5 address type: {addressType}")
        };

        if (addressLength == -1)
        {
            var length = new byte[1];
            await stream.ReadExactlyAsync(length, cancellationToken);
            addressLength = length[0];
        }

        var remainder = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(remainder, cancellationToken);
    }

    private static async Task WaitForLoopbackListenerAsync(
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var endpoints = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Where(endpoint => endpoint.Port == port)
                .ToArray();

            if (endpoints.Length > 0)
            {
                if (endpoints.Any(endpoint => !IPAddress.IsLoopback(endpoint.Address)))
                {
                    throw new InvalidOperationException(
                        "R34 proxy listener escaped the loopback boundary.");
                }

                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException("R34 proxy listener did not become ready.");
    }

    private static async Task WaitForNoListenerAsync(
        int port,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port);

            if (!exists)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException("R34 proxy listener remained after cleanup.");
    }

    private static int ReserveDistinctFreeLoopbackPort(int excludedPort)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var port = ReserveFreeLoopbackPort();
            if (port != excludedPort)
            {
                return port;
            }
        }

        throw new InvalidOperationException("Unable to reserve two distinct loopback ports.");
    }

    private static int ReserveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start(1);
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class LoopbackHttpServer : IAsyncDisposable
    {
        private readonly TcpListener listener;

        public LoopbackHttpServer(int port)
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start(4);
        }

        public async Task<string> ServeOnceAsync(
            string marker,
            CancellationToken cancellationToken)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            await using var stream = client.GetStream();

            using var requestBytes = new MemoryStream();
            var buffer = new byte[1024];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                requestBytes.Write(buffer, 0, read);
                if (requestBytes.Length > 16 * 1024)
                {
                    throw new InvalidOperationException("Synthetic HTTP request exceeded R34 bound.");
                }

                var requestText = Encoding.ASCII.GetString(requestBytes.ToArray());
                if (requestText.Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var body = Encoding.ASCII.GetBytes(marker);
                    var headers = Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\nContent-Type: text/plain\r\n\r\n");
                    await stream.WriteAsync(headers, cancellationToken);
                    await stream.WriteAsync(body, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                    return requestText;
                }
            }

            throw new InvalidOperationException("Synthetic HTTP client disconnected before headers completed.");
        }

        public ValueTask DisposeAsync()
        {
            listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
