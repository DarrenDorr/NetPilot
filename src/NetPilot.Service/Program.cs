using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;
using NetPilot.Security;
using NetPilot.Service;
using NetPilot.Updater;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "NetPilot Service";
});

builder.Services.AddSingleton(new OperationPolicy(
[
    PrivilegedOperation.EngineInstall,
    PrivilegedOperation.EngineStart,
    PrivilegedOperation.EngineStop,
    PrivilegedOperation.UpdateApply
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
builder.Services.AddSingleton(_ =>
    new ZapretInstallationInspector(
        WindowsZapretEngineLocation.GetDefaultEngineBaseRoot()));
builder.Services.AddHostedService<PrivilegedIpcWorker>();

await builder.Build().RunAsync();
