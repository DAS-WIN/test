namespace SrbMinerMonitor.Core;

public interface IHardwareSensorProvider
{
    Task<MinerSnapshot> EnrichAsync(MinerSnapshot snapshot, IReadOnlyDictionary<int, int> buses, CancellationToken ct);
}
public sealed class NvidiaSensorProvider : IHardwareSensorProvider
{
    public async Task<MinerSnapshot> EnrichAsync(MinerSnapshot snapshot, IReadOnlyDictionary<int, int> buses, CancellationToken ct)
        => NvidiaSensors.Merge(snapshot, buses, await NvidiaSensors.ReadAsync(ct));
}

public sealed class MonitoringService : IDisposable
{
    private readonly MinerAdapterRegistry _registry;
    private readonly IReadOnlyList<IHardwareSensorProvider> _sensors;
    private IMinerMonitorSession? _session;
    private MinerSettings? _settings;
    private CancellationTokenSource _stage = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private Guid _sourceSession, _metricSession;
    public MinerSnapshot? Snapshot { get; private set; }
    public MinuteMetrics Metrics { get; } = new();
    public MetricHistory History { get; } = new();
    private ObservationState _state = ObservationState.Waiting;
    public ObservationState State { get => _state == ObservationState.Valid && Snapshot is { } snapshot &&
        DateTimeOffset.Now - snapshot.Timestamp > TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(15).Ticks, (Descriptor?.PollInterval.Ticks ?? 0) * 3))
        ? ObservationState.Stale : _state; private set => _state = value; }
    public string? Error { get; private set; }
    public MinerDescriptor? Descriptor { get; private set; }
    public MonitoringService(MinerAdapterRegistry? registry = null, IEnumerable<IHardwareSensorProvider>? sensors = null)
    { _registry = registry ?? MinerAdapterRegistry.Default; _sensors = (sensors ?? [new NvidiaSensorProvider()]).ToArray(); }
    public void EnsureSession(MinerSettings settings, Guid sourceSession)
    {
        if (_session is not null && _sourceSession == sourceSession && _settings == settings) return;
        var adapter = _registry.Get(settings.MinerId);
        _stage.Cancel(); _stage.Dispose(); _stage = new();
        if (_session is not null) _ = _session.DisposeAsync();
        _sourceSession = sourceSession; _metricSession = sourceSession; _settings = settings;
        _session = adapter.CreateMonitorSession(settings, sourceSession); Descriptor = adapter.Descriptor;
        Snapshot = null; Metrics.Clear(); State = ObservationState.Waiting; Error = null;
    }
    public bool IsCurrent(Guid sourceSession) => _session is not null && _sourceSession == sourceSession;
    public OutputObservation? ConsumeOutput(Guid sourceSession, string line, DateTimeOffset now)
        => IsCurrent(sourceSession) ? _session!.ConsumeOutput(line, now) : null;
    public ShareStats GetShares(int? deviceId = null) => _session?.GetShares(Snapshot, deviceId) ?? new();
    public async Task<MonitorReading?> ReadAsync(CancellationToken ct)
    {
        var current = _session; var token = _stage.Token;
        if (current is null) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
        await _readGate.WaitAsync(linked.Token);
        try
        {
            var reading = await current.ReadAsync(linked.Token);
            if (token.IsCancellationRequested || current != _session) return null;
            var snapshot = reading.Snapshot;
            if (Snapshot?.UptimeSeconds is double previous && snapshot.UptimeSeconds < previous)
            { _metricSession = Guid.NewGuid(); current.ResetCounters(); }
            var algorithms = snapshot.Algorithms;
            if (Snapshot is { } previousSnapshot && !previousSnapshot.Algorithms.Select(a => (a.Id, a.RateUnit)).SequenceEqual(algorithms.Select(a => (a.Id, a.RateUnit))))
                _metricSession = Guid.NewGuid();
            var primary = algorithms.Count == 1 ? algorithms[0] : null;
            snapshot = snapshot with { Session = _metricSession, MinerId = Descriptor!.Id, MinerName = Descriptor.Name,
                Algorithms = algorithms, Algorithm = primary?.Name, Hashrate = primary?.Rate,
                Gpus = snapshot.Devices.Select(d => d with { Hashrate = primary?.DeviceRates.GetValueOrDefault(d.Id), RateUnit = primary?.RateUnit ?? "H/s" }).ToArray(),
                Observation = new("API", snapshot.Timestamp, _metricSession) };
            Snapshot = snapshot; _ = current.GetShares(snapshot); State = ObservationState.Valid; Error = null;
            return reading with { Snapshot = snapshot };
        }
        catch (Exception) when (token.IsCancellationRequested) { return null; }
        catch (Exception e) when (current == _session)
        { State = ObservationState.Failed; Error = e.Message; throw; }
        finally { _readGate.Release(); }
    }
    public async Task<MinerSnapshot?> SampleAsync(MonitorReading reading, MetricRefreshOptions refresh, CancellationToken ct)
    {
        var current = _session; var token = _stage.Token;
        if (!Metrics.IsDue(reading.Snapshot.Timestamp, reading.Snapshot.Session, refresh)) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, token);
        var hardware = reading.Snapshot;
        try
        {
            foreach (var sensor in _sensors) hardware = await sensor.EnrichAsync(hardware, reading.DeviceBuses, linked.Token);
            if (current != _session || token.IsCancellationRequested || Snapshot?.Session != hardware.Session) return null;
            if (!Metrics.Update(hardware, refresh)) return null;
            History.Add(hardware); return hardware;
        }
        catch (Exception) when (token.IsCancellationRequested) { return null; }
    }
    public void Dispose()
    {
        _stage.Cancel();
        if (_session is not null) _ = _session.DisposeAsync();
        _stage.Dispose();
    }
}
