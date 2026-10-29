using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineLaunchPlanMetadataContractTests
{
    [Fact]
    public void Trusted_launch_plan_metadata_catalog_is_empty_without_prerequisites()
    {
        Assert.Empty(BuiltInEngineLaunchPlanMetadataCatalog.All);
    }

    [Fact]
    public void TryGet_fails_closed_without_complete_trusted_prerequisites()
    {
        var found = BuiltInEngineLaunchPlanMetadataCatalog.TryGet(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            BuiltInEngineProfileCatalog.StandardId,
            out var metadata);

        Assert.False(found);
        Assert.Null(metadata);
    }

    [Fact]
    public void Get_rejects_missing_launch_plan_metadata()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineLaunchPlanMetadataCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows,
                BuiltInEngineProfileCatalog.StandardId));
    }

    [Fact]
    public void Launch_plan_metadata_has_no_public_constructor()
    {
        var constructors = typeof(EngineLaunchPlanMetadata)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        Assert.Empty(constructors);
    }

    [Fact]
    public void Launch_plan_provider_exposes_only_non_executable_metadata()
    {
        var properties = typeof(IEngineLaunchPlanMetadataProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        var property = Assert.Single(properties);
        Assert.Equal("LaunchPlanMetadata", property.Name);
        Assert.Equal(typeof(EngineLaunchPlanMetadata), property.PropertyType);
    }

    [Fact]
    public void Launch_plan_metadata_contains_no_execution_surface()
    {
        var properties = typeof(EngineLaunchPlanMetadata)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Contains(properties, static property =>
            property.Name == nameof(EngineLaunchPlanMetadata.RuntimePrerequisites)
            && property.PropertyType == typeof(EngineRuntimePrerequisiteDeclaration));
        Assert.Contains(properties, static property =>
            property.Name == nameof(EngineLaunchPlanMetadata.ProfileId)
            && property.PropertyType == typeof(EngineProfileId));

        Assert.DoesNotContain(properties, static property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            static property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Executable", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Argument", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Environment", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Process", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Service", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Driver", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Socket", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Internal_constructor_rejects_profile_not_supported_by_trusted_capabilities()
    {
        var budgetConstructor = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 4
                    && parameters[0].ParameterType == typeof(BuiltInEngineBackendKind)
                    && parameters[1].ParameterType == typeof(EngineHostPlatform)
                    && parameters[2].ParameterType == typeof(uint)
                    && parameters[3].ParameterType == typeof(ulong);
            });

        var testOnlyBudget = (EngineResourceBudgetDeclaration)budgetConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.ByeDpi,
                EngineHostPlatform.Windows,
                (uint)1,
                (ulong)1
            });

        var proxyOnlyCapability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.ByeDpi,
            EngineHostPlatform.Windows);

        var prerequisiteConstructor = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length == 2
                    && parameters[0].ParameterType == typeof(EngineBackendCapabilityDeclaration)
                    && parameters[1].ParameterType == typeof(EngineResourceBudgetDeclaration);
            });

        var prerequisites = (EngineRuntimePrerequisiteDeclaration)prerequisiteConstructor.Invoke(
            new object[]
            {
                proxyOnlyCapability,
                testOnlyBudget
            });

        var launchMetadataConstructor = Assert.Single(
            typeof(EngineLaunchPlanMetadata)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        var exception = Assert.Throws<TargetInvocationException>(() =>
            launchMetadataConstructor.Invoke(
                new object[]
                {
                    prerequisites,
                    BuiltInEngineProfileCatalog.YouTubeId
                }));

        var inner = Assert.IsType<EngineProfileRuntimeException>(exception.InnerException);
        Assert.Equal("UnsupportedProfileCapabilities", inner.Code);
    }
}
