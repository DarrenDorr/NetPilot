using System.Reflection;
using System.Runtime.ExceptionServices;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsTypedEngineArgumentPlanTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Public_compiler_accepts_no_raw_cli_path_url_or_environment_input()
    {
        var method = typeof(WindowsTypedEngineArgumentPlanCompiler)
            .GetMethod(
                nameof(WindowsTypedEngineArgumentPlanCompiler.Compile),
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Compile method not found.");

        Assert.Collection(
            method.GetParameters(),
            first => Assert.Equal(
                typeof(WindowsVerifiedEnginePreLaunchHandoff),
                first.ParameterType),
            second => Assert.Equal(
                typeof(EngineArgumentPlanDeclaration),
                second.ParameterType));
        Assert.DoesNotContain(
            method.GetParameters(),
            static parameter => parameter.ParameterType == typeof(string));

        Assert.Empty(typeof(WindowsCompiledEngineArgumentPlan)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Compile_resolves_only_typed_trusted_sources()
    {
        using var fixture = CreateFixture();
        var declaration = CreateArgumentPlan(
            fixture.Binding,
            new[]
            {
                CreateToken(EngineArgumentTokenKind.Flag, "--safe-flag"),
                CreateToken(
                    EngineArgumentTokenKind.FixedPositiveUInt,
                    "--retry",
                    fixedPositiveUInt: 7),
                CreateToken(
                    EngineArgumentTokenKind.ConntrackMaxEntries,
                    "--conntrack-limit"),
                CreateToken(
                    EngineArgumentTokenKind.DelayedQueueMaxBytes,
                    "--queue-bytes"),
                CreateToken(
                    EngineArgumentTokenKind.VerifiedComponentPath,
                    "--component",
                    componentId: "engine")
            });

        var compiled = WindowsTypedEngineArgumentPlanCompiler.Compile(
            fixture.Handoff,
            declaration);

        Assert.Collection(
            compiled.Arguments,
            first => Assert.Equal("--safe-flag", first),
            second => Assert.Equal("--retry=7", second),
            third => Assert.Equal("--conntrack-limit=32", third),
            fourth => Assert.Equal("--queue-bytes=65536", fourth),
            fifth => Assert.Equal(
                $"--component={Path.GetFullPath(fixture.TempFile)}",
                fifth));

        Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)compiled.Arguments).Add("--forged"));

        compiled.EnsureReady();
        fixture.Package.Dispose();
        Assert.Throws<ObjectDisposedException>(() => compiled.EnsureReady());
    }

    [Fact]
    public void Compile_rejects_argument_plan_for_other_identity_binding()
    {
        using var first = CreateFixture();
        using var second = CreateFixture();
        var declaration = CreateArgumentPlan(
            second.Binding,
            new[] { CreateToken(EngineArgumentTokenKind.Flag, "--safe-flag") });

        var exception = Assert.Throws<WindowsEngineArgumentPlanException>(() =>
            WindowsTypedEngineArgumentPlanCompiler.Compile(
                first.Handoff,
                declaration));

        Assert.Equal("ArgumentPlanBindingMismatch", exception.Code);
    }

    private static EngineArgumentTokenTemplate CreateToken(
        EngineArgumentTokenKind kind,
        string optionName,
        ulong fixedPositiveUInt = 0,
        string? componentId = null)
    {
        var constructor = typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);

        try
        {
            return (EngineArgumentTokenTemplate)constructor.Invoke(
                new object?[] { kind, optionName, fixedPositiveUInt, componentId });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static EngineArgumentPlanDeclaration CreateArgumentPlan(
        EngineLaunchIdentityBinding binding,
        IReadOnlyList<EngineArgumentTokenTemplate> arguments)
    {
        var constructor = typeof(EngineArgumentPlanDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        return (EngineArgumentPlanDeclaration)constructor.Invoke(
            new object[] { binding, arguments });
    }

    private static Fixture CreateFixture()
    {
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"NetPilot-R28.18-{Guid.NewGuid():N}.bin");
        File.WriteAllText(tempFile, "typed-argument-lock");
        var stream = new FileStream(
            tempFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        try
        {
            var identity = CreateIdentity();
            var packageLayout = CreatePackageLayout(identity);
            var binding = CreateBinding(identity);
            var verifiedComponent = CreateVerifiedComponent(tempFile);
            var package = CreateVerifiedPackage(
                identity,
                packageLayout,
                tempFile,
                verifiedComponent,
                stream);
            var securityVerification = CreateSecurityVerification(package);
            var handoff = WindowsVerifiedEnginePreLaunchHandoffFactory.Create(
                binding,
                package,
                securityVerification);

            return new Fixture(tempFile, binding, package, handoff);
        }
        catch
        {
            stream.Dispose();
            File.Delete(tempFile);
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

    private static WindowsVerifiedEngineComponent CreateVerifiedComponent(string fullPath)
    {
        var constructor = typeof(WindowsVerifiedEngineComponent)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);
        return (WindowsVerifiedEngineComponent)constructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                fullPath,
                DigestA
            });
    }

    private static WindowsVerifiedEnginePackage CreateVerifiedPackage(
        EngineExecutableIdentityDeclaration identity,
        EnginePackageLayoutDeclaration packageLayout,
        string tempFile,
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
                Path.GetDirectoryName(tempFile)!,
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
        string tempFile,
        EngineLaunchIdentityBinding binding,
        WindowsVerifiedEnginePackage package,
        WindowsVerifiedEnginePreLaunchHandoff handoff) : IDisposable
    {
        public string TempFile { get; } = tempFile;
        public EngineLaunchIdentityBinding Binding { get; } = binding;
        public WindowsVerifiedEnginePackage Package { get; } = package;
        public WindowsVerifiedEnginePreLaunchHandoff Handoff { get; } = handoff;

        public void Dispose()
        {
            Package.Dispose();
            if (File.Exists(TempFile))
            {
                File.Delete(TempFile);
            }
        }
    }
}
