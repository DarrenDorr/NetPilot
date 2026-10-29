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

// R33: service-owned ByeDPI lifecycle уже существует как bounded controller,
// но production DI намеренно получает launcher, который физически не создаёт процесс.
// SystemWindowsVerifiedEngineProcessLauncher предназначен только для отдельного
// lab/admission composition после explicit gate, а не для обычного Service startup.
builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher>(
    NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance);
builder.Services.AddSingleton<WindowsByeDpiServiceOwnedProcessController>();

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
