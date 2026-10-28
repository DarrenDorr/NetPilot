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
    [STAThread]
    public static void Main(string[] args)
    {
        AppComposition.Configure(CreateMainWindowViewModel);
        AppComposition.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var healthChecker = new PublicHttpsHealthChecker();
        var serviceClient = new WindowsPrivilegedServiceClient();
        var strategyRuntime = new WindowsZapretStrategyRuntime(serviceClient);
        var healthProbe = new PublicResourceHealthProbe(healthChecker);
        var selectionEngine = new StrategySelectionEngine(strategyRuntime, healthProbe);
        var dashboardService = new DashboardService(
            healthChecker,
            strategyRuntime,
            selectionEngine);

        return new MainWindowViewModel(dashboardService);
    }
}
