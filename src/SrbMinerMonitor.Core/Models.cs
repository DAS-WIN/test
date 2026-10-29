using System.Text.Json.Serialization;

namespace SrbMinerMonitor.Core;

public sealed record ShareStats(long? Accepted = null, long? Rejected = null,
    long? Stale = null, long? Timeout = null, long? HardwareErrors = null)
{
    public double? Acceptance => Accepted is long a && Rejected is long r && a + r > 0
        ? (double)a / (a + r) * 100 : null;
}

public record DeviceMetrics(int Id, string Name, double? Hashrate = null, double? Power = null,
    double? Fan = null, double? Temperature = null, double? CoreClock = null,
    double? MemoryClock = null, ShareStats? Shares = null)
{
    public DeviceKind Kind { get; init; } = DeviceKind.Gpu;
    public string? Uuid { get; init; }
    public string? PciAddress { get; init; }
    public string RateUnit { get; init; } = "H/s";
    public double? Efficiency => Power > 0 && Hashrate.HasValue ? Hashrate / Power : null;
}

public sealed record MinerSnapshot(DateTimeOffset Timestamp, string? Version, string? Algorithm,
    double? UptimeSeconds, IReadOnlyList<DeviceMetrics> Gpus, ShareStats Shares,
    double? Hashrate = null, string? Pool = null, bool? PoolConnected = null,
    double? Latency = null, DateTimeOffset? LastShare = null, Guid Session = default)
{
    public string MinerId { get; init; } = "";
    public string MinerName { get; init; } = "";
    public IReadOnlyList<DeviceMetrics> Devices => Gpus;
    public IReadOnlyList<AlgorithmMetrics> Algorithms { get; init; } = [];
    public ObservationMetadata? Observation { get; init; }
    public string RateUnit => Algorithms.Count == 1 ? Algorithms[0].RateUnit : "H/s";
    // Do not present a partial sensor sum as total GPU power.
    public double? TotalPower => Gpus.Count > 0 && Gpus.All(g => g.Power.HasValue)
        ? Gpus.Sum(g => g.Power!.Value) : null;
    public double? TotalHashrate => Algorithms.Count > 1 ? null : Hashrate ?? (Gpus.Count > 0 && Gpus.All(g => g.Hashrate.HasValue)
        ? Gpus.Sum(g => g.Hashrate!.Value) : null);
    public double? Efficiency => TotalPower > 0 ? TotalHashrate / TotalPower : null;
    public double? MaxTemperature => Gpus.Where(g => g.Temperature.HasValue)
        .Select(g => g.Temperature).DefaultIfEmpty().Max();
}

public sealed record RestartOptions(bool Enabled = false, int RunValue = 6, bool Hours = true,
    int WaitMinutes = 5, bool Repeat = true)
{
    public TimeSpan RunDuration => Hours ? TimeSpan.FromHours(RunValue) : TimeSpan.FromMinutes(RunValue);
    public string? Validate() => RunValue is < 1 or > 10000 || WaitMinutes is < 1 or > 10000
        ? "運行時間與等待分鐘必須為 1–10000 的正整數。" : null;
}

public sealed record MinerSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string MinerId { get; init; } = "srbminer";
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; init; }
    public Dictionary<string, System.Text.Json.JsonElement>? AdapterSettings { get; init; }
    public string BatchFile { get; init; } = "";
    public bool UsePastedBatch { get; init; }
    [JsonIgnore] public string BatchContent { get; init; } = "";
    public string? ProtectedBatchContent { get; init; }
    public string BatchDirectory { get; init; } = "";
    public string RigName { get; init; } = "rig-01";
    public int ApiPort { get; init; } = 21550;
    public RestartOptions Restart { get; init; } = new();
    public MetricRefreshOptions MetricRefresh { get; init; } = new();
    public bool RestartOnUnexpectedExit { get; init; }

    public string? Validate(bool checkFile = true)
    {
        if (SchemaVersion != CurrentSchemaVersion) return "設定格式版本不支援，請更新應用程式。";
        if (string.IsNullOrWhiteSpace(MinerId)) return "請選擇挖礦程式。";
        if (MetricRefresh.Validate() is string intervalError) return intervalError;
        if (ApiPort is < 1 or > 65535) return "API 連接埠必須介於 1 與 65535。";
        if (UsePastedBatch)
        {
            if (string.IsNullOrWhiteSpace(BatchContent)) return "請貼上自己的 BAT 內容。";
            if (BatchContent.Length > 1024 * 1024 || BatchContent.Contains('\0')) return "BAT 內容過大或含有無效字元。";
            if (string.IsNullOrWhiteSpace(BatchDirectory) || (checkFile && !Directory.Exists(BatchDirectory))) return "請選擇礦工所在資料夾，讓 BAT 的相對路徑正常運作。";
            return null;
        }
        if (string.IsNullOrWhiteSpace(BatchFile) || !new[] { ".bat", ".cmd" }.Contains(Path.GetExtension(BatchFile), StringComparer.OrdinalIgnoreCase) || (checkFile && !File.Exists(BatchFile)))
            return "請選擇有效的 BAT 或 CMD 檔案，或改為貼上 BAT 內容。";
        return null;
    }
}

public sealed record MonitorEvent(DateTimeOffset Time, string Category, string Message, Guid Id = default)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss");
}
