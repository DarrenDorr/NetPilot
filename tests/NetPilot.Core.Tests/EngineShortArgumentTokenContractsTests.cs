using System.Reflection;
using System.Runtime.ExceptionServices;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineShortArgumentTokenContractsTests
{
    [Theory]
    [InlineData(EngineArgumentTokenKind.ShortFlag, "-N", 0UL)]
    [InlineData(EngineArgumentTokenKind.ShortFlag, "-U", 0UL)]
    [InlineData(EngineArgumentTokenKind.ShortFixedPositiveUInt, "-p", 1080UL)]
    [InlineData(EngineArgumentTokenKind.ShortFixedPositiveUInt, "-c", 8UL)]
    [InlineData(EngineArgumentTokenKind.ShortFixedNonNegativeUInt, "-x", 0UL)]
    [InlineData(EngineArgumentTokenKind.ShortLoopbackIpv4, "-i", 0UL)]
    public void Exact_typed_short_forms_are_representable(
        EngineArgumentTokenKind kind,
        string optionName,
        ulong fixedValue)
    {
        var token = CreateToken(kind, optionName, fixedValue);

        Assert.Equal(kind, token.Kind);
        Assert.Equal(optionName, token.OptionName);
        Assert.Equal(fixedValue, token.FixedPositiveUInt);
        Assert.Null(token.ComponentId);
    }

    [Theory]
    [InlineData("--i")]
    [InlineData("-ab")]
    [InlineData("-1")]
    [InlineData("-_")]
    [InlineData("-;")]
    [InlineData("i")]
    [InlineData("")]
    public void Short_option_kind_rejects_non_exact_one_letter_option(string optionName)
    {
        Assert.Throws<ArgumentException>(() =>
            CreateToken(EngineArgumentTokenKind.ShortFlag, optionName));
    }

    [Fact]
    public void Short_positive_integer_rejects_zero()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateToken(
                EngineArgumentTokenKind.ShortFixedPositiveUInt,
                "-p",
                fixedValue: 0));
    }

    [Fact]
    public void Short_non_negative_integer_allows_zero_without_string_source()
    {
        var token = CreateToken(
            EngineArgumentTokenKind.ShortFixedNonNegativeUInt,
            "-x",
            fixedValue: 0);

        Assert.Equal(0UL, token.FixedPositiveUInt);
        Assert.Null(token.ComponentId);
    }

    [Fact]
    public void Loopback_kind_cannot_carry_arbitrary_string_component_or_numeric_value()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateToken(
                EngineArgumentTokenKind.ShortLoopbackIpv4,
                "-i",
                fixedValue: 1));

        Assert.Throws<ArgumentException>(() =>
            CreateToken(
                EngineArgumentTokenKind.ShortLoopbackIpv4,
                "-i",
                componentId: "example.com"));
    }

    [Fact]
    public void Production_argument_catalog_remains_empty_after_short_argv_support()
    {
        Assert.Empty(BuiltInEngineArgumentPlanCatalog.All);
    }

    [Fact]
    public void Token_contract_still_has_no_public_constructor_or_generic_string_value()
    {
        Assert.Empty(typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));

        var internalConstructor = typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);

        Assert.Collection(
            internalConstructor.GetParameters(),
            first => Assert.Equal(typeof(EngineArgumentTokenKind), first.ParameterType),
            second => Assert.Equal(typeof(string), second.ParameterType),
            third => Assert.Equal(typeof(ulong), third.ParameterType),
            fourth => Assert.Equal(typeof(string), fourth.ParameterType));
    }

    private static EngineArgumentTokenTemplate CreateToken(
        EngineArgumentTokenKind kind,
        string optionName,
        ulong fixedValue = 0,
        string? componentId = null)
    {
        var constructor = typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 4);

        try
        {
            return (EngineArgumentTokenTemplate)constructor.Invoke(
                new object?[] { kind, optionName, fixedValue, componentId });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }
}
