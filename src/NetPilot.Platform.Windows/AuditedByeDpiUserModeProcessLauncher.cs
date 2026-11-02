using System.Diagnostics;
using NetPilot.Automation;

namespace NetPilot.Platform.Windows;

internal static class AuditedByeDpiUserModeProcessLauncher
{
    public static Process Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        if (startInfo.UseShellExecute
            || !string.Equals(Path.GetFileName(startInfo.FileName), "ciadpi.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new StrategyRuntimeException(
                "ByeDpiLauncherPolicyRejected",
                "Audited ByeDPI launcher rejected the process configuration.");
        }

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new StrategyRuntimeException(
                    "ByeDpiProcessStartFailed",
                    "Windows could not start the reviewed ByeDPI process.");
            }

            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }
}
