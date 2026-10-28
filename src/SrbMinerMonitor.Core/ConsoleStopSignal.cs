using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
namespace SrbMinerMonitor.Core;

// Only a short-lived helper attaches to the miner console. The WPF process never joins it.
public static class ConsoleStopSignal
{
    private const string HelperArgument = "--internal-miner-stop-signal";
    private static readonly Handler IgnoreSignals = _ => true;

    public static bool TryHandleArguments(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.FirstOrDefault() != HelperArgument) return false;
        if (!OperatingSystem.IsWindows() || args.Length is < 2 or > 2050) { exitCode = 2; return true; }
        var ids = new List<uint>();
        foreach (var argument in args.Skip(1))
        {
            if (!uint.TryParse(argument, out var id) || id == 0 || id == Environment.ProcessId) { exitCode = 2; return true; }
            ids.Add(id);
        }
        try { exitCode = SendFromHelper(ids.Distinct().ToArray()) ? 0 : 1; }
        catch { exitCode = 2; }
        return true;
    }
    public static async Task<bool> SendAsync(IEnumerable<uint> ownedGroups)
    {
        if (!OperatingSystem.IsWindows()) return false;
        var ids = ownedGroups.Where(id => id != 0 && id != Environment.ProcessId).Distinct().ToArray();
        if (ids.Length == 0) return false;
        var executable = Environment.ProcessPath;
        if (executable is null) return false;
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        // Also support framework-dependent launches via `dotnet application.dll`.
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrEmpty(assembly)) return false;
            info.ArgumentList.Add(assembly);
        }
        info.ArgumentList.Add(HelperArgument);
        foreach (var id in ids) info.ArgumentList.Add(id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        using var helper = new Process { StartInfo = info };
        try
        {
            helper.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var stdout = helper.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = helper.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, helper.WaitForExitAsync(timeout.Token));
            return helper.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or OperationCanceledException or InvalidOperationException)
        {
            try { if (!helper.HasExited) helper.Kill(true); }
            catch (Exception cleanup) when (cleanup is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            return false;
        }
    }
    private static bool SendFromHelper(uint[] groups)
    {
        // Detach only the helper from any inherited host console.
        if (GetConsoleWindow() != IntPtr.Zero) FreeConsole();
        foreach (var pid in groups)
        {
            if (!AttachConsole(pid)) continue;
            try
            {
                // AttachConsole resets handlers. Install this afterwards, only in the helper.
                if (!SetConsoleCtrlHandler(IgnoreSignals, true)) return false;
                var sent = false;
                foreach (var group in groups) sent |= GenerateConsoleCtrlEvent(1, group); // Targeted CTRL_BREAK; never broadcast group 0.
                Thread.Sleep(100); // Allow delivery before detaching the isolated helper.
                return sent;
            }
            finally { FreeConsole(); }
        }
        return false;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] [return: MarshalAs(UnmanagedType.Bool)] private delegate bool Handler(uint signal);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AttachConsole(uint pid);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetConsoleCtrlHandler(Handler handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GenerateConsoleCtrlEvent(uint signal, uint group);
}
