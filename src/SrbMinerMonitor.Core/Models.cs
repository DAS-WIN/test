using System.Globalization;
using System.Text.Json.Serialization;

namespace SrbMinerMonitor.Core;

public sealed record ShareStats(long? Accepted = null, long? Rejected = null,
    long? Stale = null, long? Timeout = null, long? HardwareErrors = null)
{
    public double? Acceptance => Accepted is long a && Rejected is long r && a + r > 0
        ? (double)a / (a + r) * 100 : null;
}

public sealed record GpuMetrics(int Id, string Name, double? Hashrate = null, double? Power = null,
    double? Fan = null, double? Temperature = null, double? CoreClock = null,
    double? MemoryClock = null, ShareStats? Shares = null)
{
    public double? Efficiency => Power > 0 && Hashrate.HasValue ? Hashrate / Power : null;
}

public sealed record MinerSnapshot(DateTimeOffset Timestamp, string? Version, string? Algorithm,
    double? UptimeSeconds, IReadOnlyList<GpuMetrics> Gpus, ShareStats Shares,
    double? Hashrate = null, string? Pool = null, bool? PoolConnected = null,
    double? Latency = null, DateTimeOffset? LastShare = null, Guid Session = default)
{
    // Do not present a partial sensor sum as total GPU power.
    public double? TotalPower => Gpus.Count > 0 && Gpus.All(g => g.Power.HasValue)
        ? Gpus.Sum(g => g.Power!.Value) : null;
    public double? TotalHashrate => Hashrate ?? (Gpus.Count > 0 && Gpus.All(g => g.Hashrate.HasValue)
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
    public string Executable { get; init; } = "";
    public string RigName { get; init; } = "rig-01";
    public string Algorithm { get; init; } = "";
    public string PoolHost { get; init; } = "";
    public int PoolPort { get; init; } = 3333;
    public string Wallet { get; init; } = "";
    public string Worker { get; init; } = "rig-01";
    [JsonIgnore] public string Password { get; init; } = "";
    public string? ProtectedPassword { get; init; }
    public bool Tls { get; init; }
    public int ApiPort { get; init; } = 21550;
    public int[] GpuIds { get; init; } = [];
    public string[] Algorithms { get; init; } = [];
    public string AlgorithmSource { get; init; } = "";
    public RestartOptions Restart { get; init; } = new();

    public string? Validate(bool checkFile = true)
    {
        if (string.IsNullOrWhiteSpace(Executable) || (checkFile && !File.Exists(Executable))) return "請選擇有效的 SRBMiner 執行檔。";
        if (string.IsNullOrWhiteSpace(Algorithm) || Algorithm.IndexOfAny([';', ',', ' ', '\r', '\n']) >= 0) return "請選擇單一演算法。";
        if (GpuIds.Length == 0 || GpuIds.Any(id => id < 0)) return "請至少選擇一張 GPU。";
        if (string.IsNullOrWhiteSpace(PoolHost) || Uri.CheckHostName(PoolHost) == UriHostNameType.Unknown) return "請填入礦池主機名稱或 IP（不含通訊協定與連接埠）。";
        if (PoolPort is < 1 or > 65535 || ApiPort is < 1 or > 65535) return "連接埠必須介於 1 與 65535。";
        if (string.IsNullOrWhiteSpace(Wallet)) return "請填入錢包地址。";
        if (new[] { Wallet, Worker, Password }.Any(v => v.IndexOfAny([';', ',', '!', '\r', '\n']) >= 0)) return "錢包、Worker 與密碼不可包含多礦池分隔符號或換行。";
        return null;
    }

    public IReadOnlyList<string> Arguments() => ["--algorithm", Algorithm, "--disable-cpu", "--gpu-id",
        string.Join(',', GpuIds.Distinct().Order()), "--pool", PoolAddress, "--wallet", Wallet,
        "--worker", Worker, "--password", Password, "--tls", Tls ? "true" : "false",
        "--api-enable", "--api-port", ApiPort.ToString(CultureInfo.InvariantCulture), "--api-rig-name", RigName];
    [JsonIgnore] public string PoolAddress => $"{(PoolHost.Contains(':') ? $"[{PoolHost}]" : PoolHost)}:{PoolPort}";
}

public sealed record MonitorEvent(DateTimeOffset Time, string Category, string Message)
{
    public string TimeText => Time.ToLocalTime().ToString("HH:mm:ss");
}

public static class Display
{
    public static string Uptime(double? seconds) => seconds is double value && double.IsFinite(value) && value >= 0
        ? TimeSpan.FromSeconds(Math.Min(value, TimeSpan.MaxValue.TotalSeconds / 2)).ToString(@"d' 天 'hh\:mm\:ss", CultureInfo.InvariantCulture) : "—";
    public static string Number(double? value, string unit = "", string format = "0.0") =>
        value is double n && double.IsFinite(n) ? $"{n.ToString(format, CultureInfo.InvariantCulture)}{(unit.Length > 0 ? " " + unit : "")}" : "—";
    public static string Hash(double? value, bool efficiency = false, double? unitReference = null)
    {
        if (value is not double v || !double.IsFinite(v)) return "—";
        string[] units = ["H/s", "kH/s", "MH/s", "GH/s", "TH/s", "PH/s"];
        var i = 0;
        var reference = unitReference ?? v;
        while (Math.Abs(reference) >= 1000 && i < units.Length - 1) { v /= 1000; reference /= 1000; i++; }
        return Number(v, units[i] + (efficiency ? "/W" : ""), efficiency ? "0.000" : "0.00");
    }
    public static string Count(long? v) => v?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";
}
