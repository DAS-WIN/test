using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SrbMinerMonitor.Core;

// Runs inside the pseudoconsole, before any WPF initialization.
public static class ConsoleBatchHost
{
    public const string Argument = "--internal-batch-console";
    public static bool TryHandleArguments(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.FirstOrDefault() != Argument) return false;
        if (!OperatingSystem.IsWindows() || args.Length != 1) { exitCode = 2; return true; }
        try
        {
            var batch = Environment.GetEnvironmentVariable("SRBM_MONITOR_BATCH_PATH");
            if (string.IsNullOrEmpty(batch) || !File.Exists(batch)) throw new InvalidOperationException("Missing batch path.");
            var security = new SecurityAttributes { Size = (uint)Marshal.SizeOf<SecurityAttributes>(), Inherit = 1 };
            using var output = CreateFileW("CONOUT$", 0xC0000000, 3, ref security, 3, 0, IntPtr.Zero);
            using var input = CreateFileW("NUL", 0x80000000, 3, ref security, 3, 0, IntPtr.Zero);
            if (output.IsInvalid || input.IsInvalid || !GetConsoleMode(output, out var mode)
                || !SetConsoleMode(output, mode | 4)
                || !SetStdHandle(-11, output) || !SetStdHandle(-12, output) || !SetStdHandle(-10, input))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            SetConsoleOutputCP(65001);
            var cmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
            using var process = Process.Start(new ProcessStartInfo(cmd, "/d /v:off /s /c \"\"%SRBM_MONITOR_BATCH_PATH%\"\"")
                { UseShellExecute = false, CreateNoWindow = false }) ?? throw new InvalidOperationException("Cannot start batch.");
            process.WaitForExit(); exitCode = process.ExitCode;
        }
        catch { exitCode = 2; }
        return true;
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public uint Size; public IntPtr Descriptor; public int Inherit; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, ref SecurityAttributes attributes, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetConsoleMode(SafeFileHandle handle, out uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleMode(SafeFileHandle handle, uint mode);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetStdHandle(int kind, SafeFileHandle handle);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleOutputCP(uint codePage);
}
