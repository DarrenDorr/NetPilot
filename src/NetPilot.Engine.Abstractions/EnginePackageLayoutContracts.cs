namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Доверенное сопоставление logical component id с фиксированным относительным
/// путём внутри managed engine package. RelativePath никогда не является
/// пользовательским вводом и не содержит абсолютный/rooted path.
/// </summary>
public sealed class EnginePackageComponentLayout
{
    internal EnginePackageComponentLayout(string componentId, string relativePath)
    {
        ComponentId = ValidateComponentId(componentId);
        RelativePath = ValidateRelativePath(relativePath);
    }

    public string ComponentId { get; }

    /// <summary>
    /// Канонический относительный путь с '/' как separator.
    /// </summary>
    public string RelativePath { get; }

    private static string ValidateComponentId(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 64
            || value[0] == '-'
            || value[^1] == '-')
        {
            throw new ArgumentException(
                "Component id должен быть каноническим logical token Net.Pilot.",
                nameof(value));
        }

        var previousHyphen = false;
        foreach (var character in value)
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                previousHyphen = false;
                continue;
            }

            if (character == '-' && !previousHyphen)
            {
                previousHyphen = true;
                continue;
            }

            throw new ArgumentException(
                "Component id должен быть каноническим logical token Net.Pilot.",
                nameof(value));
        }

        return value;
    }

    private static string ValidateRelativePath(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.Length > 256
            || value[0] == '/'
            || value[^1] == '/'
            || value.Contains('\\')
            || value.Contains(':')
            || value.Contains("//", StringComparison.Ordinal)
            || value.Any(static character => char.IsControl(character)))
        {
            throw new ArgumentException(
                "Relative package path имеет недопустимый формат.",
                nameof(value));
        }

        var segments = value.Split('/');
        if (segments.Length is 0 or > 16)
        {
            throw new ArgumentException(
                "Relative package path содержит недопустимое число сегментов.",
                nameof(value));
        }

        foreach (var segment in segments)
        {
            if (segment.Length is 0 or > 64
                || segment is "." or ".."
                || segment.EndsWith(".", StringComparison.Ordinal)
                || IsWindowsReservedDeviceName(segment)
                || segment.Any(static character =>
                    character is not (>= 'a' and <= 'z')
                        and not (>= 'A' and <= 'Z')
                        and not (>= '0' and <= '9')
                        and not '-'
                        and not '_'
                        and not '.'))
            {
                throw new ArgumentException(
                    "Relative package path содержит небезопасный или неоднозначный сегмент.",
                    nameof(value));
            }
        }

        return value;
    }

    private static bool IsWindowsReservedDeviceName(string segment)
    {
        var dotIndex = segment.IndexOf('.');
        var baseName = dotIndex >= 0 ? segment[..dotIndex] : segment;

        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (baseName.Length == 4
            && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && baseName[3] is >= '1' and <= '9')
        {
            return true;
        }

        return false;
    }
}

/// <summary>
/// Immutable package-layout declaration для exact executable identity.
/// Layout обязан описывать каждый component id ровно один раз и не может
/// добавлять файлы, отсутствующие в immutable identity.
/// </summary>
public sealed class EnginePackageLayoutDeclaration
{
    internal EnginePackageLayoutDeclaration(
        EngineExecutableIdentityDeclaration executableIdentity,
        IReadOnlyList<EnginePackageComponentLayout> components)
    {
        ArgumentNullException.ThrowIfNull(executableIdentity);
        ArgumentNullException.ThrowIfNull(components);

        if (components.Count != executableIdentity.Components.Count)
        {
            throw new ArgumentException(
                "Package layout должен описывать ровно тот же набор компонентов, что immutable identity.",
                nameof(components));
        }

        var snapshot = components.ToArray();
        if (snapshot.Any(static component => component is null))
        {
            throw new ArgumentException(
                "Package layout не может содержать null component.",
                nameof(components));
        }

        var ids = snapshot
            .Select(static component => component.ComponentId)
            .ToArray();

        if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException(
                "Package layout содержит повторяющийся component id.",
                nameof(components));
        }

        var expectedIds = executableIdentity.Components
            .Select(static component => component.ComponentId)
            .ToHashSet(StringComparer.Ordinal);

        if (ids.Any(id => !expectedIds.Contains(id))
            || expectedIds.Any(expected => !ids.Contains(expected, StringComparer.Ordinal)))
        {
            throw new ArgumentException(
                "Package layout не соответствует component ids immutable identity.",
                nameof(components));
        }

        ExecutableIdentity = executableIdentity;
        Components = Array.AsReadOnly(snapshot);
    }

    public EngineExecutableIdentityDeclaration ExecutableIdentity { get; }

    public IReadOnlyList<EnginePackageComponentLayout> Components { get; }
}

/// <summary>
/// Provider публикует только заранее проверенный package layout.
/// </summary>
public interface IEnginePackageLayoutProvider
{
    EnginePackageLayoutDeclaration PackageLayout { get; }
}

/// <summary>
/// Production catalog намеренно пуст до появления NetPilot-owned exact fork package.
/// Upstream/compile-smoke layout не становится trusted автоматически.
/// </summary>
public static class BuiltInEnginePackageLayoutCatalog
{
    private static readonly IReadOnlyList<EnginePackageLayoutDeclaration> Layouts =
        Array.AsReadOnly(Array.Empty<EnginePackageLayoutDeclaration>());

    public static IReadOnlyList<EnginePackageLayoutDeclaration> All => Layouts;

    public static bool TryGet(
        EngineExecutableIdentityDeclaration executableIdentity,
        out EnginePackageLayoutDeclaration? layout)
    {
        ArgumentNullException.ThrowIfNull(executableIdentity);
        layout = null;

        foreach (var candidate in Layouts)
        {
            if (ReferenceEquals(candidate.ExecutableIdentity, executableIdentity))
            {
                layout = candidate;
                return true;
            }
        }

        return false;
    }

    public static EnginePackageLayoutDeclaration Get(
        EngineExecutableIdentityDeclaration executableIdentity)
    {
        if (TryGet(executableIdentity, out var layout) && layout is not null)
        {
            return layout;
        }

        throw new InvalidOperationException(
            "Для immutable executable identity отсутствует trusted package layout Net.Pilot.");
    }
}
