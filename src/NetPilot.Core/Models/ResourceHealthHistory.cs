namespace NetPilot.Core.Models;

/// <summary>
/// Минимальный снимок результата health-check для локальной диагностической истории.
/// Намеренно не содержит URI, IP-адрес, exception message, HTTP body или пользовательские данные.
/// </summary>
public sealed record ResourceHealthHistoryEntry(
    string ResourceId,
    ResourceHealthState State,
    DateTimeOffset CheckedAt,
    TimeSpan? Latency,
    string? DiagnosticCode);

/// <summary>
/// Ограниченная история health-check только в памяти процесса.
/// Класс не выполняет файловых или сетевых операций и удаляет самые старые записи
/// при достижении заданной ёмкости.
/// </summary>
public sealed class ResourceHealthHistory
{
    public const int DefaultCapacity = 50;
    public const int MaximumCapacity = 200;

    private readonly object _gate = new();
    private readonly List<ResourceHealthHistoryEntry> _entries = [];
    private readonly int _capacity;

    public ResourceHealthHistory(int capacity = DefaultCapacity)
    {
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                $"Ёмкость диагностической истории должна быть от 1 до {MaximumCapacity}.");
        }

        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public void Add(ResourceHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);

        var entry = CreateEntry(health);
        lock (_gate)
        {
            _entries.Add(entry);
            TrimToCapacity();
        }
    }

    public void AddRange(IEnumerable<ResourceHealth> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var entries = results.Select(CreateEntry).ToArray();
        if (entries.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _entries.AddRange(entries);
            TrimToCapacity();
        }
    }

    public IReadOnlyList<ResourceHealthHistoryEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private void TrimToCapacity()
    {
        var overflow = _entries.Count - _capacity;
        if (overflow > 0)
        {
            _entries.RemoveRange(0, overflow);
        }
    }

    private static ResourceHealthHistoryEntry CreateEntry(ResourceHealth health)
    {
        ValidateResourceId(health.ResourceId);
        ValidateDiagnosticCode(health.DiagnosticCode);

        if (health.State is ResourceHealthState.Unknown or ResourceHealthState.Checking)
        {
            throw new ArgumentException(
                "В диагностическую историю можно добавлять только завершённый health-check.",
                nameof(health));
        }

        if (health.Latency is { } latency && latency < TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Задержка health-check не может быть отрицательной.",
                nameof(health));
        }

        return new ResourceHealthHistoryEntry(
            health.ResourceId,
            health.State,
            health.CheckedAt,
            health.Latency,
            health.DiagnosticCode);
    }

    private static void ValidateResourceId(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId)
            || resourceId.Length > 64
            || resourceId.Any(static character =>
                character is not (>= 'a' and <= 'z')
                    and not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '-'
                    and not '_'
                    and not '.'))
        {
            throw new ArgumentException(
                "ResourceId диагностической записи имеет небезопасный формат.",
                nameof(resourceId));
        }
    }

    private static void ValidateDiagnosticCode(string? diagnosticCode)
    {
        if (diagnosticCode is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(diagnosticCode)
            || diagnosticCode.Length > 64
            || diagnosticCode.Any(static character =>
                character is not (>= 'a' and <= 'z')
                    and not (>= 'A' and <= 'Z')
                    and not (>= '0' and <= '9')
                    and not '-'
                    and not '_'
                    and not '.'))
        {
            throw new ArgumentException(
                "DiagnosticCode диагностической записи имеет небезопасный формат.",
                nameof(diagnosticCode));
        }
    }
}
