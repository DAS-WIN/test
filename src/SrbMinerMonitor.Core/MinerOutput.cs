using System.Text.RegularExpressions;

namespace SrbMinerMonitor.Core;

// Receives only new stdout/stderr from the owned process, never rereads a historical log.
public sealed partial class MinerOutput
{
    private readonly Dictionary<int, ShareStats> _gpus = [];
    private bool _shareTable;
    private ShareStats _total = new(), _api = new();
    public string? Pool { get; private set; }
    public IReadOnlyDictionary<int, ShareStats> Gpus => _gpus;
    public ShareStats Total => Max(_api, ObservedTotal);
    public ShareStats ObservedTotal => Max(_total, new(Sum(s => s.Accepted), Sum(s => s.Rejected), Timeout: Sum(s => s.Timeout), HardwareErrors: Sum(s => s.HardwareErrors)));
    private long? Sum(Func<ShareStats, long?> field) => _gpus.Count == 0 || _gpus.Values.Any(s => field(s) is null)
        ? null : _gpus.Values.Sum(s => field(s)!.Value);
    public static string Clean(string line) => Ansi().Replace(line, "").TrimEnd('\r');
    public void Reset(string? pool = null) { _gpus.Clear(); _total = new(); _api = new(); Pool = pool; _shareTable = false; }
    public void Read(string line)
    {
        line = Clean(line);
        if (line.Contains("devfee", StringComparison.OrdinalIgnoreCase) || line.Contains("donation", StringComparison.OrdinalIgnoreCase)) return;
        if (line.Contains("Hashrate", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(line, @"\bA\s+R\s+HW\b")) _shareTable = true;
        var row = _shareTable ? TableRow().Match(line) : Match.Empty;
        if (row.Success && int.TryParse(row.Groups[1].Value, out var rowId)
            && long.TryParse(row.Groups[2].Value, out var accepted) && long.TryParse(row.Groups[3].Value, out var rejected)
            && long.TryParse(row.Groups[4].Value, out var hardware))
        {
            _gpus[rowId] = Max(_gpus.GetValueOrDefault(rowId), new(accepted, rejected, HardwareErrors: hardware));
            _total = ObservedTotal;
            return;
        }
        var share = Share().Match(line);
        if (!share.Success || !int.TryParse(share.Groups[1].Value, out var id)) return;
        var outcome = Outcome(line);
        _gpus[id] = Increment(_gpus.GetValueOrDefault(id) ?? new(0, 0), outcome);
        _total = Increment(_total, outcome);
    }
    public void Reconcile(MinerSnapshot snapshot)
    {
        // API can arrive before its buffered stdout line. Keep an independent baseline;
        // feeding it into the output counter would count the same share twice.
        _api = Max(_api, snapshot.Shares);
    }

    private static ShareStats Increment(ShareStats previous, string? outcome) => outcome switch
    {
        "成功" => previous with { Accepted = (previous.Accepted ?? 0) + 1 },
        "失敗" => previous with { Rejected = (previous.Rejected ?? 0) + 1 },
        "逾時" => previous with { Timeout = (previous.Timeout ?? 0) + 1 },
        _ => previous
    };
    public static string? Outcome(string line)
    {
        line = Clean(line);
        if (line.Contains("devfee", StringComparison.OrdinalIgnoreCase) || line.Contains("donation", StringComparison.OrdinalIgnoreCase)) return null;
        var match = Result().Match(line);
        if (!match.Success) return null;
        return match.Groups[1].Value.ToLowerInvariant() switch { "accepted" => "成功", "rejected" or "failed" => "失敗", _ => "逾時" };
    }
    // Reconcile cumulative counts by taking the newer observed maximum, never adding sources.
    public static ShareStats Max(ShareStats? first, ShareStats? second)
    {
        static long? M(long? a, long? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
        return new(M(first?.Accepted, second?.Accepted), M(first?.Rejected, second?.Rejected), M(first?.Stale, second?.Stale), M(first?.Timeout, second?.Timeout), M(first?.HardwareErrors, second?.HardwareErrors));
    }
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")] private static partial Regex Ansi();
    [GeneratedRegex(@"\bGPU\s*#?(\d+)(?:\[t\d+\])?\s+share\s+(accepted|rejected|failed|timeout|timed\s+out)\b", RegexOptions.IgnoreCase)] private static partial Regex Share();
    [GeneratedRegex(@"(?:^|\]\s*)#(\d+)\s+.+?\s+(\d+)\s+(\d+)\s+(\d+)\s*$")] private static partial Regex TableRow();
    [GeneratedRegex(@"\bshare\s+(accepted|rejected|failed|timeout|timed\s+out)\b", RegexOptions.IgnoreCase)] private static partial Regex Result();
}
