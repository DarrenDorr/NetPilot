using System.Globalization;
using System.Security.Principal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetPilot.Engine.Abstractions;
using NetPilot.Engine.Zapret.Windows;
using NetPilot.Platform.Windows;
using NetPilot.Security;
using NetPilot.Service;
using NetPilot.Updater;

namespace NetPilot.ByeDpiR36ServiceLab;

internal static class Program
{
    internal const ushort ProxyPort = 39036;
    internal const string ServiceName = "NetPilot.R36.Lab";

    public static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()
            || !Environment.Is64BitOperatingSystem
            || !Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException("R36 LocalSystem lab requires Windows x64.");
        }

        if (args.Length != 0)
        {
            throw new InvalidOperationException("R36 lab service accepts no command-line arguments.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        if (!identity.IsSystem)
        {
            throw new InvalidOperationException("R36 lab service must run only as LocalSystem.");
        }

        var installationRoot = WindowsZapretEngineLocation.GetDefaultInstallationRoot();
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = ServiceName;
        });

        // Lab-only policy. This executable is never shipped by the production installer.
        builder.Services.AddSingleton(new OperationPolicy(
        [
            PrivilegedOperation.EngineStart,
            PrivilegedOperation.EngineStop
        ]));

        builder.Services.AddSingleton(_ => new WindowsAuthorizedUserStore(installationRoot));
        builder.Services.AddSingleton<IWindowsUserProfileDirectorySecurity, WindowsUserProfileDirectorySecurity>();
        builder.Services.AddSingleton(serviceProvider =>
            new WindowsUserProfileStore(
                installationRoot,
                serviceProvider.GetRequiredService<IWindowsUserProfileDirectorySecurity>()));
        builder.Services.AddSingleton<UserResourceOwnership>();
        builder.Services.AddSingleton<WindowsNamedPipeServerFactory>();
        builder.Services.AddSingleton<WindowsNamedPipePeerAuthenticator>();

        // Zapret remains no-admit even inside this ByeDPI-specific lab.
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

        // R36 is the only composition allowed to cross the R34-tested launch boundary.
        // The executable launcher and exact plan provider live only in this lab project.
        builder.Services.AddSingleton<IWindowsVerifiedEngineProcessLauncher, SystemZapretProcessLauncher>();
        builder.Services.AddSingleton<R36ByeDpiRuntimePlanProvider>();
        builder.Services.AddSingleton<IWindowsByeDpiRuntimePlanProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<R36ByeDpiRuntimePlanProvider>());
        builder.Services.AddSingleton<WindowsByeDpiServiceOwnedProcessController>();
        builder.Services.AddSingleton<WindowsByeDpiRuntimeSession>();
        builder.Services.AddHostedService<PrivilegedIpcWorker>();

        await builder.Build().RunAsync();
    }
}

internal sealed class R36ByeDpiRuntimePlanProvider :
    IWindowsByeDpiRuntimePlanProvider,
    IDisposable
{
    private readonly WindowsVerifiedEnginePackage _verifiedPackage;
    private readonly WindowsCompiledEngineArgumentPlan _compiledPlan;
    private bool _disposed;

    public R36ByeDpiRuntimePlanProvider()
    {
        if (BuiltInEngineArgumentPlanCatalog.All.Count != 0
            || BuiltInEngineExecutableIdentityCatalog.All.Count != 0
            || BuiltInEnginePackageLayoutCatalog.All.Count != 0)
        {
            throw new InvalidOperationException(
                "R36 refuses to start when production native engine catalogs are non-empty.");
        }

        var contract = ByeDpiR34LabContract.Create(Program.ProxyPort);
        _verifiedPackage = WindowsImmutableEnginePackageVerifier.Verify(
            contract.LaunchIdentityBinding,
            contract.PackageLayout);

        try
        {
            var security = WindowsProtectedEngineSecurityVerifier.Verify(_verifiedPackage);
            var handoff = WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                contract.LaunchIdentityBinding,
                _verifiedPackage,
                security);
            _compiledPlan = WindowsTypedEngineArgumentPlanCompiler.Compile(
                handoff,
                contract.ArgumentPlan);

            var expectedArguments = new[]
            {
                "-i", "127.0.0.1",
                "-p", Program.ProxyPort.ToString(CultureInfo.InvariantCulture),
                "-N", "-U", "-c", "8", "-x", "0"
            };

            if (!_compiledPlan.Arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("R36 exact typed argv drifted from R34 evidence.");
            }
        }
        catch
        {
            _verifiedPackage.Dispose();
            throw;
        }
    }

    public WindowsCompiledEngineArgumentPlan CreatePlan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _compiledPlan.EnsureReady();
        return _compiledPlan;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _verifiedPackage.Dispose();
    }
}
