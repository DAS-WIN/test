using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace SrbMinerMonitor.App.Services;

public static class StartupDiagnostics
{
    private static readonly object Gate = new();
    private static int _fatalReported;
    public static string? LogPath { get; private set; }
    public static bool SuppressDialogs { get; set; }

    public static void Initialize()
    {
        var locations = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SRBMiner Monitor", "Logs"),
            Path.Combine(Path.GetTempPath(), "SRBMiner Monitor", "Logs")
        };
        foreach (var location in locations)
        {
            try
            {
                Directory.CreateDirectory(location);
                var path = Path.Combine(location, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
                File.WriteAllText(path, $"SRBMiner Monitor {Assembly.GetExecutingAssembly().GetName().Version}{Environment.NewLine}" +
                    $"OS: {RuntimeInformation.OSDescription}{Environment.NewLine}" +
                    $"Runtime: {RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture}){Environment.NewLine}");
                LogPath = path;
                Write("Application entry point reached.");
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                if (LogPath is { } path) File.AppendAllText(path, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static void ReportFatal(Exception exception)
    {
        if (Interlocked.Exchange(ref _fatalReported, 1) != 0) return;
        Write("FATAL: " + exception);
        if (SuppressDialogs) return;
        try
        {
            MessageBox.Show("SRBMiner Monitor 無法啟動或繼續執行。\n\n" +
                exception.GetBaseException().Message + "\n\n" +
                (LogPath is { } path ? "錯誤紀錄：\n" + path : "無法建立錯誤紀錄，請執行 Start-Diagnostics.cmd。"),
                "SRBMiner Monitor 啟動錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception) { /* Error reporting must not hide the original failure. */ }
    }
}
