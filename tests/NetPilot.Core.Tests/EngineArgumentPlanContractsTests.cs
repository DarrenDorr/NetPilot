using System.Reflection;
using System.Runtime.ExceptionServices;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Core.Tests;

public sealed class EngineArgumentPlanContractsTests
{
    private const string DigestA =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DigestB =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private const string DigestC =
        "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void Production_argument_plan_catalog_is_empty_fail_closed()
    {
        Assert.Empty(BuiltInEngineArgumentPlanCatalog.All);
        Assert.False(BuiltInEngineArgumentPlanCatalog.TryGet(
            BuiltInEngineBackendKind.Zapret2,
            EngineHostPlatform.Windows,
            EngineBinaryArchitecture.X64,
            BuiltInEngineProfileCatalog.StandardId,
            out var declaration));
        Assert.Null(declaration);
    }

    [Fact]
    public void Trusted_argument_types_have_no_public_constructors()
    {
        Assert.Empty(typeof(EngineArgumentTokenTemplate)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(EngineArgumentPlanDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Theory]
    [InlineData("--mode=value")]
    [InlineData("--path/to/file")]
    [InlineData("--url:https")]
    [InlineData("--two words")]
    [InlineData("--semi;colon")]
    public void Option_name_rejects_raw_value_path_url_or_shell_syntax(string optionName)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreateToken(EngineArgumentTokenKind.Flag, optionName));

        Assert.Contains("Option name", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixed_positive_uint_rejects_zero()
    {
        Assert.Throws<ArgumentException>(() =>
            CreateToken(
                EngineArgumentTokenKind.FixedPositiveUInt,
                "--limit"));
    }

    [Fact]
    public void Component_path_token_must_reference_exact_immutable_component()
    {
        var binding = CreateBinding();
        var token = CreateToken(
            EngineArgumentTokenKind.VerifiedComponentPath,
            "--component",
            componentId: "missing-component");

        Assert.Throws<ArgumentException>(() =>
            CreatePlan(binding, new[] { token }));
    }

    [Fact]
    public void Valid_plan_contains_only_typed_tokens_and_is_read_only()
    {
        var binding = CreateBinding();
        var arguments = new[]
        {
            CreateToken(EngineArgumentTokenKind.Flag, "--safe-flag"),
            CreateToken(
                EngineArgumentTokenKind.FixedPositiveUInt,
                "--retry",
                fixedPositiveUInt: 7),
            CreateToken(EngineArgumentTokenKind.ConntrackMaxEntries, "--conntrack-limit"),
            CreateToken(EngineArgumentTokenKind.DelayedQueueMaxBytes, "--queue-bytes"),
            CreateToken(
                EngineArgumentTokenKind.VerifiedComponentPath,
                "--component",
                componentId: "engine")
        };

        var plan = CreatePlan(binding, arguments);

        Assert.Same(binding, plan.LaunchIdentityBinding);
        Assert.Equal(arguments.Length, plan.Arguments.Count);
        Assert.IsAssignableFrom<IReadOnlyList<EngineArgumentTokenTemplate>>(plan.Arguments);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<EngineArgumentTokenTemplate>)plan.Arguments)
                .Add(CreateToken(EngineArgumentTokenKind.Flag, "--extra")));
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

    private static EngineArgumentPlanDeclaration CreatePlan(
        EngineLaunchIdentityBinding binding,
        IReadOnlyList<EngineArgumentTokenTemplate> arguments)
    {
        var constructor = typeof(EngineArgumentPlanDeclaration)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(static candidate => candidate.GetParameters().Length == 2);

        try
        {
            return (EngineArgumentPlanDeclaration)constructor.Invoke(
                new object[] { binding, arguments });
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static EngineLaunchIdentityBinding CreateBinding()
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
        var identity = (EngineExecutableIdentityDeclaration)identityConstructor.Invoke(
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
}
