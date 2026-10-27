namespace NetPilot.Core.Models;

public enum ResourceHealthState
{
    Unknown,
    Checking,
    Healthy,
    Degraded,
    Unavailable
}

public sealed record ResourceHealth(
    string ResourceId,
    ResourceHealthState State,
    DateTimeOffset CheckedAt,
    TimeSpan? Latency = null,
    string? DiagnosticCode = null);
