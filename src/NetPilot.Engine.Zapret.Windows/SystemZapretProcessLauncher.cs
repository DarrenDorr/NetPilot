using System.Diagnostics;
using NetPilot.Platform.Windows;

namespace NetPilot.Engine.Zapret.Windows;

/// <summary>
/// Единственная audited точка создания native process в NetPilot.
/// Production DI сам по себе не получает этот launcher без отдельного admission.
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
            return new SystemManagedProcess(StartProcess(startInfo));
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
            return new SystemManagedProcess(StartProcess(startInfo));
        }
        catch (InvalidOperationException exception)
        {
            throw new WindowsServiceOwnedEngineProcessException(
                "ProcessCreationFailed",
                "Windows не вернула дескриптор запущенного verified native process.",
                exception);
        }
    }

    // SECURITY INVARIANT: во всём runtime-коде NetPilot остаётся ровно одна
    // audited точка Process.Start. Оба typed launch path приходят сюда только после
    // своих соответствующих проверенных ProcessStartInfo factories.
    private static Process StartProcess(ProcessStartInfo startInfo) =>
        Process.Start(startInfo)
        ?? throw new InvalidOperationException("Windows process creation returned null.");

    private sealed class SystemManagedProcess(Process process) :
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
            process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
