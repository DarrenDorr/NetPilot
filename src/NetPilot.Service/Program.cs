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

// На текущем этапе ни Flowseal/zapret1, ни zapret2, ни ByeDPI не имеют
// production runtime ADMIT. Поэтому installation/start остаются fail-closed.
// EngineStop оставлен намеренно для уже управляемого процесса, но новый native
// process production-служба создать не способна.
builder.Services.AddSingleton(OperationPolicy.CreateNoAdmittedEnginePolicy());

var installationRoot = WindowsZapretEngineLocation.GetDefaultInstallationRoot();
builder.Services.AddSingleton(_ =>
    new WindowsAuthorizedUserStore(installationRoot));
builder.Services.AddSingleton<IWindowsUserProfileDirectorySecurity, WindowsUserProfileDirectorySecurity>();
builder.Services.AddSingleton(serviceProvider =>
    new WindowsUserProfileStore(
        installationRoot,
        serviceProvider.GetRequiredService<IWindowsUserProfileDirectorySecurity>()));
builder.Services.AddSingleton<UserResourceOwnership>();
builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();

// R20: без ADMIT LocalSystem-служба получает только fail-closed Zapret launcher.
// Даже прямой ошибочный StartAsync не может дойти до Process.Start.
builder.Services.AddSingleton<IZapretProcessLauncher>(
    NoAdmittedZapretProcessLauncher.Instance);
builder.Services.AddSingleton<ZapretProcessRunner>();
builder.Services.AddSingleton(serviceProvider =>
    new ZapretEngineController(
        WindowsZapretEngineLocation.GetDefaultEngineRoot(),
        serviceProvider.GetRequiredService<ZapretProcessRunner>()));
builder.Services.AddSingleton<IZapretUpdateRuntimeGate, WindowsZapretUpdateRuntimeGate>();

// R35: service-owned ByeDPI lifecycle подключён к Service как отдельная runtime-session,
// но обе production-границы запуска остаются fail-closed:
// 1) process launcher не может создать процесс;
// 2) runtime plan provider не может выдать verified/compiled launch plan.
// Замена любого из них требует отдельного admission decision после runtime evidence.
builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher>(
    NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance);
builder.Services.AddSingleton<IWindowsByeDpiRuntimePlanProvider>(
    NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance);
builder.Services.AddSingleton<WindowsByeDpiServiceOwnedProcessController>();
builder.Services.AddSingleton<WindowsByeDpiRuntimeSession>();

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
