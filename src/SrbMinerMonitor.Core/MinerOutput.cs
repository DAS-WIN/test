using System.Text.RegularExpressions;

namespace SrbMinerMonitor.Core;

// Receives only new stdout/stderr from the owned process, never rereads a historical log.
public sealed partial class MinerOutput
{
    private readonly Dictionary<int, ShareStats> _gpus = [];
    private bool _shareTable;
    public string? Pool { get; private set; }
    public IReadOnlyDictionary<int, ShareStats> Gpus => _gpus;
    public ShareStats Total => new(Sum(s => s.Accepted), Sum(s => s.Rejected), HardwareErrors: Sum(s => s.HardwareErrors));
    private long? Sum(Func<ShareStats, long?> field) => _gpus.Count == 0 || _gpus.Values.Any(s => field(s) is null)
        ? null : _gpus.Values.Sum(s => field(s)!.Value);
    public static string Clean(string line) => Ansi().Replace(line, "").TrimEnd('\r');
    public void Reset(string? pool = null) { _gpus.Clear(); Pool = pool; _shareTable = false; }
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
            _gpus[rowId] = new(accepted, rejected, HardwareErrors: hardware);
            return;
        }
        var share = Share().Match(line);
        if (!share.Success || !int.TryParse(share.Groups[1].Value, out var id)) return;
        var previous = _gpus.GetValueOrDefault(id) ?? new(0, 0);
        _gpus[id] = share.Groups[2].Value.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            ? previous with { Accepted = (previous.Accepted ?? 0) + 1 }
            : previous with { Rejected = (previous.Rejected ?? 0) + 1 };
    }
    // API cumulative values win. Never add the same shares from two sources.
    public static ShareStats Prefer(ShareStats? api, ShareStats? output) => new(
        api?.Accepted ?? output?.Accepted, api?.Rejected ?? output?.Rejected,
        api?.Stale ?? output?.Stale, api?.Timeout ?? output?.Timeout, api?.HardwareErrors ?? output?.HardwareErrors);
    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]")] private static partial Regex Ansi();
    [GeneratedRegex(@"\bGPU\s*#?(\d+)(?:\[t\d+\])?\s+share\s+(accepted|rejected)\b", RegexOptions.IgnoreCase)] private static partial Regex Share();
    [GeneratedRegex(@"(?:^|\]\s*)#(\d+)\s+.+?\s+(\d+)\s+(\d+)\s+(\d+)\s*$")] private static partial Regex TableRow();
}
