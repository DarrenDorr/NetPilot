using System.Globalization;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.ByeDpiExperimentalService;

internal sealed class ExperimentalByeDpiRuntimePlanProvider :
    IWindowsByeDpiRuntimePlanProvider,
    IDisposable
{
    internal const ushort ProxyPort = 39036;

    private readonly WindowsVerifiedEnginePackage _verifiedPackage;
    private readonly WindowsCompiledEngineArgumentPlan _compiledPlan;
    private bool _disposed;

    public ExperimentalByeDpiRuntimePlanProvider()
    {
        if (BuiltInEngineArgumentPlanCatalog.All.Count != 0
            || BuiltInEngineExecutableIdentityCatalog.All.Count != 0
            || BuiltInEnginePackageLayoutCatalog.All.Count != 0)
        {
            throw new InvalidOperationException(
                "Experimental ByeDPI refuses to start when production native engine catalogs are non-empty.");
        }

        var contract = ByeDpiR34LabContract.Create(ProxyPort);
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
                "-p", ProxyPort.ToString(CultureInfo.InvariantCulture),
                "-N", "-U", "-c", "8", "-x", "0"
            };

            if (!_compiledPlan.Arguments.SequenceEqual(expectedArguments, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Experimental ByeDPI exact typed argv drifted from the R34-R40 evidence boundary.");
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
