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
// EngineStop оставлен намеренно: служба должна уметь остановить уже существующий
// legacy-процесс после обновления NetPilot или при аварийном восстановлении.
builder.Services.AddSingleton(OperationPolicy.CreateNoAdmittedEnginePolicy());

builder.Services.AddSingleton(serviceProvider =>
    new WindowsAuthorizedUserStore(
        WindowsZapretEngineLocation.GetDefaultInstallationRoot()));
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

// R17: upstream GitHub является только transport. Решение о том, какой exact release
// можно установить, принимает локальный admission catalog. Пока ADMIT отсутствует,
// каталог пуст и updater отказывает до любого metadata/download запроса к GitHub.
builder.Services.AddSingleton<GithubZapretReleaseClient>();
builder.Services.AddSingleton<IZapretReleaseAdmissionCatalog>(
    NoAdmittedZapretReleaseCatalog.Instance);
builder.Services.AddSingleton<IZapretReleaseSource>(serviceProvider =>
    new AdmittedZapretReleaseSource(
        serviceProvider.GetRequiredService<GithubZapretReleaseClient>(),
        serviceProvider.GetRequiredService<IZapretReleaseAdmissionCatalog>()));

builder.Services.AddSingleton(serviceProvider =>
    new ZapretUpdateOrchestrator(
        WindowsZapretEngineLocation.GetDefaultEngineBaseRoot(),
        serviceProvider.GetRequiredService<IZapretReleaseSource>(),
        serviceProvider.GetRequiredService<IZapretUpdateRuntimeGate>()));
builder.Services.AddHostedService<PrivilegedIpcWorker>();

await builder.Build().RunAsync();
