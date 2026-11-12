using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using NetPilot.Automation;

namespace NetPilot.Platform.Windows;

public sealed class WindowsByeDpiStrategyRuntime : IStrategyRuntime, ILocalSocks5EndpointProvider, IDisposable
{
    private readonly ByeDpiUserModeOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Process? _process;
    private WindowsJobObject? _job;
    private string? _strategyId;
    private int? _proxyPort;
    private bool _disposed;

    public WindowsByeDpiStrategyRuntime(ByeDpiUserModeOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    public int? LocalSocks5Port => ProxyPort;

    public int? ProxyPort
    {
        get
        {
            var process = _process;
            return process is not null && !process.HasExited ? _proxyPort : null;
        }
    }

    public Task<IReadOnlyList<StrategyDescriptor>> ListStrategiesAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ByeDpiUserModeLaunchPolicy.Strategies);
    }

    public async Task<StrategyRuntimeState> GetStateAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            ReconcileExitedProcess();
            return _process is not null && !_process.HasExited && !string.IsNullOrWhiteSpace(_strategyId)
                ? StrategyRuntimeState.Running(_strategyId)
                : StrategyRuntimeState.Stopped;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ActivateAsync(
        string strategyId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);

        _ = ByeDpiUserModeLaunchPolicy.BuildArguments(
            strategyId,
            proxyPort: 1080,
            _options.MaxConnections);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            ReconcileExitedProcess();
            if (_process is not null
                && !_process.HasExited
                && string.Equals(_strategyId, strategyId, StringComparison.Ordinal))
            {
                return;
            }

            await StopCoreAsync(cancellationToken);
            await StartCoreAsync(strategyId, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _gate.Wait();
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCoreAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task StartCoreAsync(string strategyId, CancellationToken cancellationToken)
    {
        var proxyPort = ReserveFreeLoopbackPort();
        var arguments = ByeDpiUserModeLaunchPolicy.BuildArguments(
            strategyId,
            proxyPort,
            _options.MaxConnections);

        using var verifiedExecutable = OpenAndVerifyExecutable();

        WindowsJobObject? job = null;
        Process? process = null;
        var committed = false;
        try
        {
            job = new WindowsJobObject();
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(_options.ExecutablePath),
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(_options.ExecutablePath))!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = AuditedByeDpiUserModeProcessLauncher.Start(startInfo);

            try
            {
                job.Assign(process);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                TryKill(process);
                throw new StrategyRuntimeException(
                    "ByeDpiJobAssignmentFailed",
                    "NetPilot остановил ByeDPI: процесс не удалось поместить в защищённый Job Object.",
                    exception);
            }

            await WaitForSocks5ReadyAsync(
                process,
                proxyPort,
                _options.ListenerStartupTimeout,
                cancellationToken);

            _process = process;
            _job = job;
            _strategyId = strategyId;
            _proxyPort = proxyPort;
            committed = true;
        }
        catch (StrategyRuntimeException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new StrategyRuntimeException(
                "ByeDpiStartFailed",
                "NetPilot безопасно остановил запуск ByeDPI.",
                exception);
        }
        finally
        {
            if (!committed)
            {
                if (process is not null)
                {
                    TryKill(process);
                    process.Dispose();
                }

                job?.Dispose();
            }
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        var job = _job;
        var port = _proxyPort;

        _process = null;
        _job = null;
        _strategyId = null;
        _proxyPort = null;

        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            job?.Dispose();
            process?.Dispose();
        }

        if (port is not null)
        {
            await WaitForNoListenerAsync(port.Value, TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private FileStream OpenAndVerifyExecutable()
    {
        var packageRoot = Path.GetFullPath(_options.PackageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var executablePath = Path.GetFullPath(_options.ExecutablePath);

        EnsureNoReparsePoints(packageRoot, executablePath);

        var stream = new FileStream(
            executablePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        try
        {
            if (stream.Length != _options.ExpectedSize)
            {
                throw new StrategyRuntimeException(
                    "ByeDpiSizeMismatch",
                    "Размер файла ByeDPI не совпадает с проверенным пакетом.");
            }

            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actualSha256, _options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new StrategyRuntimeException(
                    "ByeDpiHashMismatch",
                    "SHA-256 ByeDPI не совпадает с проверенным пакетом. Запуск заблокирован.");
            }

            stream.Position = 0;
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void EnsureNoReparsePoints(string packageRoot, string executablePath)
    {
        if (!Directory.Exists(packageRoot))
        {
            throw new StrategyRuntimeException(
                "ByeDpiPackageMissing",
                "Проверенный пакет ByeDPI ещё не установлен для текущего пользователя.");
        }

        if (!File.Exists(executablePath))
        {
            throw new StrategyRuntimeException(
                "ByeDpiExecutableMissing",
                "Файл ciadpi.exe отсутствует. Запуск заблокирован.");
        }

        var rootPrefix = packageRoot + Path.DirectorySeparatorChar;
        if (!executablePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new StrategyRuntimeException(
                "ByeDpiPathEscape",
                "Путь ByeDPI вышел за границы проверенного пакета.");
        }

        var rootInfo = new DirectoryInfo(packageRoot);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StrategyRuntimeException(
                "ByeDpiReparsePointRejected",
                "Каталоги-перенаправления запрещены для пакета ByeDPI.");
        }

        var executable = new FileInfo(executablePath);
        if ((executable.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new StrategyRuntimeException(
                "ByeDpiReparsePointRejected",
                "Файлы-перенаправления запрещены для ByeDPI.");
        }

        var current = executable.Directory;
        var reachedRoot = false;
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StrategyRuntimeException(
                    "ByeDpiReparsePointRejected",
                    "Каталоги-перенаправления запрещены для ByeDPI.");
            }

            if (string.Equals(
                    current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    packageRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                reachedRoot = true;
                break;
            }

            current = current.Parent;
        }

        if (!reachedRoot)
        {
            throw new StrategyRuntimeException(
                "ByeDpiPathEscape",
                "Путь ByeDPI не принадлежит проверенному пакету.");
        }
    }

    private void ReconcileExitedProcess()
    {
        if (_process is null || !_process.HasExited)
        {
            return;
        }

        _process.Dispose();
        _process = null;
        _job?.Dispose();
        _job = null;
        _strategyId = null;
        _proxyPort = null;
    }

    private static int ReserveFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start(1);
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitForSocks5ReadyAsync(
        Process process,
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new StrategyRuntimeException(
                    "ByeDpiExitedDuringStartup",
                    $"ByeDPI завершился во время запуска с кодом {process.ExitCode}.");
            }

            try
            {
                var listeners = IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Where(endpoint => endpoint.Port == port)
                    .ToArray();
                if (listeners.Any(endpoint => !IPAddress.IsLoopback(endpoint.Address)))
                {
                    throw new StrategyRuntimeException(
                        "ByeDpiListenerBoundaryViolation",
                        "ByeDPI открыл порт вне loopback. Процесс остановлен.");
                }

                using var client = new TcpClient(AddressFamily.InterNetwork);
                using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                await client.ConnectAsync(IPAddress.Loopback, port, attemptCts.Token);
                await using var stream = client.GetStream();
                await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, attemptCts.Token);
                var response = new byte[2];
                await stream.ReadExactlyAsync(response, attemptCts.Token);
                if (response[0] == 0x05 && response[1] == 0x00)
                {
                    return;
                }
            }
            catch (StrategyRuntimeException)
            {
                throw;
            }
            catch (Exception exception) when (exception is SocketException
                                               or IOException
                                               or OperationCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new StrategyRuntimeException(
            "ByeDpiListenerTimeout",
            "ByeDPI не подтвердил безопасный SOCKS5 loopback listener.",
            lastException);
    }

    private static async Task WaitForNoListenerAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IPGlobalProperties.GetIPGlobalProperties()
                    .GetActiveTcpListeners()
                    .Any(endpoint => endpoint.Port == port))
            {
                return;
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new StrategyRuntimeException(
            "ByeDpiListenerResidue",
            "После остановки ByeDPI остался активный listener. Операция заблокирована.");
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
