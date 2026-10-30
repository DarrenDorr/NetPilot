using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetPilot.Platform.Windows;

public sealed class WindowsProcessContainmentException(
    string code,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string Code { get; } = code;
}

/// <summary>
/// Kernel-enforced lifetime containment for a native engine process.
///
/// The job is configured with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE before the process is
/// assigned. The owning launcher must retain this object for the full process lifetime.
/// If the service process terminates abruptly, Windows closes the service-owned job handle
/// and terminates every process assigned to the job without relying on managed cleanup.
/// </summary>
public sealed class WindowsKillOnCloseJob : IDisposable
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private SafeJobHandle? _handle;

    private WindowsKillOnCloseJob(SafeJobHandle handle)
    {
        _handle = handle;
    }

    public static WindowsKillOnCloseJob Attach(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Windows Job Object containment доступен только в Windows.");
        }

        SafeJobHandle? job = null;
        try
        {
            job = CreateJobObjectW(IntPtr.Zero, null);
            if (job.IsInvalid)
            {
                throw CreateWin32Failure(
                    "JobCreateFailed",
                    "Windows не позволила создать Job Object для native engine.");
            }

            var information = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    LimitFlags = JobObjectLimitKillOnJobClose
                }
            };

            if (!SetInformationJobObject(
                    job,
                    JobObjectExtendedLimitInformation,
                    ref information,
                    checked((uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>())))
            {
                throw CreateWin32Failure(
                    "JobPolicyFailed",
                    "Windows не позволила включить kill-on-close policy для native engine Job Object.");
            }

            // Process.Handle for a process returned by Process.Start carries the access needed
            // for assignment. No PID reopen and no user-selected process handle is accepted here.
            if (!AssignProcessToJobObject(job, process.Handle))
            {
                throw CreateWin32Failure(
                    "JobAssignmentFailed",
                    "Windows не позволила привязать native engine к service-owned Job Object.");
            }

            var result = new WindowsKillOnCloseJob(job);
            job = null;
            return result;
        }
        finally
        {
            job?.Dispose();
        }
    }

    public void Dispose()
    {
        var handle = Interlocked.Exchange(ref _handle, null);
        handle?.Dispose();
    }

    private static WindowsProcessContainmentException CreateWin32Failure(
        string code,
        string message)
    {
        var error = Marshal.GetLastWin32Error();
        return new WindowsProcessContainmentException(
            code,
            message,
            new Win32Exception(error));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeJobHandle CreateJobObjectW(
        IntPtr jobAttributes,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeJobHandle job,
        uint jobObjectInformationClass,
        ref JobObjectExtendedLimitInformation jobObjectInformation,
        uint jobObjectInformationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(
        SafeJobHandle job,
        IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    private sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeJobHandle() : base(ownsHandle: true)
        {
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
