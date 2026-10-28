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

        AppComposition.Configure(CreateMainWindowViewModel);
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

        // ExecAsOriginalUser обязан вернуть нас к исходному не-elevated пользователю.
        // Если Setup был вручную запущен уже elevated (например, через "Запуск от имени
        // администратора"), Inno Setup не может восстановить исходный token. В таком случае
        // enrollment должен завершиться fail-closed, а не привязать установку к admin SID.
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

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var healthChecker = new PublicHttpsHealthChecker();
        var serviceClient = new WindowsPrivilegedServiceClient();

        // Пока native admission не содержит ADMIT, GUI/Automation вообще не получают
        // legacy Zapret runtime. Даже ошибочный вызов selector не сможет активировать
        // native-код: profile runtime физически fail-closed.
        var profileRuntime = new NoAdmittedEngineProfileRuntime();
        var strategyRuntime = new EngineProfileStrategyRuntimeAdapter(profileRuntime);

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
