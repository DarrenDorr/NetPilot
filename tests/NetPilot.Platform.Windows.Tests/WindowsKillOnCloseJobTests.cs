using System.Diagnostics;
using NetPilot.Platform.Windows;

namespace NetPilot.Platform.Windows.Tests;

public sealed class WindowsKillOnCloseJobTests
{
    [Fact]
    public void Disposing_job_terminates_assigned_process()
    {
        var pingPath = Path.Combine(Environment.SystemDirectory, "ping.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = pingPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("30");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows did not start ping.exe for containment test.");

        try
        {
            using (var job = WindowsKillOnCloseJob.Attach(process))
            {
                Assert.False(process.HasExited);
            }

            Assert.True(
                process.WaitForExit(milliseconds: 5_000),
                "Assigned process survived JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 5_000);
            }
        }
    }
}
