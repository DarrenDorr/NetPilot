using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiR39RestartClient;

internal static class Program
{
    private const int ProxyPort = 39036;
    private const string ExpectStoppedMode = "--expect-stopped-after-restart";
    private const string StartFreshOwnerMode = "--start-fresh-owner";
    private const string ExpectNonOwnerDeniedMode = "--expect-nonowner-denied";
    private const string FreshOwnerStopMode = "--fresh-owner-stop";

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("R39 restart client is Windows-only.");
            return 2;
        }

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            Console.Error.WriteLine("R39 restart client requires a non-zero interactive Windows session.");
            return 2;
        }

        if (args.Length != 1 || !IsFixedMode(args[0]))
        {
            Console.Error.WriteLine("R39 restart client accepts one fixed lab mode only.");
            return 2;
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellationToken = timeout.Token;
        var client = new WindowsPrivilegedServiceClient();

        try
        {
            var ping = await client.PingAsync(cancellationToken);
            RequireSuccess(ping, "PING");
            Console.WriteLine("R39_AUTHENTICATED_IPC=PASS");
            Console.WriteLine("R39_SESSION_NONZERO=True");

            return args[0] switch
            {
                ExpectStoppedMode => await ExpectStoppedAfterRestartAsync(client, cancellationToken),
                StartFreshOwnerMode => await StartFreshOwnerAsync(client, cancellationToken),
                ExpectNonOwnerDeniedMode => await ExpectNonOwnerDeniedAsync(client, cancellationToken),
                FreshOwnerStopMode => await FreshOwnerStopAsync(client, cancellationToken),
                _ => throw new InvalidOperationException("Unexpected R39 fixed mode.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"R39_FAILURE_TYPE={exception.GetType().Name}");
            Console.Error.WriteLine($"R39_FAILURE_MESSAGE={exception.Message}");
            Console.Error.WriteLine("RESULT=R39_RESTART_RECONCILIATION_FAIL");
            return 1;
        }
    }

    private static async Task<int> ExpectStoppedAfterRestartAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        var status = await client.GetByeDpiStatusAsync(cancellationToken);
        RequireSuccess(status, "POST_RESTART_STATUS");
        if (RequireStatus(status) != IpcByeDpiProcessState.Stopped)
        {
            throw new InvalidOperationException("R39 service restart did not reconcile ByeDPI to Stopped.");
        }

        await WaitForNoListenerAsync(ProxyPort, cancellationToken);
        Console.WriteLine("R39_POST_RESTART_STATUS_STOPPED=PASS");
        Console.WriteLine("R39_POST_RESTART_NO_LISTENER=PASS");
        Console.WriteLine("RESULT=R39_POST_RESTART_STOPPED_PASS");
        return 0;
    }

    private static async Task<int> StartFreshOwnerAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        var before = await client.GetByeDpiStatusAsync(cancellationToken);
        RequireSuccess(before, "FRESH_OWNER_STATUS_BEFORE_START");
        if (RequireStatus(before) != IpcByeDpiProcessState.Stopped)
        {
            throw new InvalidOperationException("R39 fresh owner expected Stopped before new acquisition.");
        }

        var started = await client.StartByeDpiAsync(cancellationToken);
        RequireSuccess(started, "FRESH_OWNER_START");
        if (RequireStatus(started) != IpcByeDpiProcessState.Running)
        {
            throw new InvalidOperationException("R39 fresh owner start did not return Running.");
        }

        await WaitForListenerAsync(ProxyPort, cancellationToken);
        Console.WriteLine("R39_FRESH_OWNER_START=PASS");
        Console.WriteLine("R39_FRESH_OWNER_LISTENER=PASS");
        Console.WriteLine("RESULT=R39_FRESH_OWNER_START_PASS");
        return 0;
    }

    private static async Task<int> ExpectNonOwnerDeniedAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        var before = await client.GetByeDpiStatusAsync(cancellationToken);
        RequireSuccess(before, "NONOWNER_STATUS_BEFORE");
        if (RequireStatus(before) != IpcByeDpiProcessState.Running)
        {
            throw new InvalidOperationException("R39 non-owner probe expected a running fresh session.");
        }

        var start = await client.StartByeDpiAsync(cancellationToken);
        RequireDeniedByOwner(start, "NONOWNER_START");
        Console.WriteLine("R39_OLD_OWNER_START_DENIED=PASS");

        var stop = await client.StopByeDpiAsync(cancellationToken);
        RequireDeniedByOwner(stop, "NONOWNER_STOP");
        Console.WriteLine("R39_OLD_OWNER_STOP_DENIED=PASS");

        var after = await client.GetByeDpiStatusAsync(cancellationToken);
        RequireSuccess(after, "NONOWNER_STATUS_AFTER");
        if (RequireStatus(after) != IpcByeDpiProcessState.Running)
        {
            throw new InvalidOperationException("R39 denied old-owner operations changed fresh owner state.");
        }

        Console.WriteLine("R39_FRESH_LEASE_PRESERVED=PASS");
        Console.WriteLine("RESULT=R39_OLD_OWNER_DENIAL_PASS");
        return 0;
    }

    private static async Task<int> FreshOwnerStopAsync(
        WindowsPrivilegedServiceClient client,
        CancellationToken cancellationToken)
    {
        var before = await client.GetByeDpiStatusAsync(cancellationToken);
        RequireSuccess(before, "FRESH_OWNER_STATUS_BEFORE_STOP");
        if (RequireStatus(before) != IpcByeDpiProcessState.Running)
        {
            throw new InvalidOperationException("R39 fresh owner expected Running before stop.");
        }

        var stopped = await client.StopByeDpiAsync(cancellationToken);
        RequireSuccess(stopped, "FRESH_OWNER_STOP");
        if (RequireStatus(stopped) != IpcByeDpiProcessState.Stopped)
        {
            throw new InvalidOperationException("R39 fresh owner stop did not return Stopped.");
        }

        await WaitForNoListenerAsync(ProxyPort, cancellationToken);
        Console.WriteLine("R39_FRESH_OWNER_STOP=PASS");
        Console.WriteLine("R39_FRESH_OWNER_CLEANUP=PASS");
        Console.WriteLine("RESULT=R39_FRESH_OWNER_STOP_PASS");
        return 0;
    }

    private static bool IsFixedMode(string value) =>
        value is ExpectStoppedMode
            or StartFreshOwnerMode
            or ExpectNonOwnerDeniedMode
            or FreshOwnerStopMode;

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
        ?? throw new InvalidOperationException("R39 ByeDPI response has no status payload.");

    private static void RequireSuccess(IpcResponse response, string stage)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException($"{stage} failed: {response.Code}: {response.Message}");
        }
    }

    private static async Task WaitForListenerAsync(
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
                return;
            }
            catch (SocketException)
            {
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new InvalidOperationException("R39 proxy listener did not appear.");
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

        throw new InvalidOperationException("R39 proxy listener remained unexpectedly.");
    }
}
