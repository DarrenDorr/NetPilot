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

#if NETPILOT_EXPERIMENTAL_BYEDPI
// Experimental alpha composition: only the exact R34-R40 ByeDPI candidate may start.
// Zapret/zapret2 remain no-admit and WinDivert is not present. EngineInstall remains denied.
builder.Services.AddSingleton(new OperationPolicy(
[
    PrivilegedOperation.EngineStart,
    PrivilegedOperation.EngineStop
]));
#else
// Default product composition remains fail-closed for all native engine starts.
builder.Services.AddSingleton(OperationPolicy.CreateNoAdmittedEnginePolicy());
#endif

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

// Zapret remains fail-closed even in the experimental ByeDPI build.
builder.Services.AddSingleton<IZapretProcessLauncher>(
    NoAdmittedZapretProcessLauncher.Instance);
builder.Services.AddSingleton<ZapretProcessRunner>();
builder.Services.AddSingleton(serviceProvider =>
    new ZapretEngineController(
        WindowsZapretEngineLocation.GetDefaultEngineRoot(),
        serviceProvider.GetRequiredService<ZapretProcessRunner>()));
builder.Services.AddSingleton<IZapretUpdateRuntimeGate, WindowsZapretUpdateRuntimeGate>();

#if NETPILOT_EXPERIMENTAL_BYEDPI
// The only execution-capable composition in this alpha build. The provider performs
// exact immutable package + ACL/owner + typed argv verification before returning a plan.
builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher, SystemZapretProcessLauncher>();
builder.Services.AddSingleton<ExperimentalByeDpiRuntimePlanProvider>();
builder.Services.AddSingleton<IWindowsByeDpiRuntimePlanProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<ExperimentalByeDpiRuntimePlanProvider>());
#else
builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher>(
    NoAdmittedWindowsVerifiedEngineProcessLauncher.Instance);
builder.Services.AddSingleton<IWindowsByeDpiRuntimePlanProvider>(
    NoAdmittedWindowsByeDpiRuntimePlanProvider.Instance);
#endif
builder.Services.AddSingleton<WindowsByeDpiServiceOwnedProcessController>();
builder.Services.AddSingleton<WindowsByeDpiRuntimeSession>();

// Network acquisition stays disabled in both builds. This alpha is installer-bundled only.
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
