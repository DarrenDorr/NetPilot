using System.Diagnostics;
using System.Reflection;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsVerifiedEngineProcessStartInfoFactoryTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Public_factory_accepts_only_compiled_typed_plan()
    {
        var method = typeof(WindowsVerifiedEngineProcessStartInfoFactory)
            .GetMethod(
                nameof(WindowsVerifiedEngineProcessStartInfoFactory.Create),
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Create method not found.");

        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(WindowsCompiledEngineArgumentPlan), parameter.ParameterType);
        Assert.NotEqual(typeof(string), parameter.ParameterType);
    }

    [Fact]
    public void Create_uses_exact_primary_argument_list_and_minimal_environment()
    {
        using var fixture = CreateFixture();

        var startInfo = WindowsVerifiedEngineProcessStartInfoFactory.Create(
            fixture.CompiledPlan);

        Assert.Equal(Path.GetFullPath(fixture.PrimaryPath), startInfo.FileName);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(fixture.BinDirectory)),
            startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
        Assert.False(startInfo.ErrorDialog);
        Assert.False(startInfo.RedirectStandardInput);
        Assert.False(startInfo.RedirectStandardOutput);
        Assert.False(startInfo.RedirectStandardError);
        Assert.True(string.IsNullOrEmpty(startInfo.Arguments));
        Assert.Collection(
            startInfo.ArgumentList,
            first => Assert.Equal("--safe-flag", first));

        Assert.Equal(2, startInfo.Environment.Count);
        Assert.True(startInfo.Environment.ContainsKey("SystemRoot"));
        Assert.True(startInfo.Environment.ContainsKey("WINDIR"));
        Assert.False(startInfo.Environment.ContainsKey("PATH"));
        Assert.False(startInfo.Environment.ContainsKey("PATHEXT"));
    }

    [Fact]
    public void Create_fails_after_verified_package_is_disposed()
    {
        using var fixture = CreateFixture();
        fixture.Package.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            WindowsVerifiedEngineProcessStartInfoFactory.Create(
                fixture.CompiledPlan));
    }

    [Fact]
    public void Create_rejects_verified_primary_that_does_not_match_identity()
    {
        using var fixture = CreateFixture(
            verifiedKind: EngineExecutableComponentKind.SharedLibrary);

        var exception = Assert.Throws<WindowsEngineProcessStartInfoException>(() =>
            WindowsVerifiedEngineProcessStartInfoFactory.Create(
                fixture.CompiledPlan));

        Assert.Equal("PrimaryVerifiedComponentMismatch", exception.Code);
    }

    [Fact]
    public void Factory_itself_does_not_expose_process_start_method()
    {
        Assert.DoesNotContain(
            typeof(WindowsVerifiedEngineProcessStartInfoFactory)
                .GetMethods(BindingFlags.Public | BindingFlags.Static),
            static method => string.Equals(
                method.Name,
                nameof(Process.Start),
                StringComparison.Ordinal));
    }

    private static Fixture CreateFixture(
        EngineExecutableComponentKind verifiedKind =
            EngineExecutableComponentKind.PrimaryExecutable)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"NetPilot-R28.19-{Guid.NewGuid():N}");
        var engineRoot = Path.Combine(tempRoot, "engine-root");
        var binDirectory = Path.Combine(engineRoot, "bin");
        Directory.CreateDirectory(binDirectory);
        var primaryPath = Path.Combine(binDirectory, "engine.exe");
        File.WriteAllText(primaryPath, "non-starting-process-info");

        var stream = new FileStream(
            primaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            var identity = CreateIdentity();
            var packageLayout = CreatePackageLayout(identity);
            var binding = CreateBinding(identity);
            var verifiedComponent = CreateVerifiedComponent(
                primaryPath,
                verifiedKind);
            var package = CreateVerifiedPackage(
                identity,
                packageLayout,
                engineRoot,
                verifiedComponent,
                stream);
            var securityVerification = CreateSecurityVerification(package);
            var handoff = WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                binding,
                package,
                securityVerification);
            var declaration = CreateArgumentPlan(binding);
            var compiledPlan = WindowsTypedEngineArgumentPlanCompiler.Compile(
                handoff,
                declaration);

            return new Fixture(
                tempRoot,
                engineRoot,
                binDirectory,
                primaryPath,
                package,
                compiledPlan);
        }
        catch
        {
            stream.Dispose();
            Directory.Delete(tempRoot, recursive: true);
            throw;
        }
    }

    private static EngineExecutableIdentityDeclaration CreateIdentity()
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        var component = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                DigestA
            });

        var identityConstructor = typeof(EngineExecutableIdentityDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 8);
        return (EngineExecutableIdentityDeclaration)identityConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                EngineBinaryArchitecture.X64,
                "1.0.6-np.test",
                DigestA,
                DigestB,
                DigestC,
                new[] { component }
            });
    }

    private static EnginePackageLayoutDeclaration CreatePackageLayout(
        EngineExecutableIdentityDeclaration identity)
    {
        var componentConstructor = typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var component = (EnginePackageComponentLayout)componentConstructor.Invoke(
            new object[] { "engine", "bin/engine.exe" });

        var layoutConstructor = typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        return (EnginePackageLayoutDeclaration)layoutConstructor.Invoke(
            new object[] { identity, new[] { component } });
    }

    private static EngineLaunchIdentityBinding CreateBinding(
        EngineExecutableIdentityDeclaration identity)
    {
        var capability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        var budgetConstructor = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        var budget = (EngineResourceBudgetDeclaration)budgetConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                (uint)32,
                (ulong)65536
            });

        var prerequisiteConstructor = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var prerequisites = (EngineRuntimePrerequisiteDeclaration)prerequisiteConstructor.Invoke(
            new object[] { capability, budget });

        var metadataConstructor = typeof(EngineLaunchPlanMetadata)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var metadata = (EngineLaunchPlanMetadata)metadataConstructor.Invoke(
            new object[]
            {
                prerequisites,
                BuiltInEngineProfileCatalog.StandardId
            });

        var bindingConstructor = typeof(EngineLaunchIdentityBinding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        return (EngineLaunchIdentityBinding)bindingConstructor.Invoke(
            new object[]
            {
                metadata,
                identity,
                EngineBinaryArchitecture.X64
            });
    }

    private static EngineArgumentPlanDeclaration CreateArgumentPlan(
        EngineLaunchIdentityBinding binding)
    {
        var tokenConstructor = typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        var token = (EngineArgumentTokenTemplate)tokenConstructor.Invoke(
            new object?[]
            {
                EngineArgumentTokenKind.Flag,
                "--safe-flag",
                (ulong)0,
                null
            });

        var planConstructor = typeof(EngineArgumentPlanDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        return (EngineArgumentPlanDeclaration)planConstructor.Invoke(
            new object[] { binding, new[] { token } });
    }

    private static WindowsVerifiedEngineComponent CreateVerifiedComponent(
        string fullPath,
        EngineExecutableComponentKind kind)
    {
        var constructor = typeof(WindowsVerifiedEngineComponent)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        return (WindowsVerifiedEngineComponent)constructor.Invoke(
            new object[]
            {
                "engine",
                kind,
                fullPath,
                DigestA
            });
    }

    private static WindowsVerifiedEnginePackage CreateVerifiedPackage(
        EngineExecutableIdentityDeclaration identity,
        EnginePackageLayoutDeclaration packageLayout,
        string engineRoot,
        WindowsVerifiedEngineComponent component,
        FileStream stream)
    {
        var constructor = typeof(WindowsVerifiedEnginePackage)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 5);
        return (WindowsVerifiedEnginePackage)constructor.Invoke(
            new object[]
            {
                identity,
                packageLayout,
                engineRoot,
                Array.AsReadOnly(new[] { component }),
                new[] { stream }
            });
    }

    private static WindowsProtectedEngineSecurityVerification CreateSecurityVerification(
        WindowsVerifiedEnginePackage package)
    {
        var constructor = typeof(WindowsProtectedEngineSecurityVerification)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        return (WindowsProtectedEngineSecurityVerification)constructor.Invoke(
            new object[] { package, 1 });
    }

    private sealed class Fixture(
        string tempRoot,
        string engineRoot,
        string binDirectory,
        string primaryPath,
        WindowsVerifiedEnginePackage package,
        WindowsCompiledEngineArgumentPlan compiledPlan) : IDisposable
    {
        public string EngineRoot { get; } = engineRoot;
        public string BinDirectory { get; } = binDirectory;
        public string PrimaryPath { get; } = primaryPath;
        public WindowsVerifiedEnginePackage Package { get; } = package;
        public WindowsCompiledEngineArgumentPlan CompiledPlan { get; } = compiledPlan;

        public void Dispose()
        {
            Package.Dispose();
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
