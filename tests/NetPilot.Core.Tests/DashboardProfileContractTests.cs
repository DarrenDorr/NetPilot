using NetPilot.App.Services;

namespace NetPilot.Core.Tests;

public sealed class DashboardProfileContractTests
{
    [Fact]
    public void Dashboard_contract_does_not_expose_legacy_strategy_types_or_names()
    {
        var methods = typeof(IDashboardService).GetMethods();

        Assert.DoesNotContain(
            methods,
            method => method.Name.Contains("Strategy", StringComparison.Ordinal));

        foreach (var method in methods)
        {
            var exposedTypes = ExpandType(method.ReturnType)
                .Concat(method.GetParameters().SelectMany(parameter => ExpandType(parameter.ParameterType)));

            foreach (var type in exposedTypes)
            {
                Assert.False(
                    type.Namespace?.StartsWith("NetPilot.Automation", StringComparison.Ordinal) == true,
                    $"IDashboardService exposes Automation type {type.FullName} through {method.Name}.");
                Assert.False(
                    type.Name.Contains("Strategy", StringComparison.Ordinal),
                    $"IDashboardService exposes legacy strategy type {type.FullName} through {method.Name}.");
            }
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var nested in ExpandType(elementType))
            {
                yield return nested;
            }
        }

        if (!type.IsGenericType)
        {
            yield break;
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var nested in ExpandType(genericArgument))
            {
                yield return nested;
            }
        }
    }
}
