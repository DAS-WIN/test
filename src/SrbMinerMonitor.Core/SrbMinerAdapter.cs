namespace SrbMinerMonitor.Core;

public sealed class SrbMinerAdapter : IMinerAdapter
{
    public MinerDescriptor Descriptor { get; } = new("srbminer", "SRBMiner-Multi", [],
        new(true, true, true, false, true, false, true, new HashSet<string>()), TimeSpan.FromSeconds(1));
    public string? ValidateSettings(MinerSettings settings, bool checkFile = true)
    {
        if (settings.MinerId != Descriptor.Id) return "挖礦程式與轉接器不一致。";
        if (settings.Validate(checkFile) is string error) return error;
        if (!checkFile) return null;
        try
        {
            var text = settings.UsePastedBatch ? settings.BatchContent : File.ReadAllText(settings.BatchFile);
            if (text.Length > 1024 * 1024) return "BAT 超過 1 MB。";
            if (!BatchFileInspector.HasMinerCommand(text))
                return "BAT 無法辨識 SRBMiner-MULTI.exe 指令，請確認挖礦程式與 BAT。";
            if (settings.UsePastedBatch) _ = PastedBatch.Prepare(text, settings.ApiPort);
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { return e.Message; }
        return null;
    }
    public MinerLaunchPlan PrepareLaunch(MinerSettings settings)
    {
        if (ValidateSettings(settings) is string error) throw new ArgumentException(error);
        return settings.UsePastedBatch
            ? new("", PastedBatch.Prepare(settings.BatchContent, settings.ApiPort), Path.GetFullPath(settings.BatchDirectory), settings.ApiPort)
            : new(Path.GetFullPath(settings.BatchFile), null, Path.GetDirectoryName(Path.GetFullPath(settings.BatchFile))!, settings.ApiPort);
    }
    public BatchInspection InspectBatch(string content, string directory) => BatchFileInspector.Inspect(content, directory);
    public byte[] CreateMonitoringCopy(byte[] original, int port) => BatchFileInspector.CreateMonitoringCopy(original, port);
    public IMinerMonitorSession CreateMonitorSession(MinerSettings settings, Guid sessionId) => new Session(settings.ApiPort, sessionId);

    private sealed class Session(int port, Guid id) : IMinerMonitorSession
    {
        private readonly ApiReader _api = new();
        private readonly MinerOutput _output = new();
        public Guid SessionId => id;
        public async Task<MonitorReading> ReadAsync(CancellationToken ct)
        {
            var data = await _api.ReadAsync(port, ct);
            var snapshot = data.Snapshot;
            snapshot = snapshot with { Algorithms = [new(snapshot.Algorithm ?? "unknown", snapshot.Algorithm ?? "—", "H/s",
                snapshot.TotalHashrate, snapshot.Shares, snapshot.Devices.ToDictionary(d => d.Id, d => d.Hashrate))] };
            return new(snapshot, data.Buses);
        }
        public OutputObservation ConsumeOutput(string line, DateTimeOffset now)
        {
            var text = MinerOutput.Clean(line); _output.Read(text);
            var category = MinerOutput.Outcome(text);
            return new(text, category is null ? null : new(now, category, text, Guid.NewGuid()));
        }
        public ShareStats GetShares(MinerSnapshot? snapshot, int? deviceId = null)
        {
            if (snapshot is not null) _output.Reconcile(snapshot);
            var total = MinerOutput.Max(snapshot?.Shares, _output.Total);
            if (deviceId is not int id) return total;
            var single = snapshot?.Gpus.Count == 1;
            return MinerOutput.Max(snapshot?.Gpus.FirstOrDefault(g => g.Id == id)?.Shares ?? (single ? snapshot?.Shares : null),
                _output.Gpus.GetValueOrDefault(id) ?? (single ? total : null));
        }
        public void ResetCounters() => _output.Reset();
        public ValueTask DisposeAsync() { _api.Dispose(); return ValueTask.CompletedTask; }
    }
}
