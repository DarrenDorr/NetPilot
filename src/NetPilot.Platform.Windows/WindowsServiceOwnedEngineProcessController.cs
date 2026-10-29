using System.Diagnostics;
using System.Globalization;
using NetPilot.Engine.Abstractions;

namespace NetPilot.Platform.Windows;

public sealed class WindowsServiceOwnedEngineProcessException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

public interface IWindowsManagedEngineProcess : IAsyncDisposable
{
    int Id { get; }

    bool HasExited { get; }

    void KillEntireTree();

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IWindowsVerifiedEngineProcessLauncher
{
    IWindowsManagedEngineProcess Launch(WindowsCompiledEngineArgumentPlan compiledPlan);
}

/// <summary>
/// Production default while native runtime admission is BLOCK. This type contains no
/// Process/System.Diagnostics state and cannot create a process under any input.
/// </summary>
public sealed class NoAdmittedWindowsVerifiedEngineProcessLauncher
    : IWindowsVerifiedEngineProcessLauncher
{
    public static NoAdmittedWindowsVerifiedEngineProcessLauncher Instance { get; } = new();

    private NoAdmittedWindowsVerifiedEngineProcessLauncher()
    {
    }

    public IWindowsManagedEngineProcess Launch(WindowsCompiledEngineArgumentPlan compiledPlan) =>
        throw new WindowsServiceOwnedEngineProcessException(
            "NoAdmittedEngine",
            "Запуск native engine запрещён: production runtime admission остаётся BLOCK.");
}

/// <summary>
/// Реальный Windows launcher для отдельного lab/admission composition. Он принимает
/// только уже compiled typed plan из verified handoff и не имеет overload с path,
/// command line или raw arguments.
/// </summary>
public sealed class SystemWindowsVerifiedEngineProcessLauncher
    : IWindowsVerifiedEngineProcessLauncher
{
    public IWindowsManagedEngineProcess Launch(WindowsCompiledEngineArgumentPlan compiledPlan)
    {
        ArgumentNullException.ThrowIfNull(compiledPlan);
        compiledPlan.EnsureReady();

        var startInfo = WindowsVerifiedEngineProcessStartInfoFactory.Create(compiledPlan);
        var process = Process.Start(startInfo)
            ?? throw new WindowsServiceOwnedEngineProcessException(
                "ProcessCreationFailed",
                "Windows не вернула дескриптор запущенного verified native process.");

        return new ManagedProcess(process);
    }

    private sealed class ManagedProcess(Process process) : IWindowsManagedEngineProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public void KillEntireTree() => process.Kill(entireProcessTree: true);

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

public enum WindowsServiceOwnedEngineProcessState
{
    Stopped,
    Running
}

public sealed record WindowsServiceOwnedEngineProcessSnapshot(
    WindowsServiceOwnedEngineProcessState State,
    int? ProcessId);

/// <summary>
/// Bounded single-process lifecycle for the reviewed ByeDPI loopback shape.
/// No IPC/user data is accepted here. The controller rejects every plan except the
/// exact typed R32-compatible shape: -i 127.0.0.1 -p PORT -N -U -c 8 -x 0.
/// </summary>
public sealed class WindowsByeDpiServiceOwnedProcessController(
    IWindowsVerifiedEngineProcessLauncher launcher) : IAsyncDisposable
{
    private static readonly TimeSpan ForcedStopTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private IWindowsManagedEngineProcess? process;
    private bool disposed;

    public async Task<WindowsServiceOwnedEngineProcessSnapshot> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await ClearExitedProcessAsync();
            return Snapshot();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<WindowsServiceOwnedEngineProcessSnapshot> StartAsync(
        WindowsCompiledEngineArgumentPlan compiledPlan,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(compiledPlan);
        compiledPlan.EnsureReady();
        ValidateExactByeDpiLoopbackPlan(compiledPlan);

        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await ClearExitedProcessAsync();
            if (process is not null)
            {
                throw new WindowsServiceOwnedEngineProcessException(
                    "EngineAlreadyRunning",
                    "Service-owned ByeDPI process уже запущен.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var launched = launcher.Launch(compiledPlan);
            if (launched.HasExited)
            {
                await launched.DisposeAsync();
                throw new WindowsServiceOwnedEngineProcessException(
                    "ProcessExitedEarly",
                    "ByeDPI завершился до перехода service-owned lifecycle в Running.");
            }

            process = launched;
            return Snapshot();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async Task<WindowsServiceOwnedEngineProcessSnapshot> StopAsync(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            await ClearExitedProcessAsync();
            if (process is null)
            {
                return Snapshot();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var current = process;
            current.KillEntireTree();

            using var forcedStop = new CancellationTokenSource(ForcedStopTimeout);
            try
            {
                await current.WaitForExitAsync(forcedStop.Token);
            }
            catch (OperationCanceledException) when (forcedStop.IsCancellationRequested)
            {
                throw new WindowsServiceOwnedEngineProcessException(
                    "ForcedStopTimeout",
                    "Service-owned ByeDPI process не завершился в допустимый cleanup timeout.");
            }

            await current.DisposeAsync();
            process = null;
            return Snapshot();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync(CancellationToken.None);
        try
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    process.KillEntireTree();
                    using var forcedStop = new CancellationTokenSource(ForcedStopTimeout);
                    try
                    {
                        await process.WaitForExitAsync(forcedStop.Token);
                    }
                    catch (OperationCanceledException) when (forcedStop.IsCancellationRequested)
                    {
                        // Dispose remains fail-safe: release the managed handle even if the
                        // OS process failed to acknowledge the bounded forced-stop wait.
                    }
                }

                await process.DisposeAsync();
                process = null;
            }

            disposed = true;
        }
        finally
        {
            lifecycleGate.Release();
            lifecycleGate.Dispose();
        }
    }

    private async Task ClearExitedProcessAsync()
    {
        if (process is null || !process.HasExited)
        {
            return;
        }

        await process.DisposeAsync();
        process = null;
    }

    private WindowsServiceOwnedEngineProcessSnapshot Snapshot() =>
        process is null
            ? new WindowsServiceOwnedEngineProcessSnapshot(
                WindowsServiceOwnedEngineProcessState.Stopped,
                null)
            : new WindowsServiceOwnedEngineProcessSnapshot(
                WindowsServiceOwnedEngineProcessState.Running,
                process.Id);

    private static void ValidateExactByeDpiLoopbackPlan(
        WindowsCompiledEngineArgumentPlan compiledPlan)
    {
        var binding = compiledPlan.Declaration.LaunchIdentityBinding;
        if (binding.Backend != BuiltInEngineBackendKind.ByeDpi
            || binding.Platform != EngineHostPlatform.Windows
            || binding.Architecture != EngineBinaryArchitecture.X64)
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ByeDpiBindingRequired",
                "Service-owned controller принимает только Windows x64 ByeDPI binding.");
        }

        var templates = compiledPlan.Declaration.Arguments;
        if (templates.Count != 6
            || !Matches(templates[0], EngineArgumentTokenKind.ShortLoopbackIpv4, "-i", 0)
            || !MatchesPort(templates[1])
            || !Matches(templates[2], EngineArgumentTokenKind.ShortFlag, "-N", 0)
            || !Matches(templates[3], EngineArgumentTokenKind.ShortFlag, "-U", 0)
            || !Matches(templates[4], EngineArgumentTokenKind.ShortFixedPositiveUInt, "-c", 8)
            || !Matches(templates[5], EngineArgumentTokenKind.ShortFixedNonNegativeUInt, "-x", 0))
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ByeDpiArgumentPlanRejected",
                "Typed ByeDPI plan не соответствует exact R32 loopback lifecycle profile.");
        }

        var port = templates[1].FixedPositiveUInt;
        var expected = new[]
        {
            "-i",
            "127.0.0.1",
            "-p",
            port.ToString(CultureInfo.InvariantCulture),
            "-N",
            "-U",
            "-c",
            "8",
            "-x",
            "0"
        };

        if (!compiledPlan.Arguments.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ByeDpiCompiledArgumentsRejected",
                "Compiled ByeDPI argv не соответствует exact typed declaration.");
        }
    }

    private static bool Matches(
        EngineArgumentTokenTemplate template,
        EngineArgumentTokenKind kind,
        string optionName,
        ulong value) =>
        template.Kind == kind
        && string.Equals(template.OptionName, optionName, StringComparison.Ordinal)
        && template.FixedPositiveUInt == value
        && template.ComponentId is null;

    private static bool MatchesPort(EngineArgumentTokenTemplate template) =>
        template.Kind == EngineArgumentTokenKind.ShortFixedPositiveUInt
        && string.Equals(template.OptionName, "-p", StringComparison.Ordinal)
        && template.FixedPositiveUInt is >= 1024 and <= 65535
        && template.ComponentId is null;

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(disposed, this);
}
