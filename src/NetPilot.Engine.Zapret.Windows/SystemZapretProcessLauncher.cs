using System.Diagnostics;
using NetPilot.Platform.Windows;

namespace NetPilot.Engine.Zapret.Windows;

/// <summary>
/// Единственная audited точка создания native process в NetPilot.
/// Production DI сам по себе не получает этот launcher без отдельного admission.
/// Every created native process must also enter a kill-on-close Windows Job Object;
/// otherwise launch fails closed and the just-created process is terminated.
/// </summary>
public sealed class SystemZapretProcessLauncher :
    IZapretProcessLauncher,
    IWindowsVerifiedEngineProcessLauncher
{
    public IZapretManagedProcess Launch(ZapretLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var startInfo = ZapretProcessStartInfoFactory.Create(plan);
        try
        {
            return StartContainedProcess(startInfo);
        }
        catch (WindowsProcessContainmentException exception)
        {
            throw new ZapretProcessRunnerException(
                "ProcessContainmentFailed",
                "Windows не позволила безопасно привязать процесс Zapret к service-owned Job Object.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ZapretProcessRunnerException(
                "ProcessCreationFailed",
                "Windows не вернула дескриптор запущенного процесса Zapret.",
                exception);
        }
    }

    public IWindowsManagedEngineProcess Launch(
        WindowsCompiledEngineArgumentPlan compiledPlan)
    {
        ArgumentNullException.ThrowIfNull(compiledPlan);
        compiledPlan.EnsureReady();

        var startInfo = WindowsVerifiedEngineProcessStartInfoFactory.Create(compiledPlan);
        try
        {
            return StartContainedProcess(startInfo);
        }
        catch (WindowsProcessContainmentException exception)
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ProcessContainmentFailed",
                "Windows не позволила безопасно привязать verified native process к service-owned Job Object.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ProcessCreationFailed",
                "Windows не вернула дескриптор запущенного verified native process.",
                exception);
        }
    }

    private static SystemManagedProcess StartContainedProcess(ProcessStartInfo startInfo)
    {
        var process = StartProcess(startInfo);
        try
        {
            var containment = WindowsKillOnCloseJob.Attach(process);
            return new SystemManagedProcess(process, containment);
        }
        catch
        {
            // A process that cannot be placed in the required OS containment boundary
            // must never be allowed to continue as an uncontained native engine.
            TerminateUncontainedProcessBestEffort(process);
            process.Dispose();
            throw;
        }
    }

    // SECURITY INVARIANT: во всём runtime-коде NetPilot остаётся ровно одна
    // audited точка Process.Start. Оба typed launch path приходят сюда только после
    // своих соответствующих проверенных ProcessStartInfo factories.
    private static Process StartProcess(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)
        ?? throw new InvalidOperationException("Windows process creation returned null.");

    private static void TerminateUncontainedProcessBestEffort(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 5_000);
            }
        }
        catch
        {
            // The original containment failure is authoritative. This path is only a
            // best-effort secondary cleanup after launch has already been rejected.
        }
    }

    private sealed class SystemManagedProcess(
        Process process,
        WindowsKillOnCloseJob containment) :
        IZapretManagedProcess,
        IWindowsManagedEngineProcess
    {
        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public void KillEntireTree() => process.Kill(entireProcessTree: true);

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);

        public ValueTask DisposeAsync()
        {
            // Closing the job is intentionally part of disposal. If the process is still
            // alive, Windows terminates it because JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE is set.
            containment.Dispose();
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
