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

// На текущем этапе ни Flowseal/zapret1, ни zapret2 не имеют статуса ADMIT.
// Поэтому установка и запуск сетевого движка запрещены fail-closed.
// EngineStop оставлен намеренно: runner по-прежнему может безопасно остановить
// только тот процесс, которым уже владеет, но новый процесс создать не способен.
builder.Services.AddSingleton(OperationPolicy.CreateNoAdmittedEnginePolicy());

builder.Services.AddSingleton(serviceProvider =>
    new WindowsAuthorizedUserStore(
        WindowsZapretEngineLocation.GetDefaultInstallationRoot()));
builder.Services.AddSingleton<UserResourceOwnership>();
builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();

// R20: без ADMIT LocalSystem-служба не получает SystemZapretProcessLauncher.
// Даже прямой ошибочный StartAsync не может дойти до Process.Start.
builder.Services.AddSingleton<IZapretProcessLauncher>(
    NoAdmittedZapretProcessLauncher.Instance);
builder.Services.AddSingleton<ZapretProcessRunner>();
builder.Services.AddSingleton(serviceProvider =>
    new ZapretEngineController(
        WindowsZapretEngineLocation.GetDefaultEngineRoot(),
        serviceProvider.GetRequiredService<ZapretProcessRunner>()));
builder.Services.AddSingleton<IZapretUpdateRuntimeGate, WindowsZapretUpdateRuntimeGate>();

// R17/R20: release identity по-прежнему проходит только через admission-wrapper,
// но при отсутствии ADMIT LocalSystem-служба даже не получает сетевой GitHub transport.
// NoAdmittedZapretReleaseTransport не содержит HttpClient и всегда отказывает fail-closed.
builder.Services.AddSingleton<IZapretReleaseTransport>(
    NoAdmittedZapretReleaseTransport.Instance);
builder.Services.AddSingleton<IZapretReleaseAdmissionCatalog>(
    NoAdmittedZapretReleaseCatalog.Instance);
builder.Services.AddSingleton<IZapretReleaseSource>(serviceProvider =>
    new AdmittedZapretReleaseSource(
        serviceProvider.GetRequiredService<IZapretReleaseTransport>(),
        serviceProvider.GetRequiredService<IZapretReleaseAdmissionCatalog>()));

builder.Services.AddSingleton(serviceProvider =>
    new ZapretUpdateOrchestrator(
        WindowsZapretEngineLocation.GetDefaultEngineBaseRoot(),
        serviceProvider.GetRequiredService<IZapretReleaseSource>(),
        serviceProvider.GetRequiredService<IZapretUpdateRuntimeGate>()));
builder.Services.AddHostedService<PrivilegedIpcWorker>();

await builder.Build().RunAsync();
