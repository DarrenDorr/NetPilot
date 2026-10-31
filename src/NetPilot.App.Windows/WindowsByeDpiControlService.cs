using NetPilot.App.Services;
using NetPilot.Ipc;
using NetPilot.Platform.Windows;

namespace NetPilot.App.Windows;

internal sealed class WindowsByeDpiControlService : IByeDpiControlService
{
    private readonly WindowsPrivilegedServiceClient _client = new();

#if NETPILOT_EXPERIMENTAL_BYEDPI
    public bool IsExperimentalAvailable => true;
#else
    public bool IsExperimentalAvailable => false;
#endif

    public string ProxyEndpoint => IsExperimentalAvailable
        ? "127.0.0.1:39036"
        : "недоступно";

    public async Task<ByeDpiUiStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!IsExperimentalAvailable)
        {
            return Unavailable();
        }

        return Map(await _client.GetByeDpiStatusAsync(cancellationToken));
    }

    public async Task<ByeDpiUiStatus> StartAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Map(await _client.StartByeDpiAsync(cancellationToken));
    }

    public async Task<ByeDpiUiStatus> StopAsync(CancellationToken cancellationToken = default)
    {
        EnsureAvailable();
        return Map(await _client.StopByeDpiAsync(cancellationToken));
    }

    private static ByeDpiUiStatus Map(IpcResponse response)
    {
        if (!response.Success)
        {
            throw new InvalidOperationException(
                $"ByeDPI IPC operation failed: {response.Code}: {response.Message}");
        }

        var state = response.ByeDpiStatus?.State
            ?? throw new InvalidOperationException("ByeDPI IPC response has no status payload.");

        return state switch
        {
            IpcByeDpiProcessState.Running => new ByeDpiUiStatus(
                ByeDpiUiState.Running,
                "ByeDPI работает как локальный SOCKS5 proxy. Системный proxy NetPilot не изменяет."),
            IpcByeDpiProcessState.Stopped => new ByeDpiUiStatus(
                ByeDpiUiState.Stopped,
                "ByeDPI остановлен."),
            _ => throw new InvalidOperationException("ByeDPI IPC returned an unknown process state.")
        };
    }

    private void EnsureAvailable()
    {
        if (!IsExperimentalAvailable)
        {
            throw new InvalidOperationException("ByeDPI experimental runtime is not enabled in this build.");
        }
    }

    private static ByeDpiUiStatus Unavailable() => new(
        ByeDpiUiState.Unavailable,
        "ByeDPI не включён в этой сборке NetPilot.");
}
