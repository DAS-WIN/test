namespace SrbMinerMonitor.Core;

public sealed class MetricHistory
{
    private readonly List<MinerSnapshot> _samples = [];
    public IReadOnlyList<MinerSnapshot> Samples => _samples;
    public void Add(MinerSnapshot snapshot)
    {
        _samples.Add(snapshot);
        _samples.RemoveAll(s => s.Timestamp < snapshot.Timestamp.AddHours(-6));
    }
    public void Clear() => _samples.Clear();
    public IReadOnlyList<MinerSnapshot> Window(DateTimeOffset now, TimeSpan duration) =>
        _samples.Where(s => s.Timestamp >= now - duration).ToArray();
    public (double? Value, TimeSpan Coverage) Average(DateTimeOffset now, TimeSpan duration, int? gpu = null)
    {
        var samples = Window(now, duration);
        if (samples.Count == 0) return (null, TimeSpan.Zero);
        // Averages belong to the latest session. No synthetic values are inserted during outages.
        var session = samples[^1].Session;
        var values = samples.Where(s => s.Session == session).Select(s =>
            (s.Timestamp, Value: gpu is int id ? s.Gpus.FirstOrDefault(g => g.Id == id)?.Hashrate : s.TotalHashrate))
            .Where(s => s.Value.HasValue).ToArray();
        return values.Length == 0 ? (null, TimeSpan.Zero) : (values.Average(s => s.Value), values[^1].Timestamp - values[0].Timestamp);
    }
}
