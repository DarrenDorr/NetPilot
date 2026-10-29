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
/// API и остаётся связан с живым R28.17 handoff.
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
/// Raw user CLI/config/script/path/URL/env не принимаются. Process.Start отсутствует.
/// </summary>
public static class WindowsTypedEngineArgumentPlanCompiler
{
    private const int MaxResolvedArgumentCharacters = 16 * 1024;
    private const int MaxAggregateArgumentCharacters = 64 * 1024;

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
        var result = new string[declaration.Arguments.Count];
        var aggregateCharacters = 0;

        for (var index = 0; index < declaration.Arguments.Count; index++)
        {
            var template = declaration.Arguments[index];
            var resolved = template.Kind switch
            {
                EngineArgumentTokenKind.Flag => template.OptionName,
                EngineArgumentTokenKind.FixedPositiveUInt =>
                    AppendUnsigned(template.OptionName, template.FixedPositiveUInt),
                EngineArgumentTokenKind.ConntrackMaxEntries =>
                    AppendUnsigned(template.OptionName, budget.ConntrackMaxEntries),
                EngineArgumentTokenKind.DelayedQueueMaxBytes =>
                    AppendUnsigned(template.OptionName, budget.DelayedQueueMaxBytes),
                EngineArgumentTokenKind.VerifiedComponentPath =>
                    AppendVerifiedComponentPath(
                        template.OptionName,
                        template.ComponentId!,
                        preLaunchHandoff.VerifiedPackage),
                _ => throw new WindowsEngineArgumentPlanException(
                    "UnknownArgumentTokenKind",
                    "Typed argument plan содержит неизвестный token kind.")
            };

            ValidateResolvedArgument(resolved);
            aggregateCharacters = checked(aggregateCharacters + resolved.Length);
            if (aggregateCharacters > MaxAggregateArgumentCharacters)
            {
                throw new WindowsEngineArgumentPlanException(
                    "ArgumentPlanTooLarge",
                    "Typed argument plan превышает общий допустимый размер.");
            }

            result[index] = resolved;
        }

        preLaunchHandoff.EnsureReady();

        return new WindowsCompiledEngineArgumentPlan(
            preLaunchHandoff,
            declaration,
            Array.AsReadOnly(result));
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

    private static string AppendUnsigned(string optionName, ulong value)
    {
        if (value == 0)
        {
            throw new WindowsEngineArgumentPlanException(
                "ZeroArgumentValue",
                "Typed numeric argument обязан иметь положительное значение.");
        }

        return string.Concat(
            optionName,
            "=",
            value.ToString(CultureInfo.InvariantCulture));
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

    private static void ValidateResolvedArgument(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > MaxResolvedArgumentCharacters
            || !value.StartsWith("--", StringComparison.Ordinal)
            || value.Any(static character => character is '\0' or '\r' or '\n'))
        {
            throw new WindowsEngineArgumentPlanException(
                "ResolvedArgumentInvalid",
                "Resolved typed argument имеет недопустимый формат.");
        }
    }
}
