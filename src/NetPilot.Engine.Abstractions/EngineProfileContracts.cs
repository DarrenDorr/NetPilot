namespace NetPilot.Engine.Abstractions;

/// <summary>
/// Канонический идентификатор пользовательского профиля Net.Pilot.
/// Это идентификатор из локального доверенного каталога, а не строка командной строки.
/// </summary>
public readonly record struct EngineProfileId
{
    public const int MaxLength = 48;

    private readonly string? _value;

    private EngineProfileId(string value)
    {
        _value = value;
    }

    public string Value => _value ?? string.Empty;

    public bool IsEmpty => string.IsNullOrEmpty(_value);

    public static EngineProfileId Parse(string value)
    {
        if (!TryParse(value, out var profileId))
        {
            throw new ArgumentException(
                "Идентификатор профиля должен содержать только строчные ASCII-буквы, цифры и одиночные дефисы; длина — от 1 до 48 символов.",
                nameof(value));
        }

        return profileId;
    }

    public static bool TryParse(string? value, out EngineProfileId profileId)
    {
        profileId = default;

        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            return false;
        }

        if (!IsAsciiLowerOrDigit(value[0]) || !IsAsciiLowerOrDigit(value[^1]))
        {
            return false;
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

            return false;
        }

        profileId = new EngineProfileId(value);
        return true;
    }

    public override string ToString() => Value;

    private static bool IsAsciiLowerOrDigit(char value) =>
        value is >= 'a' and <= 'z' or >= '0' and <= '9';
}

/// <summary>
/// Безопасное описание профиля для GUI и автоматизации.
/// Намеренно не содержит аргументы процесса, пути, BAT/Lua-код или URL.
/// </summary>
public sealed record EngineProfileDescriptor
{
    public const int MaxDisplayNameLength = 96;

    public EngineProfileDescriptor(EngineProfileId id, string displayName)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("Идентификатор профиля не может быть пустым.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > MaxDisplayNameLength
            || displayName.Any(char.IsControl))
        {
            throw new ArgumentException("Недопустимое отображаемое имя профиля.", nameof(displayName));
        }

        Id = id;
        DisplayName = displayName;
    }

    public EngineProfileId Id { get; }

    public string DisplayName { get; }
}

public sealed record EngineProfileRuntimeState
{
    private EngineProfileRuntimeState(bool isActive, EngineProfileId? activeProfileId)
    {
        IsActive = isActive;
        ActiveProfileId = activeProfileId;
    }

    public bool IsActive { get; }

    public EngineProfileId? ActiveProfileId { get; }

    public static EngineProfileRuntimeState Inactive { get; } = new(false, null);

    public static EngineProfileRuntimeState Active(EngineProfileId profileId)
    {
        if (profileId.IsEmpty)
        {
            throw new ArgumentException("Активный профиль не может иметь пустой идентификатор.", nameof(profileId));
        }

        return new EngineProfileRuntimeState(true, profileId);
    }
}

/// <summary>
/// Движок получает только идентификатор заранее известного профиля.
/// Конкретная реализация обязана разрешить его через собственный локальный allowlist
/// и отклонить неизвестный идентификатор fail-closed.
/// </summary>
public interface IEngineProfileRuntime
{
    string EngineId { get; }

    ValueTask<IReadOnlyList<EngineProfileDescriptor>> ListProfilesAsync(
        CancellationToken cancellationToken);

    ValueTask<EngineProfileRuntimeState> GetProfileStateAsync(
        CancellationToken cancellationToken);

    ValueTask ActivateProfileAsync(
        EngineProfileId profileId,
        CancellationToken cancellationToken);
}
