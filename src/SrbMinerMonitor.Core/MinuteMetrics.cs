namespace SrbMinerMonitor.Core;

// Hashrate/history advance on wall-clock minute changes; API and shares retain their own cadence.
public sealed class MinuteMetrics
{
    public MinerSnapshot? Snapshot { get; private set; }
    public bool Update(MinerSnapshot snapshot)
    {
        if (Snapshot is { } previous && previous.Session == snapshot.Session && Minute(previous.Timestamp) == Minute(snapshot.Timestamp)) return false;
        Snapshot = snapshot;
        return true;
    }
    private static long Minute(DateTimeOffset value) => value.UtcTicks / TimeSpan.TicksPerMinute;
    public void Clear() => Snapshot = null;
}
