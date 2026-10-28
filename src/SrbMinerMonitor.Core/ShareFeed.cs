namespace SrbMinerMonitor.Core;

public sealed record ShareNotice(Guid Id, DateTimeOffset Time, string Category, string Message, bool Replace = false);

// Publish newly confirmed shares immediately, then enrich that entry if buffered stdout arrives later.
public sealed class ShareFeed
{
    private readonly Dictionary<string, long> _published = [];
    private readonly Dictionary<(string Category, long Count), ShareNotice> _pending = [];
    public void Reset() { _published.Clear(); _pending.Clear(); }
    public IReadOnlyList<ShareNotice> ObserveApi(ShareStats shares, DateTimeOffset now, bool publishInitial)
    {
        var notices = new List<ShareNotice>();
        foreach (var (category, count) in new[] { ("成功", shares.Accepted), ("失敗", shares.Rejected), ("逾時", shares.Timeout) })
        {
            if (count is not long current) continue;
            var previous = _published.GetValueOrDefault(category);
            var first = !_published.ContainsKey(category);
            if (current <= previous) { _published.TryAdd(category, current); continue; }
            _published[category] = current;
            if (first && !publishInitial) continue; // External miner's historical total is not a new event.
            var label = category switch { "成功" => "Accepted 成功", "失敗" => "Rejected 失敗", _ => "Timeout 逾時" };
            // Keep one entry per confirmed share so delayed raw lines can enrich it exactly.
            // Bound a large initial jump just as the UI bounds its retained event history.
            for (var ordinal = Math.Max(previous + 1, current - 99); ; ordinal++)
            {
                var notice = new ShareNotice(Guid.NewGuid(), now, category, $"[{now:yyyy-MM-dd HH:mm:ss}] {label} · API 已確認，累計 {ordinal}");
                _pending[(category, ordinal)] = notice;
                if (_pending.Count > 2000) _pending.Remove(_pending.Keys.First());
                notices.Add(notice);
                if (ordinal == current) break;
            }
        }
        return notices;
    }
    public ShareNotice? ObserveOutput(string line, ShareStats observedOutput, DateTimeOffset now)
    {
        var category = MinerOutput.Outcome(line);
        if (category is null) return null;
        var count = category switch { "成功" => observedOutput.Accepted, "失敗" => observedOutput.Rejected, _ => observedOutput.Timeout };
        if (count is not long current) return null;
        if (_pending.Remove((category, current), out var confirmed)) return confirmed with { Message = line, Replace = true };
        if (current <= _published.GetValueOrDefault(category)) return null;
        _published[category] = current;
        return new(Guid.NewGuid(), now, category, line);
    }
}
