namespace NetPilot.App.Services;

public enum ByeDpiUiState
{
    Unavailable,
    Stopped,
    Running
}

public sealed record ByeDpiUiStatus(
    ByeDpiUiState State,
    string Details);

/// <summary>
/// Narrow application-facing surface for the experimental ByeDPI alpha path.
/// It exposes no native path, PID, raw argv, URL, SID or arbitrary configuration.
/// </summary>
public interface IByeDpiControlService
{
    bool IsExperimentalAvailable { get; }

    string ProxyEndpoint { get; }

    Task<ByeDpiUiStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ByeDpiUiStatus> StartAsync(CancellationToken cancellationToken = default);

    Task<ByeDpiUiStatus> StopAsync(CancellationToken cancellationToken = default);
}

public sealed class UnavailableByeDpiControlService : IByeDpiControlService
{
    public static UnavailableByeDpiControlService Instance { get; } = new();

    private UnavailableByeDpiControlService()
    {
    }

    public bool IsExperimentalAvailable => false;

    public string ProxyEndpoint => "недоступно";

    public Task<ByeDpiUiStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ByeDpiUiStatus(
            ByeDpiUiState.Unavailable,
            "ByeDPI не включён в этой сборке NetPilot."));

    public Task<ByeDpiUiStatus> StartAsync(CancellationToken cancellationToken = default) =>
        GetStatusAsync(cancellationToken);

    public Task<ByeDpiUiStatus> StopAsync(CancellationToken cancellationToken = default) =>
        GetStatusAsync(cancellationToken);
}
