namespace SrbMinerMonitor.Core;

public sealed record MinerCapabilities(bool Api, bool OutputEvents, bool PerDeviceShares,
    bool Cpu, bool Gpu, bool MultipleAlgorithms, bool AutomaticApiArguments,
    IReadOnlySet<string> HardwareFields);
public sealed record MinerDescriptor(string Id, string Name, IReadOnlyList<string> VerifiedVersions,
    MinerCapabilities Capabilities, TimeSpan PollInterval);
public sealed record MinerLaunchPlan(string BatchFile, byte[]? BatchBytes, string WorkingDirectory,
    int? ApiPort);
public sealed record MonitorReading(MinerSnapshot Snapshot, IReadOnlyDictionary<int, int> DeviceBuses);
public sealed record OutputObservation(string Text, MonitorEvent? Event);

public interface IMinerAdapter
{
    MinerDescriptor Descriptor { get; }
    string? ValidateSettings(MinerSettings settings, bool checkFile = true);
    MinerLaunchPlan PrepareLaunch(MinerSettings settings);
    BatchInspection InspectBatch(string content, string directory);
    byte[] CreateMonitoringCopy(byte[] original, int port);
    IMinerMonitorSession CreateMonitorSession(MinerSettings settings, Guid sessionId);
}
public interface IMinerMonitorSession : IAsyncDisposable
{
    Guid SessionId { get; }
    Task<MonitorReading> ReadAsync(CancellationToken ct);
    OutputObservation ConsumeOutput(string line, DateTimeOffset now);
    ShareStats GetShares(MinerSnapshot? snapshot, int? deviceId = null);
    void ResetCounters();
}
public sealed class MinerAdapterRegistry
{
    private readonly IReadOnlyDictionary<string, IMinerAdapter> _adapters;
    public static MinerAdapterRegistry Default { get; } = new([new SrbMinerAdapter()]);
    public MinerAdapterRegistry(IEnumerable<IMinerAdapter> adapters)
    {
        _adapters = adapters.ToDictionary(a => a.Descriptor.Id, StringComparer.Ordinal);
        if (_adapters.Values.Any(a => a.Descriptor.PollInterval <= TimeSpan.Zero))
            throw new ArgumentException("輪詢間隔必須為正數。");
    }
    public IReadOnlyList<MinerDescriptor> Descriptors => _adapters.Values.Select(a => a.Descriptor).ToArray();
    public IMinerAdapter Get(string id) => _adapters.TryGetValue(id, out var adapter) ? adapter
        : throw new NotSupportedException($"不支援的挖礦程式：{id}。請更新應用程式。");
}
