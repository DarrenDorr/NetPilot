using System.Reflection;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineResourceBudgetContractTests
{
    [Fact]
    public void Trusted_budget_catalog_is_empty_until_workload_evidence_is_promoted()
    {
        Assert.Empty(BuiltInEngineResourceBudgetCatalog.All);
    }

    [Theory]
    [InlineData(BuiltInEngineBackendKind.Zapret2, EngineHostPlatform.Windows)]
    [InlineData(BuiltInEngineBackendKind.Zapret2, EngineHostPlatform.Linux)]
    [InlineData(BuiltInEngineBackendKind.ByeDpi, EngineHostPlatform.Windows)]
    public void TryGet_fails_closed_without_promoted_budget(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform)
    {
        var found = BuiltInEngineResourceBudgetCatalog.TryGet(
            backend,
            platform,
            out var declaration);

        Assert.False(found);
        Assert.Null(declaration);
    }

    [Fact]
    public void Get_rejects_backend_without_promoted_budget()
    {
        Assert.Throws<InvalidOperationException>(() =>
            BuiltInEngineResourceBudgetCatalog.Get(
                BuiltInEngineBackendKind.Zapret2,
                EngineHostPlatform.Windows));
    }

    [Fact]
    public void Budget_declaration_has_no_public_constructor()
    {
        var constructors = typeof(EngineResourceBudgetDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public);

        Assert.Empty(constructors);
    }

    [Fact]
    public void Budget_declaration_exposes_only_typed_resource_metadata()
    {
        var properties = typeof(EngineResourceBudgetDeclaration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(static property => (property.Name, property.PropertyType))
            .ToArray();

        Assert.Contains(
            properties,
            static property => property == (nameof(EngineResourceBudgetDeclaration.Backend), typeof(BuiltInEngineBackendKind)));
        Assert.Contains(
            properties,
            static property => property == (nameof(EngineResourceBudgetDeclaration.Platform), typeof(EngineHostPlatform)));
        Assert.Contains(
            properties,
            static property => property == (nameof(EngineResourceBudgetDeclaration.ConntrackMaxEntries), typeof(uint)));
        Assert.Contains(
            properties,
            static property => property == (nameof(EngineResourceBudgetDeclaration.DelayedQueueMaxBytes), typeof(ulong)));

        Assert.DoesNotContain(properties, static property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(
            properties,
            static property => property.Name.Contains("Path", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Argument", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Command", StringComparison.OrdinalIgnoreCase)
                || property.Name.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Budget_provider_contract_cannot_fall_back_to_weak_numeric_fields()
    {
        var properties = typeof(IEngineResourceBudgetProvider)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public);

        var property = Assert.Single(properties);
        Assert.Equal("ResourceBudgetDeclaration", property.Name);
        Assert.Equal(typeof(EngineResourceBudgetDeclaration), property.PropertyType);
    }
}
