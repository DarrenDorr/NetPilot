namespace NetPilot.Engine.Abstractions;

public interface INetworkEngine
{
    string Id { get; }
    string DisplayName { get; }

    ValueTask<EngineStatus> GetStatusAsync(CancellationToken cancellationToken);
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
    ValueTask<EngineHealthResult> CheckHealthAsync(CancellationToken cancellationToken);
}

public enum EngineStatus
{
    Unknown,
    Stopped,
    Starting,
    Running,
    Degraded,
    Failed
}

public sealed record EngineHealthResult(
    EngineStatus Status,
    string? DiagnosticCode = null,
    string? UserMessage = null);
