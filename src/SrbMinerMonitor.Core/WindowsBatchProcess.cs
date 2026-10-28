using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SrbMinerMonitor.Core;

// A Job owns the complete BAT process tree, including children started with START after cmd exits.
// Start suspended, assign the Job, then resume: no child can escape during a Process.Start/Assign race.
public sealed class WindowsBatchProcess : IMinerProcess, IDisposable
{
    private SafeFileHandle? _job;
    public bool IsRunning => _job is { IsInvalid: false, IsClosed: false } && ActiveProcesses() > 0;
    public event Action<string>? Log;

    public async Task StartAsync(MinerSettings settings, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("BAT 啟動需要 Windows。");
        if (settings.Validate() is string error) throw new ArgumentException(error);
        if (IsRunning) throw new InvalidOperationException("BAT 的礦工程序仍在運行。");
        _job?.Dispose();
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits(); limits.Basic.LimitFlags = 0x2000; // KILL_ON_JOB_CLOSE
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimits>());
        try
        {
            Marshal.StructureToPtr(limits, memory, false);
            if (!SetInformationJobObject(_job, 9, memory, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally { Marshal.FreeHGlobal(memory); }

        var batch = Path.GetFullPath(settings.BatchFile);
        var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string?)entry.Value ?? "";
        environment["SRBM_MONITOR_BATCH_PATH"] = batch;
        var envBlock = Marshal.StringToHGlobalUni(string.Join('\0', environment.Select(p => p.Key + "=" + p.Value)) + "\0\0");
        var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>(), Flags = 1, ShowWindow = 1 };
        var command = new StringBuilder($"\"{cmd}\" /d /v:off /s /c \"\"%SRBM_MONITOR_BATCH_PATH%\"\"");
        ProcessInformation child = default;
        try
        {
            if (!CreateProcessW(cmd, command, IntPtr.Zero, IntPtr.Zero, false, 0x4 | 0x10 | 0x200 | 0x400,
                envBlock, Path.GetDirectoryName(batch)!, ref startup, out child)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!AssignProcessToJobObject(_job, child.Process))
            {
                var code = Marshal.GetLastWin32Error();
                TerminateProcess(child.Process, 1);
                throw new Win32Exception(code, "無法追蹤 BAT 子程序，已取消啟動。");
            }
            if (ResumeThread(child.Thread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { _job.Dispose(); _job = null; throw; }
        finally
        {
            Marshal.FreeHGlobal(envBlock);
            if (child.Thread != IntPtr.Zero) CloseHandle(child.Thread);
            if (child.Process != IntPtr.Zero) CloseHandle(child.Process);
        }
        Log?.Invoke("已啟動指定 BAT；正在等待本機 API。BAT 與其子程序由 GUI 共同管理。");
        await Task.Delay(800, ct);
        if (!IsRunning) throw new InvalidOperationException("BAT 與其子程序已退出。請查看 BAT 主控台；若要監控既有礦工，請確認已啟用 API。");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!IsRunning) return;
        foreach (var pid in ProcessIds())
        {
            if (!AttachConsole(pid)) continue;
            try { SetConsoleCtrlHandler(IntPtr.Zero, true); GenerateConsoleCtrlEvent(0, 0); }
            finally { FreeConsole(); SetConsoleCtrlHandler(IntPtr.Zero, false); }
        }
        var watch = Stopwatch.StartNew();
        while (IsRunning && watch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(100, CancellationToken.None);
        if (IsRunning)
        {
            Log?.Invoke("BAT 程序樹正常停止超過 10 秒，正在終止 GUI 管理的子程序。");
            if (!TerminateJobObject(_job!, 1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            watch.Restart();
            while (IsRunning && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100, CancellationToken.None);
        }
        if (IsRunning) throw new InvalidOperationException("BAT 仍有子程序未退出，禁止啟動第二個礦工。");
        ct.ThrowIfCancellationRequested();
    }
    private uint ActiveProcesses()
    {
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<Accounting>());
        try
        {
            if (!QueryInformationJobObject(_job!, 1, memory, (uint)Marshal.SizeOf<Accounting>(), out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return Marshal.PtrToStructure<Accounting>(memory).ActiveProcesses;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private uint[] ProcessIds()
    {
        const int size = 8 + 1024 * 8;
        var memory = Marshal.AllocHGlobal(size);
        try
        {
            if (!QueryInformationJobObject(_job!, 3, memory, size, out _)) return [];
            var count = Math.Min(Marshal.ReadInt32(memory, 4), 1024);
            return Enumerable.Range(0, count).Select(i => (uint)Marshal.ReadIntPtr(memory, 8 + i * IntPtr.Size).ToInt64()).ToArray();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public void Dispose() { _job?.Dispose(); _job = null; }

    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long PerProcess, PerJob; public uint LimitFlags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)] private struct Accounting { public long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime; public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo { public uint Size; public IntPtr Reserved, Desktop, Title; public uint X, Y, Width, Height, XChars, YChars, FillAttribute, Flags; public ushort ShowWindow, ReservedSize; public IntPtr ReservedBytes, StdInput, StdOutput, StdError; }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation { public IntPtr Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int kind, IntPtr information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr information, uint size, out uint returned);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateJobObject(SafeFileHandle job, uint code);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
