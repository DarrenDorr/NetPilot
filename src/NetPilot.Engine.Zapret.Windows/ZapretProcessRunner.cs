namespace NetPilot.Engine.Zapret.Windows;

public enum ZapretProcessState
{
    Stopped,
    Running
}

public sealed record ZapretProcessSnapshot(
    ZapretProcessState State,
    int? ProcessId = null,
    string? StrategyId = null);

public sealed class ZapretProcessRunnerException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public interface IZapretManagedProcess : IAsyncDisposable
{
    int Id { get; }
    bool HasExited { get; }
    void KillEntireTree();
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IZapretProcessLauncher
{
    IZapretManagedProcess Launch(ZapretLaunchPlan plan);
}

public sealed class ZapretProcessRunner(IZapretProcessLauncher launcher) : IAsyncDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IZapretManagedProcess? _process;
    private string? _strategyId;
    private bool _disposed;

    public async Task<ZapretProcessSnapshot> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await ClearExitedProcessAsync();
            return CreateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ZapretProcessSnapshot> StartAsync(
        ZapretLaunchPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await ClearExitedProcessAsync();

            if (_process is not null)
            {
                if (string.Equals(_strategyId, plan.StrategyId, StringComparison.Ordinal))
                {
                    return CreateSnapshot();
                }

                throw new ZapretProcessRunnerException(
                    "AlreadyRunning",
                    "Zapret уже запущен с другой стратегией. Для смены стратегии требуется контролируемая остановка.");
            }

            IZapretManagedProcess process;
            try
            {
                process = launcher.Launch(plan);
            }
            catch (Exception exception) when (exception is not ZapretProcessRunnerException)
            {
                throw new ZapretProcessRunnerException(
                    "StartFailed",
                    "Не удалось запустить процесс Zapret.",
                    exception);
            }

            if (process.HasExited)
            {
                await process.DisposeAsync();
                throw new ZapretProcessRunnerException(
                    "ExitedImmediately",
                    "Процесс Zapret завершился сразу после запуска.");
            }

            _process = process;
            _strategyId = plan.StrategyId;
            return CreateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ZapretProcessSnapshot> StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await ClearExitedProcessAsync();

            if (_process is null)
            {
                return CreateSnapshot();
            }

            var process = _process;
            _process = null;
            _strategyId = null;

            try
            {
                if (!process.HasExited)
                {
                    process.KillEntireTree();

                    using var stopTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    stopTimeout.CancelAfter(StopTimeout);
                    await process.WaitForExitAsync(stopTimeout.Token);
                }
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ZapretProcessRunnerException(
                    "StopTimeout",
                    "Процесс Zapret не завершился за допустимое время.",
                    exception);
            }
            finally
            {
                await process.DisposeAsync();
            }

            return CreateSnapshot();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_process is not null)
            {
                var process = _process;
                _process = null;
                _strategyId = null;

                try
                {
                    if (!process.HasExited)
                    {
                        process.KillEntireTree();
                        using var timeout = new CancellationTokenSource(StopTimeout);
                        await process.WaitForExitAsync(timeout.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    // При Dispose процесс уже получил Kill; не блокируем завершение службы бесконечно.
                }
                finally
                {
                    await process.DisposeAsync();
                }
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task ClearExitedProcessAsync()
    {
        if (_process is null || !_process.HasExited)
        {
            return;
        }

        var process = _process;
        _process = null;
        _strategyId = null;
        await process.DisposeAsync();
    }

    private ZapretProcessSnapshot CreateSnapshot() =>
        _process is null
            ? new ZapretProcessSnapshot(ZapretProcessState.Stopped)
            : new ZapretProcessSnapshot(ZapretProcessState.Running, _process.Id, _strategyId);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
