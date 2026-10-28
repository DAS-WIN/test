using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace SrbMinerMonitor.Core;

public sealed partial class MinerProcess : IMinerProcess, IDisposable
{
    private Process? _process;
    private WindowsBatchProcess? _batch;
    private int? _activeApiPort;
    public Guid OutputSession { get; private set; }
    public bool IsRunning => _batch?.IsRunning ?? _process is { HasExited: false };
    public event Action<string>? Log;

    public async Task StartAsync(MinerSettings settings, CancellationToken ct)
    {
        if (settings.Validate() is string error) throw new ArgumentException(error);
        if (IsRunning) throw new InvalidOperationException("已有受管理的礦工程序。");
        LocalApiPort.EnsureAvailable(settings.ApiPort);
        _activeApiPort = settings.ApiPort;
        OutputSession = Guid.NewGuid();
        var outputSession = OutputSession;
        _process?.Dispose();
        _process = null;
        _batch?.Dispose(); _batch = null;
        if (settings.UseBatchFile)
        {
            _batch = new WindowsBatchProcess();
            _batch.Log += message => { if (OutputSession == outputSession) Log?.Invoke(message); };
            await _batch.StartAsync(settings, ct);
            return;
        }
        var info = new ProcessStartInfo(settings.Executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(settings.Executable))!
        };
        foreach (var argument in settings.Arguments()) info.ArgumentList.Add(argument);
        var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => { if (OutputSession == outputSession && e.Data is { Length: > 0 } line) Log?.Invoke(line); };
        process.ErrorDataReceived += (_, e) => { if (OutputSession == outputSession && e.Data is { Length: > 0 } line) Log?.Invoke(line); };
        try { process.Start(); }
        catch { process.Dispose(); throw; }
        _process = process;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Catch immediate startup failures before declaring a successful lifecycle transition.
        await Task.Delay(600, ct);
        if (process.HasExited) throw new InvalidOperationException($"礦工啟動失敗（退出碼 {process.ExitCode}），請查看事件紀錄。");
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (_batch is not null)
        {
            await _batch.StopAsync(ct);
            if (_activeApiPort is int batchPort) await LocalApiPort.WaitUntilReleasedAsync(batchPort, TimeSpan.FromSeconds(3));
            return;
        }
        var process = _process;
        if (process is null || process.HasExited)
        {
            if (_activeApiPort is int previousPort) await LocalApiPort.WaitUntilReleasedAsync(previousPort, TimeSpan.FromSeconds(3));
            return;
        }
        if (OperatingSystem.IsWindows()) TrySendStopSignal(process.Id);
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
        if (_activeApiPort is int port) await LocalApiPort.WaitUntilReleasedAsync(port, TimeSpan.FromSeconds(3));
    }

    // Target the owned child's process group with CTRL_BREAK so the GUI never receives its own stop signal.
    private static void TrySendStopSignal(int pid)
    {
        if (!AttachConsole((uint)pid)) return;
        try { GenerateConsoleCtrlEvent(1, (uint)pid); }
        finally { FreeConsole(); }
    }
    [LibraryImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool AttachConsole(uint pid);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool FreeConsole();
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool SetConsoleCtrlHandler(IntPtr handler, [MarshalAs(UnmanagedType.Bool)] bool add);
    [LibraryImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GenerateConsoleCtrlEvent(uint signal, uint group);
    public void Dispose() { _batch?.Dispose(); _process?.Dispose(); }
}
