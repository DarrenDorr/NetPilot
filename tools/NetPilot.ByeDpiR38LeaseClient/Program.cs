using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiR38LeaseClient;

internal static class Program
{
    private const int ProxyPort = 39036;
    private const string NonOwnerMode = "--expect-nonowner-denied";
    private const string OwnerStopMode = "--owner-stop";

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R38 lease client is Windows-only.");
            return 2;
        }

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            Console.Error.WriteLine("R38 lease client requires a non-zero interactive Windows session.");
            return 2;
        }

        if (args.Length != 1
            || (args[0] != NonOwnerMode && args[0] != OwnerStopMode))
        {
            Console.Error.WriteLine("R38 lease client accepts one fixed lab mode only.");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var client = new WindowsPrivilegedServiceClient();

        try
        {
            var ping = await client.PingAsync(cancellationToken);
            RequireSuccess(ping, "PING");
            Console.WriteLine("R38_AUTHENTICATED_IPC=PASS");
            Console.WriteLine("R38_SESSION_NONZERO=True");

            var status = await client.GetByeDpiStatusAsync(cancellationToken);
            RequireSuccess(status, "STATUS");
            if (RequireStatus(status) != IpcByeDpiProcessState.Running)
            {
                throw new InvalidOperationException("R38 expected active ByeDPI before lease probe.");
            }
            Console.WriteLine("R38_OBSERVED_RUNNING=PASS");

            if (args[0] == NonOwnerMode)
            {
                var start = await client.StartByeDpiAsync(cancellationToken);
                RequireDeniedByOwner(start, "START");
                Console.WriteLine("R38_NONOWNER_START_DENIED=PASS");

                var stop = await client.StopByeDpiAsync(cancellationToken);
                RequireDeniedByOwner(stop, "STOP");
                Console.WriteLine("R38_NONOWNER_STOP_DENIED=PASS");

                var after = await client.GetByeDpiStatusAsync(cancellationToken);
                RequireSuccess(after, "STATUS_AFTER_DENIAL");
                if (RequireStatus(after) != IpcByeDpiProcessState.Running)
                {
                    throw new InvalidOperationException("R38 non-owner denial changed active engine state.");
                }

                Console.WriteLine("R38_LEASE_PRESERVED_AFTER_DENIAL=PASS");
                Console.WriteLine("RESULT=R38_NONOWNER_DENIAL_PASS");
                return 0;
            }

            var stopped = await client.StopByeDpiAsync(cancellationToken);
            RequireSuccess(stopped, "OWNER_STOP");
            if (RequireStatus(stopped) != IpcByeDpiProcessState.Stopped)
            {
                throw new InvalidOperationException("R38 owner stop did not return Stopped.");
            }

            await WaitForNoListenerAsync(ProxyPort, cancellationToken);
            Console.WriteLine("R38_OWNER_STOP=PASS");
            Console.WriteLine("R38_OWNER_STOP_CLEANUP=PASS");
            Console.WriteLine("RESULT=R38_OWNER_STOP_PASS");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"R38_FAILURE_TYPE={exception.GetType().Name}");
            Console.Error.WriteLine($"R38_FAILURE_MESSAGE={exception.Message}");
            Console.Error.WriteLine("RESULT=R38_LEASE_TEST_FAIL");
            return 1;
        }
    }

    private static void RequireDeniedByOwner(IpcResponse response, string stage)
    {
        if (response.Success
            || !string.Equals(response.Code, "EngineOwnedByAnotherUser", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{stage} expected EngineOwnedByAnotherUser but received {response.Code} / success={response.Success}.");
        }
    }

    private static IpcByeDpiProcessState RequireStatus(IpcResponse response) =>
        response.ByeDpiStatus?.State
        ?? throw new InvalidOperationException("R38 ByeDPI response has no status payload.");

    private static void RequireSuccess(IpcResponse response, string stage)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException($"{stage} failed: {response.Code}: {response.Message}");
        }
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
                using var probe = new TcpClient(AddressFamily.InterNetwork);
                await probe.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            }
            catch (SocketException)
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException("R38 proxy listener remained after owner stop.");
    }
}
