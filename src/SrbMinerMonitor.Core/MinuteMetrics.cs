namespace SrbMinerMonitor.Core;

// One sampled snapshot drives every hardware value, independent of live API/share data.
public sealed class MinuteMetrics
{
    public MinerSnapshot? Snapshot { get; private set; }
    private MetricRefreshOptions _options = new();
    public bool IsDue(DateTimeOffset now, Guid session, MetricRefreshOptions options) => Snapshot is not { } previous
        || previous.Session != session || options != _options || Bucket(previous.Timestamp, options) != Bucket(now, options);
    public bool Update(MinerSnapshot snapshot, MetricRefreshOptions? options = null)
    {
        options ??= new();
        if (options.Validate() is string error) throw new ArgumentException(error);
        if (!IsDue(snapshot.Timestamp, snapshot.Session, options)) return false;
        Snapshot = snapshot; _options = options;
        return true;
    }
    // Timestamp.Ticks uses the miner machine's wall clock. The default switches at :00 each minute.
    private static long Bucket(DateTimeOffset value, MetricRefreshOptions options) => value.Ticks / options.Duration.Ticks;
    public void Clear() => Snapshot = null;
}
