using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SrbMinerMonitor.Core;

public sealed partial class MinerProcess : IMinerProcess, IDisposable
{
    private Process? _process;
    public bool IsRunning => _process is { HasExited: false };
    public event Action<string>? Log;

    public async Task StartAsync(MinerSettings settings, CancellationToken ct)
    {
        if (settings.Validate() is string error) throw new ArgumentException(error);
        if (IsRunning) throw new InvalidOperationException("已有受管理的礦工程序。");
        using (var client = new TcpClient())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            var inUse = false;
            try { await client.ConnectAsync("127.0.0.1", settings.ApiPort, timeout.Token); inUse = true; }
            catch (SocketException) { }
            if (inUse) throw new InvalidOperationException("API 連接埠已有程序使用，已取消啟動以避免重複礦工。");
        }
        _process?.Dispose();
        var info = new ProcessStartInfo(settings.Executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.Executable))!
        };
        foreach (var argument in settings.Arguments()) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) Log?.Invoke(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } line) Log?.Invoke(line); };
        _process = process;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Catch immediate startup failures before declaring a successful lifecycle transition.
        await Task.Delay(600, ct);
        if (process.HasExited) throw new InvalidOperationException($"礦工啟動失敗（退出碼 {process.ExitCode}），請查看事件紀錄。");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        var process = _process;
        if (process is null || process.HasExited) return;
        if (OperatingSystem.IsWindows()) TrySendCtrlC(process.Id);
        else process.CloseMainWindow();
        using var graceful = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await process.WaitForExitAsync(graceful.Token); }
        catch (OperationCanceledException)
        {
            Log?.Invoke("正常停止超過 10 秒，正在終止 GUI 管理的礦工程序。");
            if (!process.HasExited) process.Kill(true);
            using var killed = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(killed.Token);
        }
        ct.ThrowIfCancellationRequested();
    }

    // WPF has no console. Attach only to this GUI's own child's console, suppress our handler,
    // send CTRL_C, and immediately detach. Never signal an external miner's console.
    private static void TrySendCtrlC(int pid)
    {
        if (!AttachConsole((uint)pid)) return;
        try { SetConsoleCtrlHandler(IntPtr.Zero, true); GenerateConsoleCtrlEvent(0, 0); }
        finally { FreeConsole(); SetConsoleCtrlHandler(IntPtr.Zero, false); }
    }
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AttachConsole(uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool FreeConsole();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GenerateConsoleCtrlEvent(uint signal, uint group);
    public void Dispose() => _process?.Dispose();
}
