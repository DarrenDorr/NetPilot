using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineLaunchIdentityBindingContractTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Trusted_binding_catalog_is_empty_without_promoted_launch_metadata_and_identity()
    {
        Assert.Empty(BuiltInEngineLaunchIdentityBindingCatalog.All);
    }

    [Fact]
    public void TryGet_fails_closed_without_promoted_inputs()
    {
        var found = BuiltInEngineLaunchIdentityBindingCatalog.TryGet(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            BuiltInEngineProfileCatalog.StandardId,
            out var binding);

        Assert.False(found);
        Assert.Null(binding);
    }

    [Fact]
    public void Get_rejects_missing_binding()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineLaunchIdentityBindingCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                EngineBinaryArchitecture.X64,
                BuiltInEngineProfileCatalog.StandardId));
    }

    [Fact]
    public void Binding_has_no_public_constructor()
    {
        Assert.Empty(typeof(EngineLaunchIdentityBinding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Provider_exposes_only_binding_object()
    {
        var properties = typeof(IEngineLaunchIdentityBindingProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        var property = Assert.Single(properties);
        Assert.Equal("LaunchIdentityBinding", property.Name);
        Assert.Equal(typeof(EngineLaunchIdentityBinding), property.PropertyType);
    }

    [Fact]
    public void Binding_contains_no_path_argument_or_runtime_control_surface()
    {
        var properties = typeof(EngineLaunchIdentityBinding)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.DoesNotContain(
            properties,
            static property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("FileName", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Argument", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Process", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Working", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Script", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Lua", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Admission", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Internal_constructor_accepts_aligned_metadata_identity_and_architecture()
    {
        var metadata = CreateLaunchMetadata(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            BuiltInEngineProfileCatalog.DiscordId);
        var identity = CreateIdentity(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var constructor = GetBindingConstructor();

        var binding = (EngineLaunchIdentityBinding)constructor.Invoke(
            new object[]
            {
                metadata,
                identity,
                EngineBinaryArchitecture.X64
            });

        Assert.Equal("zapret2", binding.EngineId);
        Assert.Equal(BuiltInEngineBackendKind.Zapret2, binding.Backend);
        Assert.Equal(EngineHostPlatform.Windows, binding.Platform);
        Assert.Equal(EngineBinaryArchitecture.X64, binding.Architecture);
        Assert.Equal(BuiltInEngineProfileCatalog.DiscordId, binding.ProfileId);
        Assert.Same(metadata, binding.LaunchPlanMetadata);
        Assert.Same(identity, binding.ExecutableIdentity);
    }

    [Fact]
    public void Internal_constructor_rejects_backend_or_platform_mismatch()
    {
        var metadata = CreateLaunchMetadata(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            BuiltInEngineProfileCatalog.StandardId);
        var identity = CreateIdentity(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Linux,
            EngineBinaryArchitecture.X64);
        var constructor = GetBindingConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    metadata,
                    identity,
                    EngineBinaryArchitecture.X64
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    [Fact]
    public void Internal_constructor_rejects_architecture_mismatch()
    {
        var metadata = CreateLaunchMetadata(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            BuiltInEngineProfileCatalog.StandardId);
        var identity = CreateIdentity(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64);
        var constructor = GetBindingConstructor();

        var exception = Assert.Throws<TargetInvocationException>(() =>
            constructor.Invoke(
                new object[]
                {
                    metadata,
                    identity,
                    EngineBinaryArchitecture.Arm64
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }

    private static EngineLaunchPlanMetadata CreateLaunchMetadata(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineProfileId profileId)
    {
        var capability = BuiltInEngineBackendCapabilityCatalog.Get(backend, platform);
        var budget = CreateBudget(backend, platform);
        var prerequisites = CreatePrerequisites(capability, budget);

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
            new object[] { prerequisites, profileId });
    }

    private static EngineResourceBudgetDeclaration CreateBudget(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        var constructor = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 4
                    && parameters[0].ParameterType == typeof(BuiltInEngineBackendKind)
                    && parameters[1].ParameterType == typeof(EngineHostPlatform)
                    && parameters[2].ParameterType == typeof(uint)
                    && parameters[3].ParameterType == typeof(ulong);
            });

        return (EngineResourceBudgetDeclaration)constructor.Invoke(
            new object[] { backend, platform, (uint)1, (ulong)1 });
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

    private static EngineExecutableIdentityDeclaration CreateIdentity(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture)
    {
        var componentConstructor = typeof(EngineExecutableComponentIdentity)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(string)
                    && parameters[1].ParameterType == typeof(EngineExecutableComponentKind)
                    && parameters[2].ParameterType == typeof(string);
            });

        var primary = (EngineExecutableComponentIdentity)componentConstructor.Invoke(
            new object[]
            {
                "engine",
                EngineExecutableComponentKind.PrimaryExecutable,
                DigestA
            });

        var identityConstructor = typeof(EngineExecutableIdentityDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 8
                    && parameters[0].ParameterType == typeof(BuiltInEngineBackendKind)
                    && parameters[1].ParameterType == typeof(EngineHostPlatform)
                    && parameters[2].ParameterType == typeof(EngineBinaryArchitecture)
                    && parameters[3].ParameterType == typeof(string)
                    && parameters[4].ParameterType == typeof(string)
                    && parameters[5].ParameterType == typeof(string)
                    && parameters[6].ParameterType == typeof(string)
                    && parameters[7].ParameterType == typeof(IReadOnlyList<EngineExecutableComponentIdentity>);
            });

        return (EngineExecutableIdentityDeclaration)identityConstructor.Invoke(
            new object[]
            {
                backend,
                platform,
                architecture,
                "1.0.6-np.1",
                DigestA,
                DigestB,
                DigestC,
                new[] { primary }
            });
    }

    private static ConstructorInfo GetBindingConstructor() =>
        typeof(EngineLaunchIdentityBinding)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(EngineLaunchPlanMetadata)
                    && parameters[1].ParameterType == typeof(EngineExecutableIdentityDeclaration)
                    && parameters[2].ParameterType == typeof(EngineBinaryArchitecture);
            });
}
