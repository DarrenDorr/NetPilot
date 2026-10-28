using System.Diagnostics;

namespace NetPilot.Engine.Zapret.Windows;

public sealed class SystemZapretProcessLauncher : IZapretProcessLauncher
{
    public IZapretManagedProcess Launch(ZapretLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var startInfo = ZapretProcessStartInfoFactory.Create(plan);
        var process = Process.Start(startInfo)
            ?? throw new ZapretProcessRunnerException(
                "ProcessCreationFailed",
                "Windows не вернула дескриптор запущенного процесса Zapret.");

        return new SystemZapretManagedProcess(process);
    }

    private sealed class SystemZapretManagedProcess(Process process) : IZapretManagedProcess
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
