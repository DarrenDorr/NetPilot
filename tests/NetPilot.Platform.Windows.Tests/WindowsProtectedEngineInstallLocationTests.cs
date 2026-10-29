using System.Reflection;
using NetPilot.Engine.Abstractions;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsProtectedEngineInstallLocationTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Resolve_public_api_accepts_no_user_defined_path()
    {
        var method = typeof(WindowsProtectedEngineInstallLocationResolver)
            .GetMethod(
                nameof(WindowsProtectedEngineInstallLocationResolver.Resolve),
                BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Resolve method not found.");

        var parameters = method.GetParameters();
        Assert.Collection(
            parameters,
            first => Assert.Equal(typeof(EngineLaunchIdentityBinding), first.ParameterType),
            second => Assert.Equal(typeof(EnginePackageLayoutDeclaration), second.ParameterType));
        Assert.DoesNotContain(parameters, static parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void Resolve_places_components_only_under_fixed_managed_program_files_root()
    {
        var identity = CreateIdentity(
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            "1.0.6-np.1");
        var binding = CreateBinding(
            identity,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var layout = CreateLayout(identity, "bin/winws2.exe");

        var resolved = WindowsProtectedEngineInstallLocationResolver.Resolve(binding, layout);

        var programFiles = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        var expectedRoot = Path.GetFullPath(
            Path.Combine(
                programFiles,
                "NetPilot",
                "Engines",
                "Managed",
                "zapret2",
                "1.0.6-np.1",
                "x64"));

        Assert.True(string.Equals(
            expectedRoot,
            resolved.EngineRoot,
            StringComparison.OrdinalIgnoreCase));
        var component = Assert.Single(resolved.Components);
        Assert.Equal("engine", component.ComponentId);
        Assert.True(string.Equals(
            Path.Combine(expectedRoot, "bin", "winws2.exe"),
            component.FullPath,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_does_not_create_managed_engine_directory()
    {
        var version = $"test-{Guid.NewGuid():N}";
        var identity = CreateIdentity(
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            version);
        var binding = CreateBinding(
            identity,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var layout = CreateLayout(identity, "bin/winws2.exe");

        var resolved = WindowsProtectedEngineInstallLocationResolver.Resolve(binding, layout);

        Assert.False(Directory.Exists(resolved.EngineRoot));
    }

    [Fact]
    public void Resolve_rejects_layout_from_different_identity_even_when_metadata_looks_similar()
    {
        var boundIdentity = CreateIdentity(
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            "1.0.6-np.1");
        var otherIdentity = CreateIdentity(
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            "1.0.6-np.1");
        var binding = CreateBinding(
            boundIdentity,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var layout = CreateLayout(otherIdentity, "bin/winws2.exe");

        Assert.Throws<InvalidOperationException>(() =>
            WindowsProtectedEngineInstallLocationResolver.Resolve(binding, layout));
    }

    [Fact]
    public void Resolve_rejects_non_windows_binding()
    {
        var identity = CreateIdentity(
            EngineHostPlatform.Linux,
            EngineBinaryArchitecture.X64,
            "1.0.6-np.1");
        var binding = CreateBinding(
            identity,
            EngineHostPlatform.Linux,
            EngineBinaryArchitecture.X64);
        var layout = CreateLayout(identity, "bin/winws2");

        Assert.Throws<InvalidOperationException>(() =>
            WindowsProtectedEngineInstallLocationResolver.Resolve(binding, layout));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("COM1")]
    [InlineData("Lpt9.release")]
    public void Resolve_rejects_windows_device_name_version_segments(string version)
    {
        var identity = CreateIdentity(
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            version);
        var binding = CreateBinding(
            identity,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var layout = CreateLayout(identity, "bin/winws2.exe");

        Assert.Throws<InvalidOperationException>(() =>
            WindowsProtectedEngineInstallLocationResolver.Resolve(binding, layout));
    }

    private static EngineLaunchIdentityBinding CreateBinding(
        EngineExecutableIdentityDeclaration identity,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture)
    {
        var capability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            platform);
        var budget = CreateBudget(platform);
        var prerequisites = CreatePrerequisites(capability, budget);
        var metadata = CreateLaunchMetadata(prerequisites);

        var constructor = typeof(EngineLaunchIdentityBinding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);

        return (EngineLaunchIdentityBinding)constructor.Invoke(
            new object[] { metadata, identity, architecture });
    }

    private static EngineResourceBudgetDeclaration CreateBudget(
        EngineHostPlatform platform)
    {
        var constructor = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);

        return (EngineResourceBudgetDeclaration)constructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                platform,
                (uint)1,
                (ulong)1
            });
    }

    private static EngineRuntimePrerequisiteDeclaration CreatePrerequisites(
        EngineBackendCapabilityDeclaration capability,
        EngineResourceBudgetDeclaration budget)
    {
        var constructor = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(EngineBackendCapabilityDeclaration)
                    && parameters[1].ParameterType == typeof(EngineResourceBudgetDeclaration);
            });

        return (EngineRuntimePrerequisiteDeclaration)constructor.Invoke(
            new object[] { capability, budget });
    }

    private static EngineLaunchPlanMetadata CreateLaunchMetadata(
        EngineRuntimePrerequisiteDeclaration prerequisites)
    {
        var constructor = typeof(EngineLaunchPlanMetadata)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(EngineRuntimePrerequisiteDeclaration)
                    && parameters[1].ParameterType == typeof(EngineProfileId);
            });

        return (EngineLaunchPlanMetadata)constructor.Invoke(
            new object[]
            {
                prerequisites,
                BuiltInEngineProfileCatalog.StandardId
            });
    }

    private static EngineExecutableIdentityDeclaration CreateIdentity(
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        string version)
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 3);
        var primary = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
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
                platform,
                architecture,
                version,
                DigestA,
                DigestB,
                DigestC,
                new[] { primary }
            });
    }

    private static EnginePackageLayoutDeclaration CreateLayout(
        EngineExecutableIdentityDeclaration identity,
        string relativePath)
    {
        var componentConstructor = typeof(EnginePackageComponentLayout)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);
        var component = (EnginePackageComponentLayout)componentConstructor.Invoke(
            new object[] { "engine", relativePath });

        var layoutConstructor = typeof(EnginePackageLayoutDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);

        return (EnginePackageLayoutDeclaration)layoutConstructor.Invoke(
            new object[] { identity, new[] { component } });
    }
}
