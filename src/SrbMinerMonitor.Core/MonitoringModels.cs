namespace SrbMinerMonitor.Core;

public enum DeviceKind { Gpu, Cpu }
public enum ObservationState { Valid, Waiting, Failed, Stale }
public sealed record ObservationMetadata(string Source, DateTimeOffset Timestamp, Guid Session,
    ObservationState State = ObservationState.Valid);
public sealed record AlgorithmMetrics(string Id, string Name, string RateUnit, double? Rate,
    ShareStats Shares, IReadOnlyDictionary<int, double?> DeviceRates);

// Source compatibility for callers of the 0.2.0 core API.
public sealed record GpuMetrics : DeviceMetrics
{
    public GpuMetrics(int Id, string Name, double? Hashrate = null, double? Power = null,
        double? Fan = null, double? Temperature = null, double? CoreClock = null,
        double? MemoryClock = null, ShareStats? Shares = null)
        : base(Id, Name, Hashrate, Power, Fan, Temperature, CoreClock, MemoryClock, Shares) { }
}
