namespace SrbMinerMonitor.Core;

public sealed record AcceptedWindow(long? Count, double? PerMinute, TimeSpan Coverage, bool MissingBoundary = false);

public sealed class AcceptedHistory
{
    private readonly List<(DateTimeOffset Time, long Count)> _points = [];
    private DateTimeOffset _started;
    public Guid Session { get; private set; }
    public long? Total => _points.Count == 0 ? null : _points[^1].Count;
    public void Begin(Guid session, DateTimeOffset now)
    {
        Session = session; _started = now; _points.Clear(); _points.Add((now, 0));
    }
    public long Observe(DateTimeOffset now, long count)
    {
        if (_points.Count == 0) throw new InvalidOperationException("Begin a session before observing accepted.");
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var last = _points[^1];
        if (count < last.Count) return 0;
        if (now < last.Time) now = last.Time;
        var delta = count - last.Count;
        if (now == last.Time) _points[^1] = (now, count); else _points.Add((now, count));
        // Keep the last point before the one-hour boundary for an exact cumulative baseline.
        var cutoff = now.AddHours(-1);
        var before = _points.FindLastIndex(p => p.Time <= cutoff);
        if (before > 0) _points.RemoveRange(0, before);
        return delta;
    }
    public AcceptedWindow Overall()
    {
        if (_points.Count == 0) return new(null, null, TimeSpan.Zero);
        var coverage = _points[^1].Time - _started;
        return coverage <= TimeSpan.Zero ? new(null, null, TimeSpan.Zero)
            : new(_points[^1].Count, _points[^1].Count / coverage.TotalMinutes, coverage);
    }
    public AcceptedWindow Window(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (_points.Count == 0) return new(null, null, TimeSpan.Zero);
        var end = _points[^1].Time;
        var start = end - duration;
        if (start < _started) start = _started;
        var coverage = end - start;
        if (coverage <= TimeSpan.Zero) return new(null, null, TimeSpan.Zero);
        var index = _points.FindLastIndex(p => p.Time <= start);
        if (index < 0) return new(null, null, coverage, true);
        var baseline = _points[index];
        // Do not assign a cumulative jump during an outage to an unknown window boundary.
        if (baseline.Time < start && index + 1 < _points.Count
            && _points[index + 1].Time - baseline.Time > TimeSpan.FromSeconds(15))
            return new(null, null, coverage, true);
        var count = _points[^1].Count - (start == _started ? 0 : baseline.Count);
        return new(count, count / coverage.TotalMinutes, coverage);
    }
}
