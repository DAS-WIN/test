using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SrbMinerMonitor.Core;

public sealed record ProbeResult(string StandardOutput, string StandardError, int? ExitCode, bool TimedOut, TimeSpan Elapsed)
{
    public string Output => (StandardOutput + "\n" + StandardError).Trim();
    public string Details => $"退出碼：{ExitCode?.ToString() ?? "未取得"} · 耗時：{Elapsed.TotalSeconds:0.0} 秒\n" +
        (Output.Length == 0 ? "（礦工沒有輸出任何內容）" : Output);
    public void EnsureSuccess()
    {
        if (TimedOut) throw new TimeoutException("超過 10 秒，已結束讀取程序，原清單保留。");
        if (ExitCode != 0) throw new InvalidOperationException($"讀取程序退出碼 {ExitCode}，原清單保留。請查看下方原始輸出。");
        if (Output.Length == 0) throw new FormatException("讀取程序已執行，但未輸出任何資料，原清單保留。");
    }
}

public static partial class ExecutableProbe
{
    public static async Task<string> RunAsync(string executable, string argument, CancellationToken ct = default)
    {
        var result = await ExecuteAsync(executable, argument, ct);
        result.EnsureSuccess();
        return result.Output;
    }
    public static async Task<ProbeResult> ExecuteAsync(string executable, string argument, CancellationToken ct = default)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("請先選擇有效的 SRBMiner 執行檔。", executable);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var process = new Process { StartInfo = new(executable)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executable))!
        }};
        process.StartInfo.ArgumentList.Add(argument);
        var watch = Stopwatch.StartNew();
        process.Start();
        var output = new System.Text.StringBuilder();
        var errors = new System.Text.StringBuilder();
        var timedOut = false;
        var stdout = ReadBounded(process.StandardOutput, output, timeout.Token);
        var stderr = ReadBounded(process.StandardError, errors, timeout.Token);
        try
        {
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { timedOut = true; }
        finally
        {
            if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); }
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
        return new(output.ToString(), errors.ToString(), process.ExitCode, timedOut, watch.Elapsed);
    }
    private static async Task ReadBounded(StreamReader reader, System.Text.StringBuilder output, CancellationToken ct)
    {
        var chars = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(chars, ct)) > 0)
        {
            if (output.Length + read > 1024 * 1024) throw new FormatException("礦工輸出超過 1 MB 上限。");
            output.Append(chars, 0, read);
        }
    }

    public static string[] ParseAlgorithms(string output)
    {
        var ignored = new HashSet<string>(["algorithm", "algorithms", "name", "supported", "available", "srbminer", "srbminer-multi", "devfee", "fee", "cpu", "gpu", "usage", "version", "copyright", "error", "warning", "note", "runtime", "unsupported", "failed"], StringComparer.OrdinalIgnoreCase);
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in StripAnsi().Replace(output, "").Split('\n'))
        {
            var match = AlgorithmRow().Match(raw.Trim().Trim('|').Trim());
            if (match.Success && !ignored.Contains(match.Groups[1].Value)) result.Add(match.Groups[1].Value.ToLowerInvariant());
        }
        if (result.Count == 0) throw new FormatException("無法辨識演算法清單格式，原清單已保留。請將 --list-algorithms 輸出提供作為相容性樣本。");
        return result.ToArray();
    }

    public static IReadOnlyList<(int Id, string Name)> ParseDevices(string output)
    {
        var result = new Dictionary<int, string>();
        foreach (var line in StripAnsi().Replace(output, "").Split('\n'))
        {
            var match = DeviceRow().Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var id)) result[id] = match.Groups[2].Value.Trim();
        }
        if (result.Count == 0) throw new FormatException("無法辨識 GPU 清單，請參照 --list-devices 輸出手動輸入 GPU ID。");
        return result.OrderBy(p => p.Key).Select(p => (p.Key, p.Value)).ToArray();
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")] private static partial Regex StripAnsi();
    [GeneratedRegex(@"^(?:\[?\s*\d+\s*\]?[.):\-]?\s+)?([a-z][a-z0-9_-]{1,40})(?:\s*[|:]\s*.*|\s{2,}[\d.].*|\s+\d+(?:\.\d+)?%.*|\s*)$", RegexOptions.IgnoreCase)]
    private static partial Regex AlgorithmRow();
    [GeneratedRegex(@"^\s*(?:(?:GPU|Device)\s*)?[\[#]?\s*(\d+)\s*[\])\.:]?\s*[:\-]?\s+(?:\[[^\]]*\]\s*)?((?:NVIDIA|AMD|INTEL|Radeon|GeForce).+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DeviceRow();
}
