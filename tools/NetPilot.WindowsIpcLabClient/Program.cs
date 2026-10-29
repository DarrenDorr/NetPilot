using System.Diagnostics;
using System.Security.Principal;
using NetPilot.Platform.Windows;

namespace NetPilot.WindowsIpcLabClient;

internal static class Program
{
    private const string ExpectAuthorized = "--expect-authorized";

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("ERROR=WindowsOnly");
            return 20;
        }

        if (args.Length != 1 || args[0] != ExpectAuthorized)
        {
            Console.Error.WriteLine("ERROR=InvalidMode");
            return 21;
        }

        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value;
        if (string.IsNullOrWhiteSpace(sid) || identity.IsSystem)
        {
            Console.Error.WriteLine("ERROR=InvalidClientIdentity");
            return 22;
        }

        var principal = new WindowsPrincipal(identity);
        var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
        var sessionId = Process.GetCurrentProcess().SessionId;

        Console.WriteLine($"SID={sid}");
        Console.WriteLine($"IDENTITY={identity.Name}");
        Console.WriteLine($"SESSION_ID={sessionId}");
        Console.WriteLine($"IS_ADMIN={isAdmin}");

        if (isAdmin)
        {
            Console.Error.WriteLine("ERROR=AdminClientForbidden");
            return 23;
        }

        if (sessionId == 0)
        {
            Console.Error.WriteLine("ERROR=SessionZeroForbidden");
            return 24;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var client = new WindowsPrivilegedServiceClient();
            var response = await client.PingAsync(timeout.Token);

            Console.WriteLine($"SERVICE_SUCCESS={response.Success}");
            Console.WriteLine($"SERVICE_CODE={response.Code}");

            if (!response.Success
                || !string.Equals(response.Code, "Ok", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("ERROR=ExpectedAuthorizedResponse");
                return 10;
            }

            // Read-only capability probe. Он не устанавливает, не запускает и не
            // останавливает native engine; нужен только для доказательства полного
            // GUI/client -> authenticated IPC -> LocalSystem service round-trip.
            var installationResponse = await client.GetZapretInstallationStatusAsync(timeout.Token);
            Console.WriteLine($"ENGINE_STATUS_SUCCESS={installationResponse.Success}");
            Console.WriteLine($"ENGINE_STATUS_CODE={installationResponse.Code}");

            if (!installationResponse.Success
                || !string.Equals(installationResponse.Code, "Ok", StringComparison.Ordinal)
                || installationResponse.ZapretInstallation is null)
            {
                Console.Error.WriteLine("ERROR=EngineStatusProbeFailed");
                return 15;
            }

            Console.WriteLine($"ENGINE_INSTALLED={installationResponse.ZapretInstallation.IsInstalled}");
            Console.WriteLine($"ENGINE_VERSION={installationResponse.ZapretInstallation.Version ?? string.Empty}");

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var profileRoot = Path.Combine(programFiles, "NetPilot", "Users", sid);

            try
            {
                _ = Directory.GetFileSystemEntries(profileRoot);
                Console.Error.WriteLine("ERROR=ProfileDirectAccessUnexpectedlyAllowed");
                return 11;
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("PROFILE_DIRECT_ACCESS=DENIED");
            }

            Console.WriteLine("RESULT=AUTHORIZED_PASS");
            return 0;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"ERROR=PipeAccessDenied:{exception.GetType().Name}");
            return 12;
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"ERROR=PipeIo:{exception.GetType().Name}");
            return 13;
        }
        catch (Exception exception) when (
            exception is NetPilot.Ipc.IpcProtocolException
                or OperationCanceledException)
        {
            Console.Error.WriteLine($"ERROR=IpcFailure:{exception.GetType().Name}");
            return 14;
        }
    }
}
