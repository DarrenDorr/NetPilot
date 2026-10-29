using System.Globalization;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsEngineArgumentPlanException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Уже разрешённый typed argv plan для Windows. Он не содержит executable launch
/// API и остаётся связан с живым verified pre-launch handoff.
/// </summary>
public sealed class WindowsCompiledEngineArgumentPlan
{
    internal WindowsCompiledEngineArgumentPlan(
        WindowsVerifiedEnginePreLaunchHandoff preLaunchHandoff,
        EngineArgumentPlanDeclaration declaration,
        IReadOnlyList<string> arguments)
    {
        PreLaunchHandoff = preLaunchHandoff;
        Declaration = declaration;
        Arguments = arguments;
    }

    public WindowsVerifiedEnginePreLaunchHandoff PreLaunchHandoff { get; }

    public EngineArgumentPlanDeclaration Declaration { get; }

    public IReadOnlyList<string> Arguments { get; }

    public void EnsureReady() => PreLaunchHandoff.EnsureReady();
}

/// <summary>
/// Компилирует только typed trusted token sources в отдельные argv strings.
/// Short options поддерживаются только специальными typed kinds. Raw user
/// CLI/config/script/path/URL/env не принимаются. Process.Start отсутствует.
/// </summary>
public static class WindowsTypedEngineArgumentPlanCompiler
{
    private const int MaxResolvedArgumentCharacters = 16 * 1024;
    private const int MaxAggregateArgumentCharacters = 64 * 1024;

    private enum ResolvedArgumentShape
    {
        LongOption,
        ShortOption,
        UnsignedDecimal,
        LoopbackIpv4
    }

    public static WindowsCompiledEngineArgumentPlan Compile(
        WindowsVerifiedEnginePreLaunchHandoff preLaunchHandoff,
        EngineArgumentPlanDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(preLaunchHandoff);
        ArgumentNullException.ThrowIfNull(declaration);

        preLaunchHandoff.EnsureReady();
        EnsureDeclarationMatchesHandoff(preLaunchHandoff, declaration);

        var binding = preLaunchHandoff.LaunchIdentityBinding;
        var budget = binding.LaunchPlanMetadata
            .RuntimePrerequisites
            .ResourceBudgetDeclaration;
        var result = new List<string>(declaration.Arguments.Count * 2);
        var aggregateCharacters = 0;

        foreach (var template in declaration.Arguments)
        {
            switch (template.Kind)
            {
                case EngineArgumentTokenKind.Flag:
                    AddResolved(
                        result,
                        template.OptionName,
                        ResolvedArgumentShape.LongOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.FixedPositiveUInt:
                    AddResolved(
                        result,
                        AppendUnsigned(template.OptionName, template.FixedPositiveUInt),
                        ResolvedArgumentShape.LongOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.ConntrackMaxEntries:
                    AddResolved(
                        result,
                        AppendUnsigned(template.OptionName, budget.ConntrackMaxEntries),
                        ResolvedArgumentShape.LongOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.DelayedQueueMaxBytes:
                    AddResolved(
                        result,
                        AppendUnsigned(template.OptionName, budget.DelayedQueueMaxBytes),
                        ResolvedArgumentShape.LongOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.VerifiedComponentPath:
                    AddResolved(
                        result,
                        AppendVerifiedComponentPath(
                            template.OptionName,
                            template.ComponentId!,
                            preLaunchHandoff.VerifiedPackage),
                        ResolvedArgumentShape.LongOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.ShortFlag:
                    AddResolved(
                        result,
                        template.OptionName,
                        ResolvedArgumentShape.ShortOption,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.ShortFixedPositiveUInt:
                    AddResolved(
                        result,
                        template.OptionName,
                        ResolvedArgumentShape.ShortOption,
                        ref aggregateCharacters);
                    AddResolved(
                        result,
                        FormatPositiveUnsigned(template.FixedPositiveUInt),
                        ResolvedArgumentShape.UnsignedDecimal,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.ShortFixedNonNegativeUInt:
                    AddResolved(
                        result,
                        template.OptionName,
                        ResolvedArgumentShape.ShortOption,
                        ref aggregateCharacters);
                    AddResolved(
                        result,
                        template.FixedPositiveUInt.ToString(CultureInfo.InvariantCulture),
                        ResolvedArgumentShape.UnsignedDecimal,
                        ref aggregateCharacters);
                    break;

                case EngineArgumentTokenKind.ShortLoopbackIpv4:
                    AddResolved(
                        result,
                        template.OptionName,
                        ResolvedArgumentShape.ShortOption,
                        ref aggregateCharacters);
                    AddResolved(
                        result,
                        "127.0.0.1",
                        ResolvedArgumentShape.LoopbackIpv4,
                        ref aggregateCharacters);
                    break;

                default:
                    throw new WindowsEngineArgumentPlanException(
                        "UnknownArgumentTokenKind",
                        "Typed argument plan содержит неизвестный token kind.");
            }
        }

        preLaunchHandoff.EnsureReady();

        return new WindowsCompiledEngineArgumentPlan(
            preLaunchHandoff,
            declaration,
            result.AsReadOnly());
    }

    private static void EnsureDeclarationMatchesHandoff(
        WindowsVerifiedEnginePreLaunchHandoff handoff,
        EngineArgumentPlanDeclaration declaration)
    {
        var expected = handoff.LaunchIdentityBinding;
        var actual = declaration.LaunchIdentityBinding;

        if (actual.Backend != expected.Backend
            || actual.Platform != expected.Platform
            || actual.Architecture != expected.Architecture
            || actual.ProfileId != expected.ProfileId
            || !ReferenceEquals(
                actual.ExecutableIdentity,
                expected.ExecutableIdentity))
        {
            throw new WindowsEngineArgumentPlanException(
                "ArgumentPlanBindingMismatch",
                "Typed argument plan не относится к exact pre-launch identity handoff.");
        }
    }

    private static string AppendUnsigned(string optionName, ulong value) =>
        string.Concat(optionName, "=", FormatPositiveUnsigned(value));

    private static string FormatPositiveUnsigned(ulong value)
    {
        if (value == 0)
        {
            throw new WindowsEngineArgumentPlanException(
                "ZeroArgumentValue",
                "Typed positive numeric argument обязан иметь положительное значение.");
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string AppendVerifiedComponentPath(
        string optionName,
        string componentId,
        WindowsVerifiedEnginePackage verifiedPackage)
    {
        var matches = verifiedPackage.Components
            .Where(component => string.Equals(
                component.ComponentId,
                componentId,
                StringComparison.Ordinal))
            .ToArray();

        if (matches.Length != 1)
        {
            throw new WindowsEngineArgumentPlanException(
                "VerifiedComponentUnavailable",
                "Typed argument ссылается не на один exact verified component.");
        }

        var fullPath = Path.GetFullPath(matches[0].FullPath);
        return string.Concat(optionName, "=", fullPath);
    }

    private static void AddResolved(
        List<string> result,
        string value,
        ResolvedArgumentShape shape,
        ref int aggregateCharacters)
    {
        ValidateResolvedArgument(value, shape);
        aggregateCharacters = checked(aggregateCharacters + value.Length);
        if (aggregateCharacters > MaxAggregateArgumentCharacters)
        {
            throw new WindowsEngineArgumentPlanException(
                "ArgumentPlanTooLarge",
                "Typed argument plan превышает общий допустимый размер.");
        }

        result.Add(value);
    }

    private static void ValidateResolvedArgument(
        string value,
        ResolvedArgumentShape shape)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxResolvedArgumentCharacters
            || value.Any(static character => character is '\0' or '\r' or '\n'))
        {
            throw InvalidResolvedArgument();
        }

        var valid = shape switch
        {
            ResolvedArgumentShape.LongOption =>
                value.StartsWith("--", StringComparison.Ordinal),
            ResolvedArgumentShape.ShortOption =>
                value.Length == 2
                && value[0] == '-'
                && IsAsciiLetter(value[1]),
            ResolvedArgumentShape.UnsignedDecimal =>
                value.All(static character => character is >= '0' and <= '9'),
            ResolvedArgumentShape.LoopbackIpv4 =>
                string.Equals(value, "127.0.0.1", StringComparison.Ordinal),
            _ => false
        };

        if (!valid)
        {
            throw InvalidResolvedArgument();
        }
    }

    private static WindowsEngineArgumentPlanException InvalidResolvedArgument() =>
        new(
            "ResolvedArgumentInvalid",
            "Resolved typed argument имеет недопустимый формат.");

    private static bool IsAsciiLetter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
