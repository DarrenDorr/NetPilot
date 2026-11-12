using Avalonia;
using NetPilot.App;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;
using NetPilot.Automation;
using NetPilot.Health;
using NetPilot.Platform.Windows;
using NetPilot.Platform.Windows.Diagnostics;

namespace NetPilot.App.Windows;

internal static class Program
{
    private static int _crashBundleCreated;

    [STAThread]
    public static void Main(string[] args)
    {
        var startupCorrelationId = Guid.NewGuid().ToString("N");
        RegisterUnhandledExceptionLogging(startupCorrelationId);

        SafeDiagnosticLog.WriteUser(
            "app",
            NetPilotDiagnosticLevel.Information,
            "NP-APP-START-001",
            "Запуск NetPilot начат.",
            startupCorrelationId);

        if (args.Any(static arg => arg.Equals("--collect-diagnostics", StringComparison.OrdinalIgnoreCase)))
        {
            SafeDiagnosticLog.WriteUser(
                "app",
                NetPilotDiagnosticLevel.Information,
                "NP-DIAG-REQUEST-001",
                "Пользователь запросил локальный диагностический пакет.",
                startupCorrelationId);

            NetPilotSupportBundleBuilder.Build(
                new NetPilotSupportBundleContext(
                    LastOperation: "ManualSupportBundle",
                    DiagnosticCode: "NP-DIAG-MANUAL-001",
                    CorrelationId: startupCorrelationId));
            return;
        }

        try
        {
            AppComposition.Configure(CreateMainWindowViewModel);
            AppComposition.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            SafeDiagnosticLog.WriteUser(
                "app",
                NetPilotDiagnosticLevel.Information,
                "NP-APP-STOP-001",
                "NetPilot завершён штатно.",
                startupCorrelationId);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogCrashAndCollectBundle(exception, startupCorrelationId, "MainLoop");
            throw;
        }
    }

    private static void RegisterUnhandledExceptionLogging(string startupCorrelationId)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                LogCrashAndCollectBundle(exception, startupCorrelationId, "AppDomainUnhandledException");
            }
        };

        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            SafeDiagnosticLog.WriteUser(
                "app",
                NetPilotDiagnosticLevel.Error,
                "NP-APP-TASK-001",
                "Обнаружена ненаблюдаемая ошибка фоновой задачи.",
                startupCorrelationId,
                eventArgs.Exception);
        };
    }

    private static void LogCrashAndCollectBundle(
        Exception exception,
        string correlationId,
        string operation)
    {
        SafeDiagnosticLog.WriteUser(
            "app",
            NetPilotDiagnosticLevel.Critical,
            "NP-APP-CRASH-001",
            "NetPilot завершился с необработанной ошибкой.",
            correlationId,
            exception,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Operation"] = operation
            });

        if (Interlocked.Exchange(ref _crashBundleCreated, 1) != 0)
        {
            return;
        }

        try
        {
            NetPilotSupportBundleBuilder.Build(
                new NetPilotSupportBundleContext(
                    LastOperation: operation,
                    DiagnosticCode: "NP-APP-CRASH-001",
                    CorrelationId: correlationId));
        }
        catch (Exception bundleException) when (bundleException is not OutOfMemoryException)
        {
            SafeDiagnosticLog.WriteUser(
                "app",
                NetPilotDiagnosticLevel.Error,
                "NP-DIAG-BUNDLE-ERR-001",
                "Не удалось создать локальный диагностический пакет после сбоя.",
                correlationId,
                bundleException);
        }
    }

    private static MainWindowViewModel CreateMainWindowViewModel()
    {
        var healthChecker = new PublicHttpsHealthChecker();
        var strategyRuntime = new WindowsByeDpiStrategyRuntime(
            ByeDpiUserModeOptions.CreateInstalledDefault());
        var healthProbe = new ByeDpiProxyResourceHealthProbe(strategyRuntime);
        var selectionEngine = new StrategySelectionEngine(strategyRuntime, healthProbe);
        var dashboardService = new DashboardService(
            healthChecker,
            healthProbe,
            strategyRuntime,
            selectionEngine);

        return new MainWindowViewModel(dashboardService);
    }
}
