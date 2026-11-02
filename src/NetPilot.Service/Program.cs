using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;
using NetPilot.Platform.Windows.Diagnostics;
using NetPilot.Security;
using NetPilot.Service;
using NetPilot.Updater;

var startupCorrelationId = Guid.NewGuid().ToString("N");
SafeDiagnosticLog.WriteService(
    "service",
    NetPilotDiagnosticLevel.Information,
    "NP-SVC-START-001",
    "Запуск Windows-службы NetPilot начат.",
    startupCorrelationId);

try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.AddProvider(new NetPilotFileLoggerProvider());

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "NetPilot Service";
    });

    builder.Services.AddSingleton(new OperationPolicy(
    [
        PrivilegedOperation.EngineStart,
        PrivilegedOperation.EngineStop
    ]));

    builder.Services.AddSingleton<UserResourceOwnership>();
    builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
    builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();
    builder.Services.AddSingleton<IZapretProcessLauncher, SystemZapretProcessLauncher>();
    builder.Services.AddSingleton<ZapretProcessRunner>();
    builder.Services.AddSingleton(serviceProvider =>
        new ZapretEngineController(
            WindowsZapretEngineLocation.GetDefaultEngineRoot(),
            serviceProvider.GetRequiredService<ZapretProcessRunner>()));
    builder.Services.AddSingleton<IZapretUpdateRuntimeGate, WindowsZapretUpdateRuntimeGate>();
    builder.Services.AddSingleton<IZapretReleaseSource, GithubZapretReleaseClient>();
    builder.Services.AddSingleton(serviceProvider =>
        new ZapretUpdateOrchestrator(
            WindowsZapretEngineLocation.GetDefaultEngineBaseRoot(),
            serviceProvider.GetRequiredService<IZapretReleaseSource>(),
            serviceProvider.GetRequiredService<IZapretUpdateRuntimeGate>()));
    builder.Services.AddHostedService<PrivilegedIpcWorker>();

    await builder.Build().RunAsync();

    SafeDiagnosticLog.WriteService(
        "service",
        NetPilotDiagnosticLevel.Information,
        "NP-SVC-STOP-001",
        "Windows-служба NetPilot завершена штатно.",
        startupCorrelationId);
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    SafeDiagnosticLog.WriteService(
        "service",
        NetPilotDiagnosticLevel.Critical,
        "NP-SVC-CRASH-001",
        "Windows-служба NetPilot завершилась с необработанной ошибкой.",
        startupCorrelationId,
        exception);
    throw;
}
