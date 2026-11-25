using System.Runtime.InteropServices;

namespace ParseProcessPrototype;

/// <summary>Windows only (testing). The engine in a job object: a hard memory cap, ended with the app, and no
/// Windows Error Reporting round when it crashes; and, inside the engine, no fault dialogs or reporting.</summary>
public static class WindowsJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint LimitProcessMemory = 0x100, LimitDieOnUnhandledException = 0x400, LimitKillOnJobClose = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimit
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { public ulong a, b, c, d, e, f; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimit
    {
        public BasicLimit Basic;
        public IoCounters Io;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)] private static extern nint CreateJobObjectW(nint attrs, string? name);
    [DllImport("kernel32", SetLastError = true)] private static extern bool SetInformationJobObject(nint job, int cls, ref ExtendedLimit info, int size);
    [DllImport("kernel32", SetLastError = true)] private static extern bool AssignProcessToJobObject(nint job, nint process);
    [DllImport("kernel32")] private static extern uint SetErrorMode(uint mode);
    [DllImport("kernel32", SetLastError = true)] private static extern bool QueryInformationJobObject(nint job, int cls, out ExtendedLimit info, int size, nint ret);

    /// <summary>The most private memory the engine in this job has committed (MB), as the job counts it against
    /// its cap: the number that decides whether the cap is the one that bites.</summary>
    public static long PeakMb(nint job)
    {
        if (!OperatingSystem.IsWindows() || job == 0) return -1;
        return QueryInformationJobObject(job, JobObjectExtendedLimitInformation, out var info, Marshal.SizeOf<ExtendedLimit>(), 0)
            ? (long)(info.PeakProcessMemoryUsed >> 20) : -1;
    }
    [DllImport("wer")] private static extern int WerSetFlags(uint flags);

    /// <summary>Puts the process in a new job with a memory cap (0: none). Kept open by the app; closing it (the app
    /// ending) ends the engine.</summary>
    public static nint Contain(nint process, int memoryMb)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var job = CreateJobObjectW(0, null);
        var info = new ExtendedLimit();
        info.Basic.LimitFlags = LimitKillOnJobClose | LimitDieOnUnhandledException | (memoryMb > 0 ? LimitProcessMemory : 0);
        if (memoryMb > 0) info.ProcessMemoryLimit = (nuint)((long)memoryMb << 20);
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, ref info, Marshal.SizeOf<ExtendedLimit>()))
            throw new InvalidOperationException("SetInformationJobObject failed: " + Marshal.GetLastWin32Error());
        if (!AssignProcessToJobObject(job, process))
            throw new InvalidOperationException("AssignProcessToJobObject failed: " + Marshal.GetLastWin32Error());
        return job;
    }

    /// <summary>In the engine, first thing: no fault dialog, no "searching for a solution", no queued report.</summary>
    public static void QuietCrashes()
    {
        if (!OperatingSystem.IsWindows()) return;
        SetErrorMode(0x0001 | 0x0002 | 0x8000);   // SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX
        try { WerSetFlags(0x0004); } catch (Exception) { }   // WER_FAULT_REPORTING_FLAG_DISABLE_THREAD_SUSPENSION
    }
}
