namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Архитектура immutable native artifact set. Это metadata, а не команда запуска.
/// </summary>
public enum EngineBinaryArchitecture
{
    X64,
    Arm64
}

/// <summary>
/// Тип компонента внутри будущего immutable engine package.
/// Идентификатор компонента является логическим token, а не именем/путём файла.
/// </summary>
public enum EngineExecutableComponentKind
{
    PrimaryExecutable,
    SharedLibrary,
    RuntimeLibrary,
    KernelDriver
}

/// <summary>
/// Immutable identity одного компонента. Здесь нет filesystem path или filename.
/// </summary>
public sealed class EngineExecutableComponentIdentity
{
    internal EngineExecutableComponentIdentity(
        string componentId,
        EngineExecutableComponentKind kind,
        string sha256)
    {
        ComponentId = ValidateToken(componentId, nameof(componentId));

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        Kind = kind;
        Sha256 = ValidateSha256(sha256, nameof(sha256));
    }

    public string ComponentId { get; }

    public EngineExecutableComponentKind Kind { get; }

    public string Sha256 { get; }

    internal static string ValidateSha256(string value, string parameterName)
    {
        if (value is null
            || value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException(
                "SHA-256 должен содержать ровно 64 строчных hex-символа.",
                parameterName);
        }

        return value;
    }

    private static string ValidateToken(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || !IsAsciiLowerOrDigit(value[0])
            || !IsAsciiLowerOrDigit(value[^1]))
        {
            throw new ArgumentException(
                "Component id должен быть каноническим ASCII token Net.Pilot.",
                parameterName);
        }

        var previousWasHyphen = false;
        foreach (var character in value)
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
                "Component id должен быть каноническим ASCII token Net.Pilot.",
                parameterName);
        }

        return value;
    }

    private static bool IsAsciiLowerOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>
/// Доверенная immutable identity будущего native package/component set.
/// Она описывает только то, КАКОЙ exact artifact может рассматриваться дальше.
/// Здесь нет path, filename, working directory, arguments, command line, URL,
/// environment, process/service/driver controls или runtime admission grant.
/// </summary>
public sealed class EngineExecutableIdentityDeclaration
{
    private const int MaxComponents = 16;

    internal EngineExecutableIdentityDeclaration(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        string version,
        string packageSha256,
        string layoutSha256,
        string provenanceSha256,
        IReadOnlyList<EngineExecutableComponentIdentity> components)
    {
        if (!Enum.IsDefined(backend))
        {
            throw new ArgumentOutOfRangeException(nameof(backend));
        }

        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform));
        }

        if (!Enum.IsDefined(architecture))
        {
            throw new ArgumentOutOfRangeException(nameof(architecture));
        }

        Version = ValidateVersion(version);
        PackageSha256 = EngineExecutableComponentIdentity.ValidateSha256(
            packageSha256,
            nameof(packageSha256));
        LayoutSha256 = EngineExecutableComponentIdentity.ValidateSha256(
            layoutSha256,
            nameof(layoutSha256));
        ProvenanceSha256 = EngineExecutableComponentIdentity.ValidateSha256(
            provenanceSha256,
            nameof(provenanceSha256));

        ArgumentNullException.ThrowIfNull(components);
        if (components.Count == 0 || components.Count > MaxComponents)
        {
            throw new ArgumentOutOfRangeException(
                nameof(components),
                $"Immutable identity должна содержать от 1 до {MaxComponents} компонентов.");
        }

        var snapshot = components.ToArray();
        if (snapshot.Any(static component => component is null))
        {
            throw new ArgumentException(
                "Immutable identity не может содержать null component.",
                nameof(components));
        }

        if (snapshot
            .Select(static component => component.ComponentId)
            .Distinct(StringComparer.Ordinal)
            .Count() != snapshot.Length)
        {
            throw new ArgumentException(
                "Immutable identity содержит повторяющийся component id.",
                nameof(components));
        }

        if (snapshot.Count(static component =>
                component.Kind == EngineExecutableComponentKind.PrimaryExecutable) != 1)
        {
            throw new ArgumentException(
                "Immutable identity должна содержать ровно один PrimaryExecutable.",
                nameof(components));
        }

        Backend = backend;
        Platform = platform;
        Architecture = architecture;
        Components = Array.AsReadOnly(snapshot);
    }

    public BuiltInEngineBackendKind Backend { get; }

    public EngineHostPlatform Platform { get; }

    public EngineBinaryArchitecture Architecture { get; }

    public string Version { get; }

    public string PackageSha256 { get; }

    public string LayoutSha256 { get; }

    public string ProvenanceSha256 { get; }

    public IReadOnlyList<EngineExecutableComponentIdentity> Components { get; }

    private static string ValidateVersion(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value is "." or ".."
            || value.EndsWith(".", StringComparison.Ordinal)
            || value.Any(static character =>
                character is not (>= 'A' and <= 'Z')
                    and not (>= 'a' and <= 'z')
                    and not (>= '0' and <= '9')
                    and not '.'
                    and not '-'
                    and not '_'
                    and not '+'))
        {
            throw new ArgumentException(
                "Version должна быть каноническим ASCII token без path/URL aliases или separators.",
                nameof(value));
        }

        return value;
    }
}

/// <summary>
/// Будущий consumer получает только trusted immutable identity object.
/// Наличие identity не означает admission и не даёт права запуска.
/// </summary>
public interface IEngineExecutableIdentityProvider
{
    EngineExecutableIdentityDeclaration ExecutableIdentity { get; }
}

/// <summary>
/// Центральный каталог promoted immutable identities.
/// R28.13: намеренно пуст, потому что NetPilot-owned fork build ещё не создан
/// и exact production artifact identity не утверждена.
/// </summary>
public static class BuiltInEngineExecutableIdentityCatalog
{
    private static readonly IReadOnlyList<EngineExecutableIdentityDeclaration> Declarations =
        Array.AsReadOnly(Array.Empty<EngineExecutableIdentityDeclaration>());

    public static IReadOnlyList<EngineExecutableIdentityDeclaration> All => Declarations;

    public static bool TryGet(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture,
        out EngineExecutableIdentityDeclaration? declaration)
    {
        declaration = null;

        foreach (var candidate in Declarations)
        {
            if (candidate.Backend == backend
                && candidate.Platform == platform
                && candidate.Architecture == architecture)
            {
                declaration = candidate;
                return true;
            }
        }

        return false;
    }

    public static EngineExecutableIdentityDeclaration Get(
        BuiltInEngineBackendKind backend,
        EngineHostPlatform platform,
        EngineBinaryArchitecture architecture)
    {
        if (TryGet(backend, platform, architecture, out var declaration)
            && declaration is not null)
        {
            return declaration;
        }

        throw new InvalidOperationException(
            "Для указанной пары backend/platform/architecture отсутствует trusted immutable executable identity Net.Pilot.");
    }
}
