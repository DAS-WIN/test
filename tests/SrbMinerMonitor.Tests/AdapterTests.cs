using System.Text.Json;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class AdapterTests
{
    [Fact]
    public void RegistryRejectsUnknownAndDuplicateAdapters()
    {
        Assert.Single(MinerAdapterRegistry.Default.Descriptors);
        Assert.Throws<NotSupportedException>(() => MinerAdapterRegistry.Default.Get("unknown"));
        Assert.Throws<ArgumentException>(() => new MinerAdapterRegistry([new SrbMinerAdapter(), new SrbMinerAdapter()]));
    }
    [Fact]
    public void OldSettingsMigrateWithoutLosingEncryptedContentOrUnknownFields()
    {
        const string json = "{\"ApiPort\":23456,\"ProtectedBatchContent\":\"example\",\"FutureField\":42}";
        var settings = SettingsFormat.Read(json);
        Assert.Equal("srbminer", settings.MinerId);
        Assert.Equal(23456, settings.ApiPort);
        Assert.Equal("example", settings.ProtectedBatchContent);
        Assert.True(SettingsFormat.NeedsMigration(json));
        var serialized = JsonSerializer.Serialize(settings);
        Assert.False(SettingsFormat.NeedsMigration(serialized));
        Assert.Equal(42, SettingsFormat.Read(serialized).ExtensionData!["FutureField"].GetInt32());
    }
    [Theory]
    [InlineData("{\"SchemaVersion\":2}")]
    [InlineData("{\"MinerId\":\"future-miner\"}")]
    public void UnsupportedSettingsNeverFallBack(string json)
        => Assert.Throws<NotSupportedException>(() => SettingsFormat.Read(json));

    [Fact]
    public void LaunchPreparationDoesNotCreateFilesAndRejectsWrongMiner()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var adapter = new SrbMinerAdapter();
            var settings = new MinerSettings { UsePastedBatch = true, BatchDirectory = directory,
                BatchContent = "SRBMiner-MULTI.exe --algorithm example --pool pool.example.test:4444" };
            var plan = adapter.PrepareLaunch(settings);
            Assert.NotNull(plan.BatchBytes);
            Assert.Empty(Directory.GetFiles(directory));
            Assert.Contains("--api-enable", System.Text.Encoding.UTF8.GetString(plan.BatchBytes!));
            Assert.NotNull(adapter.ValidateSettings(settings with { BatchContent = "other-miner.exe" }));
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public async Task SrbOutputRemainsIndependentFromApiAndDoesNotGenerateApiEvents()
    {
        await using var session = new SrbMinerAdapter().CreateMonitorSession(new(), Guid.NewGuid());
        var api = Snapshot(100) with { Shares = new(1, 0) };
        Assert.Equal(1, session.GetShares(api).Accepted);
        var output = session.ConsumeOutput("GPU0[t0] share accepted [100ms]", DateTimeOffset.Now);
        Assert.Equal("成功", output.Event!.Category);
        Assert.Equal(1, session.GetShares(api).Accepted);
        session.ResetCounters();
        Assert.Null(session.GetShares(null).Accepted);
    }
    [Fact]
    public async Task SecondAdapterUsesCommonPipelineWithCpuAndDifferentUnits()
    {
        var adapter = new SyntheticAdapter();
        using var monitor = new MonitoringService(new([adapter]), []);
        var id = Guid.NewGuid();
        monitor.EnsureSession(new() { MinerId = "synthetic" }, id);
        var reading = await monitor.ReadAsync(default);
        Assert.Equal("Synthetic", reading!.Snapshot.MinerName);
        Assert.Equal("Sol/s", reading.Snapshot.RateUnit);
        Assert.Equal(DeviceKind.Cpu, reading.Snapshot.Devices[0].Kind);
        Assert.Null(reading.Snapshot.TotalPower);
        Assert.Null(reading.Snapshot.Efficiency);
        Assert.Equal("1.50 kSol/s", Display.Hash(reading.Snapshot.TotalHashrate, baseUnit: reading.Snapshot.RateUnit));
        Assert.NotNull(await monitor.SampleAsync(reading, new(), default));
        Assert.Single(monitor.History.Samples);
        Assert.Null(monitor.ConsumeOutput(Guid.NewGuid(), "accepted", DateTimeOffset.Now));
        Assert.NotNull(monitor.ConsumeOutput(id, "accepted", DateTimeOffset.Now)?.Event);
    }
    [Fact]
    public async Task LateResponseAndOldOutputCannotContaminateNewSession()
    {
        var adapter = new SyntheticAdapter { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var monitor = new MonitoringService(new([adapter]), []);
        var old = Guid.NewGuid();
        monitor.EnsureSession(new() { MinerId = "synthetic" }, old);
        var request = monitor.ReadAsync(default);
        monitor.EnsureSession(new() { MinerId = "synthetic" }, Guid.NewGuid());
        adapter.Pending.SetResult(new(Snapshot(100), new Dictionary<int,int>()));
        Assert.Null(await request);
        Assert.Null(monitor.Snapshot);
        Assert.Null(monitor.ConsumeOutput(old, "accepted", DateTimeOffset.Now));
    }
    [Fact]
    public void DifferentAlgorithmsAreNeverSummed()
    {
        var snapshot = Snapshot(100) with { Algorithms = [
            new("a", "A", "H/s", 100, new(), new Dictionary<int,double?>()),
            new("b", "B", "Sol/s", 200, new(), new Dictionary<int,double?>())] };
        Assert.Null(snapshot.TotalHashrate);
        Assert.Null(snapshot.Efficiency);
    }
    [Fact]
    public void FullPciAddressResolvesAmbiguousBusWithoutTouchingCpu()
    {
        var snapshot = Snapshot(100) with { Gpus = [new DeviceMetrics(0,"gpu") { PciAddress="0000:01:00.0" }] };
        var sensors = NvidiaSensors.Parse("00000000:01:00.0, A, 100, 50, 60, 1000, 2000\n00000001:01:00.0, B, 200, 50, 60, 1000, 2000");
        Assert.Equal(100, NvidiaSensors.Merge(snapshot, new Dictionary<int,int>(), sensors).Devices[0].Power);
        snapshot = snapshot with { Gpus = [snapshot.Devices[0] with { Kind = DeviceKind.Cpu }] };
        Assert.Null(NvidiaSensors.Merge(snapshot, new Dictionary<int,int>(), sensors).Devices[0].Power);
    }
    internal static MinerSnapshot Snapshot(double rate) => new(DateTimeOffset.Now,"1.0","synthetic",10,
        [new DeviceMetrics(0,"CPU") { Kind=DeviceKind.Cpu }],new(0,0),rate)
        { Algorithms=[new("synthetic","Synthetic","Sol/s",rate,new(0,0),new Dictionary<int,double?> { [0]=rate })] };

    private sealed class SyntheticAdapter : IMinerAdapter
    {
        public TaskCompletionSource<MonitorReading>? Pending { get; init; }
        public MinerDescriptor Descriptor { get; } = new("synthetic","Synthetic",["fixture"],
            new(true,true,false,true,false,false,false,new HashSet<string>()),TimeSpan.FromSeconds(2));
        public string? ValidateSettings(MinerSettings settings, bool checkFile=true) => null;
        public MinerLaunchPlan PrepareLaunch(MinerSettings settings) => throw new NotSupportedException();
        public BatchInspection InspectBatch(string content,string directory) => new(false,null,null);
        public byte[] CreateMonitoringCopy(byte[] original,int port) => throw new NotSupportedException();
        public IMinerMonitorSession CreateMonitorSession(MinerSettings settings,Guid sessionId) => new SyntheticSession(sessionId,Pending);
    }
    private sealed class SyntheticSession(Guid id, TaskCompletionSource<MonitorReading>? pending) : IMinerMonitorSession
    {
        public Guid SessionId => id;
        public Task<MonitorReading> ReadAsync(CancellationToken ct) => pending?.Task ?? Task.FromResult(new MonitorReading(Snapshot(1500),new Dictionary<int,int>()));
        public OutputObservation ConsumeOutput(string line,DateTimeOffset now) => new(line,new(now,"成功",line));
        public ShareStats GetShares(MinerSnapshot? snapshot,int? deviceId=null) => snapshot?.Shares ?? new();
        public void ResetCounters() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
