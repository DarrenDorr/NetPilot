namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Типизированный источник будущих argv tokens. Намеренно отсутствует generic
/// string-value source: raw CLI/Lua/script/path/URL/env не представимы.
/// </summary>
public enum EngineArgumentTokenKind
{
    Flag,
    FixedPositiveUInt,
    ConntrackMaxEntries,
    DelayedQueueMaxBytes,
    VerifiedComponentPath,
    ShortFlag,
    ShortFixedPositiveUInt,
    ShortFixedNonNegativeUInt,
    ShortLoopbackIpv4
}

/// <summary>
/// Один trusted argv token template. Длинные options остаются каноническими
/// `--lower-ascii-token`. Короткие options разрешены только специальными typed
/// kinds и имеют форму ровно `-X`/`-x`. Generic string value отсутствует.
/// </summary>
public sealed class EngineArgumentTokenTemplate
{
    internal EngineArgumentTokenTemplate(
        EngineArgumentTokenKind kind,
        string optionName,
        ulong fixedPositiveUInt = 0,
        string? componentId = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        OptionName = ValidateOptionName(kind, optionName);

        switch (kind)
        {
            case EngineArgumentTokenKind.Flag:
            case EngineArgumentTokenKind.ConntrackMaxEntries:
            case EngineArgumentTokenKind.DelayedQueueMaxBytes:
            case EngineArgumentTokenKind.ShortFlag:
            case EngineArgumentTokenKind.ShortLoopbackIpv4:
                if (fixedPositiveUInt != 0 || componentId is not null)
                {
                    throw new ArgumentException(
                        "Этот typed argument kind не принимает дополнительное значение.");
                }
                break;

            case EngineArgumentTokenKind.FixedPositiveUInt:
            case EngineArgumentTokenKind.ShortFixedPositiveUInt:
                if (fixedPositiveUInt == 0 || componentId is not null)
                {
                    throw new ArgumentException(
                        "Positive integer typed argument требует только положительное целое значение.");
                }
                break;

            case EngineArgumentTokenKind.ShortFixedNonNegativeUInt:
                if (componentId is not null)
                {
                    throw new ArgumentException(
                        "ShortFixedNonNegativeUInt принимает только неотрицательное целое значение.");
                }
                break;

            case EngineArgumentTokenKind.VerifiedComponentPath:
                if (fixedPositiveUInt != 0 || string.IsNullOrEmpty(componentId))
                {
                    throw new ArgumentException(
                        "VerifiedComponentPath требует только logical component id.");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        FixedPositiveUInt = fixedPositiveUInt;
        ComponentId = componentId;
    }

    public EngineArgumentTokenKind Kind { get; }

    public string OptionName { get; }

    public ulong FixedPositiveUInt { get; }

    public string? ComponentId { get; }

    private static string ValidateOptionName(
        EngineArgumentTokenKind kind,
        string value)
    {
        if (IsShortOptionKind(kind))
        {
            if (value is null
                || value.Length != 2
                || value[0] != '-'
                || !IsAsciiLetter(value[1]))
            {
                throw new ArgumentException(
                    "Short option name должен иметь exact форму `-X` или `-x`.",
                    nameof(value));
            }

            return value;
        }

        if (string.IsNullOrEmpty(value)
            || value.Length is < 4 or > 64
            || !value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Option name должен быть каноническим `--lower-ascii-token`.",
                nameof(value));
        }

        var body = value.AsSpan(2);
        if (!IsAsciiLowerOrDigit(body[0]) || !IsAsciiLowerOrDigit(body[^1]))
        {
            throw new ArgumentException(
                "Option name должен быть каноническим `--lower-ascii-token`.",
                nameof(value));
        }

        var previousWasHyphen = false;
        foreach (var character in body)
        {
            if (IsAsciiLowerOrDigit(character))
            {
                previousWasHyphen = false;
                continue;
            }

            if (character == '-' && !previousWasHyphen)
            {
                previousWasHyphen = true;
                continue;
            }

            throw new ArgumentException(
                "Option name должен быть каноническим `--lower-ascii-token` без path/URL/shell aliases.",
                nameof(value));
        }

        return value;
    }

    private static bool IsShortOptionKind(EngineArgumentTokenKind kind) =>
        kind is EngineArgumentTokenKind.ShortFlag
            or EngineArgumentTokenKind.ShortFixedPositiveUInt
            or EngineArgumentTokenKind.ShortFixedNonNegativeUInt
            or EngineArgumentTokenKind.ShortLoopbackIpv4;

    private static bool IsAsciiLowerOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static bool IsAsciiLetter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}

/// <summary>
/// Trusted non-executable argv plan declaration. Он связан с exact launch identity
/// и содержит только typed token sources. Здесь нет ProcessStartInfo/command line.
/// </summary>
public sealed class EngineArgumentPlanDeclaration
{
    private const int MaxArguments = 128;

    internal EngineArgumentPlanDeclaration(
        EngineLaunchIdentityBinding launchIdentityBinding,
        IReadOnlyList<EngineArgumentTokenTemplate> arguments)
    {
        ArgumentNullException.ThrowIfNull(launchIdentityBinding);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0 || arguments.Count > MaxArguments)
        {
            throw new ArgumentOutOfRangeException(
                nameof(arguments),
                $"Typed argument plan должен содержать от 1 до {MaxArguments} tokens.");
        }

        var snapshot = arguments.ToArray();
        if (snapshot.Any(static argument => argument is null))
        {
            throw new ArgumentException(
                "Typed argument plan не может содержать null token.",
                nameof(arguments));
        }

        foreach (var argument in snapshot)
        {
            if (argument.Kind != EngineArgumentTokenKind.VerifiedComponentPath)
            {
                continue;
            }

            if (!launchIdentityBinding.ExecutableIdentity.Components.Any(component =>
                    string.Equals(
                        component.ComponentId,
                        argument.ComponentId,
                        StringComparison.Ordinal)))
            {
                throw new ArgumentException(
                    "Typed argument plan ссылается на component id вне exact immutable identity.",
                    nameof(arguments));
            }
        }

        LaunchIdentityBinding = launchIdentityBinding;
        Arguments = Array.AsReadOnly(snapshot);
    }

    public EngineLaunchIdentityBinding LaunchIdentityBinding { get; }

    public IReadOnlyList<EngineArgumentTokenTemplate> Arguments { get; }
}

/// <summary>
/// Production catalog typed argument plans. Он намеренно остаётся пустым: R33
/// добавляет только безопасную выразимость short argv, но не даёт ADMIT ни одному
/// native engine до отдельного runtime/admission решения.
/// </summary>
public static class BuiltInEngineArgumentPlanCatalog
{
    private static readonly IReadOnlyList<EngineArgumentPlanDeclaration> Declarations =
        Array.AsReadOnly(Array.Empty<EngineArgumentPlanDeclaration>());

    public static IReadOnlyList<EngineArgumentPlanDeclaration> All => Declarations;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        EngineProfileId profileId,
        out EngineArgumentPlanDeclaration? declaration)
    {
        declaration = null;

        foreach (var candidate in Declarations)
        {
            var binding = candidate.LaunchIdentityBinding;
            if (binding.Backend == backend
                && binding.Platform == platform
                && binding.Architecture == architecture
                && binding.ProfileId == profileId)
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    public static EngineArgumentPlanDeclaration Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        EngineProfileId profileId)
    {
        if (TryGet(
                backend,
                platform,
                architecture,
                profileId,
                out var declaration)
            && declaration is not null)
        {
            return declaration;
        }

        throw new InvalidOperationException(
            "Для указанного backend/platform/architecture/profile отсутствует trusted typed argument plan Net.Pilot.");
    }
}
