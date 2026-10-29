using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineRuntimePrerequisiteContractTests
{
    [Fact]
    public void Trusted_prerequisite_catalog_is_empty_without_promoted_resource_budgets()
    {
        Assert.Empty(BuiltInEngineRuntimePrerequisiteCatalog.All);
    }

    [Theory]
    [InlineData(BuiltInEngineBackendKind.Zapret2, EngineHostPlatform.Windows)]
    [InlineData(BuiltInEngineBackendKind.Zapret2, EngineHostPlatform.Linux)]
    [InlineData(BuiltInEngineBackendKind.ByeDpi, EngineHostPlatform.Windows)]
    public void TryGet_fails_closed_when_resource_budget_is_missing(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        var found = BuiltInEngineRuntimePrerequisiteCatalog.TryGet(
            backend,
            platform,
            out var declaration);

        Assert.False(found);
        Assert.Null(declaration);
    }

    [Fact]
    public void Get_rejects_incomplete_prerequisite_set()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineRuntimePrerequisiteCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows));
    }

    [Fact]
    public void Prerequisite_declaration_has_no_public_constructor()
    {
        var constructors = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        Assert.Empty(constructors);
    }

    [Fact]
    public void Prerequisite_provider_exposes_one_composite_trusted_object()
    {
        var properties = typeof(IEngineRuntimePrerequisiteProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        var property = Assert.Single(properties);
        Assert.Equal("RuntimePrerequisites", property.Name);
        Assert.Equal(typeof(EngineRuntimePrerequisiteDeclaration), property.PropertyType);
    }

    [Fact]
    public void Prerequisite_declaration_contains_no_execution_surface()
    {
        var properties = typeof(EngineRuntimePrerequisiteDeclaration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.Contains(properties, static property =>
            property.Name == nameof(EngineRuntimePrerequisiteDeclaration.CapabilityDeclaration)
            && property.PropertyType == typeof(EngineBackendCapabilityDeclaration));
        Assert.Contains(properties, static property =>
            property.Name == nameof(EngineRuntimePrerequisiteDeclaration.ResourceBudgetDeclaration)
            && property.PropertyType == typeof(EngineResourceBudgetDeclaration));

        Assert.DoesNotContain(
            properties,
            static property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Executable", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Argument", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Process", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Driver", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Internal_constructor_rejects_backend_or_platform_mismatch()
    {
        var budgetConstructor = Assert.Single(
            typeof(EngineResourceBudgetDeclaration)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        var linuxBudget = (EngineResourceBudgetDeclaration)budgetConstructor.Invoke(
            new object[]
            {
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Linux,
                (uint)1,
                (ulong)1
            });

        var windowsCapability = BuiltInEngineBackendCapabilityCatalog.Get(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows);

        var prerequisiteConstructor = Assert.Single(
            typeof(EngineRuntimePrerequisiteDeclaration)
                .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic));

        var exception = Assert.Throws<TargetInvocationException>(() =>
            prerequisiteConstructor.Invoke(
                new object[]
                {
                    windowsCapability,
                    linuxBudget
                }));

        Assert.IsType<ArgumentException>(exception.InnerException);
    }
}
