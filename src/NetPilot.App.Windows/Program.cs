using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using NetPilot.App;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;
using NetPilot.Automation;
using NetPilot.Health;
using NetPilot.Platform.Windows;

namespace NetPilot.App.Windows;

internal static class Program
{
    private const string EnrollmentArgument = "--complete-install-enrollment";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2
            && string.Equals(args[0], EnrollmentArgument, StringComparison.Ordinal))
        {
            return CompleteInstallerEnrollmentAsync(args[1]).GetAwaiter().GetResult();
        }

        AppComposition.Configure(CreateMainWindowViewModel, CreateSupportInfo());
        return AppComposition.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static async Task<int> CompleteInstallerEnrollmentAsync(string enrollmentNonce)
    {
        if (string.IsNullOrWhiteSpace(enrollmentNonce)
            || enrollmentNonce.Length != 64
            || enrollmentNonce.Any(static character => !Uri.IsHexDigit(character)))
        {
            return 10;
        }

        // R31.1: elevation status is only a local sanity signal, not the trust boundary.
        // The LocalSystem service performs the authoritative enrollment checks itself:
        // authenticated interactive named-pipe peer, exact Session ID match and one-time nonce.
        // This permits valid Inno/UAC flows where ExecAsOriginalUser still reports an elevated
        // token while preserving fail-closed behavior if Windows cannot inspect the token.
        var elevationState = WindowsProcessElevation.GetCurrentState();
        if (!WindowsInstallerEnrollmentPolicy.MayCompleteEnrollment(elevationState))
        {
            return 13;
        }

        try
        {
            var client = new WindowsPrivilegedServiceClient();
            var response = await client.EnrollAuthorizedUserAsync(
                enrollmentNonce,
                CancellationToken.None);
            return response.Success ? 0 : 11;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NetPilot.Ipc.IpcProtocolException)
        {
            return 12;
        }
    }

    private static ApplicationSupportInfo CreateSupportInfo()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString();

        var platform = new WindowsPlatformInfo().DisplayName;
        var architecture = RuntimeInformation.ProcessArchitecture
            .ToString()
            .ToLowerInvariant();

        return new ApplicationSupportInfo(version, platform, architecture);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var healthChecker = new PublicHttpsHealthChecker();
        var serviceClient = new WindowsPrivilegedServiceClient();

        // Пока native admission не содержит ADMIT, GUI/Automation вообще не получают
        // legacy Zapret runtime. Дополнительно adapter получает явную deny-all admission
        // policy: trusted capability declaration в будущем сама по себе не сможет открыть
        // profile runtime без отдельного security admission.
        var profileRuntime = new NoAdmittedEngineProfileRuntime();
        var strategyRuntime = new EngineProfileStrategyRuntimeAdapter(
            profileRuntime,
            DenyAllEngineRuntimeAdmissionPolicy.Instance);

        var healthProbe = new PublicResourceHealthProbe(healthChecker);
        var selectionEngine = new StrategySelectionEngine(strategyRuntime, healthProbe);
        var provisioningService = new WindowsEngineProvisioningService(serviceClient);
        var dashboardService = new DashboardService(
            healthChecker,
            strategyRuntime,
            selectionEngine,
            provisioningService);

        return new MainWindowViewModel(dashboardService);
    }
}
