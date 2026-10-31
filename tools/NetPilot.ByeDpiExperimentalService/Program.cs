using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;
using NetPilot.Security;
using NetPilot.Service;
using NetPilot.Updater;

namespace NetPilot.ByeDpiExperimentalService;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()
            || !Environment.Is64BitOperatingSystem
            || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("Experimental ByeDPI service requires Windows x64.");
        }

        if (args.Length != 0)
        {
            throw new InvalidOperationException("Experimental ByeDPI service accepts no command-line arguments.");
        }

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "NetPilot Service";
        });

        builder.Services.AddSingleton(new OperationPolicy(
        [
            PrivilegedOperation.EngineStart,
            PrivilegedOperation.EngineStop
        ]));

        var installationRoot = WindowsZapretEngineLocation.GetDefaultInstallationRoot();
        builder.Services.AddSingleton(_ => new WindowsAuthorizedUserStore(installationRoot));
        builder.Services.AddSingleton<IWindowsUserProfileDirectorySecurity, WindowsUserProfileDirectorySecurity>();
        builder.Services.AddSingleton(serviceProvider =>
            new WindowsUserProfileStore(
                installationRoot,
                serviceProvider.GetRequiredService<IWindowsUserProfileDirectorySecurity>()));
        builder.Services.AddSingleton<UserResourceOwnership>();
        builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
        builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();

        // Zapret and zapret2 remain no-admit in this host.
        builder.Services.AddSingleton<IZapretProcessLauncher>(NoAdmittedZapretProcessLauncher.Instance);
        builder.Services.AddSingleton<ZapretProcessRunner>();
        builder.Services.AddSingleton(serviceProvider =>
            new ZapretEngineController(
                WindowsZapretEngineLocation.GetDefaultEngineRoot(),
                serviceProvider.GetRequiredService<ZapretProcessRunner>()));
        builder.Services.AddSingleton<IZapretUpdateRuntimeGate, WindowsZapretUpdateRuntimeGate>();
        builder.Services.AddSingleton<IZapretReleaseTransport>(NoAdmittedZapretReleaseTransport.Instance);
        builder.Services.AddSingleton<IZapretReleaseAdmissionCatalog>(NoAdmittedZapretReleaseCatalog.Instance);
        builder.Services.AddSingleton<IZapretReleaseSource>(serviceProvider =>
            new AdmittedZapretReleaseSource(
                serviceProvider.GetRequiredService<IZapretReleaseTransport>(),
                serviceProvider.GetRequiredService<IZapretReleaseAdmissionCatalog>()));
        builder.Services.AddSingleton(serviceProvider =>
            new ZapretUpdateOrchestrator(
                WindowsZapretEngineLocation.GetDefaultEngineBaseRoot(),
                serviceProvider.GetRequiredService<IZapretReleaseSource>(),
                serviceProvider.GetRequiredService<IZapretUpdateRuntimeGate>()));

        // Only the exact R34-R40 ByeDPI candidate can cross this experimental boundary.
        builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher, SystemZapretProcessLauncher>();
        builder.Services.AddSingleton<ExperimentalByeDpiRuntimePlanProvider>();
        builder.Services.AddSingleton<IWindowsByeDpiRuntimePlanProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ExperimentalByeDpiRuntimePlanProvider>());
        builder.Services.AddSingleton<WindowsByeDpiServiceOwnedProcessController>();
        builder.Services.AddSingleton<WindowsByeDpiRuntimeSession>();
        builder.Services.AddHostedService<PrivilegedIpcWorker>();

        await builder.Build().RunAsync();
    }
}
