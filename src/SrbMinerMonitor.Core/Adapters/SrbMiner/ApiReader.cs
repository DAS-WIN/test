using System.Net;
using System.Text.Json;

namespace SrbMinerMonitor.Core;

public static class ApiParser
{
    // The algorithms/hashrate/shares paths are verified against HiveOS's SRBMiner adapter.
    // Optional sensors are supplied separately, never inferred from unrelated JSON fields.
    public static MinerSnapshot Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var algorithms = Field(root, "algorithms");
        if (algorithms.ValueKind != JsonValueKind.Array || algorithms.GetArrayLength() != 1)
            throw new FormatException("目前僅支援含有單一演算法的 SRBMiner API。請確認礦工設定與版本。");
        var algorithm = algorithms[0];
        var rates = Field(algorithm, "hashrate");
        var gpuRates = Field(rates, "gpu");
        var devices = Field(root, "gpu_devices");
        var gpus = new List<GpuMetrics>();
        if (gpuRates.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in gpuRates.EnumerateObject())
            {
                if (!property.Name.StartsWith("gpu", StringComparison.Ordinal) || !int.TryParse(property.Name.AsSpan(3), out var id)) continue;
                string? name = null;
                if (devices.ValueKind == JsonValueKind.Array)
                {
                    var device = devices.EnumerateArray().Select((d, index) => (Device: d, Id: Number(d, "device_id") ?? index))
                        .FirstOrDefault(d => d.Id == id).Device;
                    name = Text(device, "name");
                }
                gpus.Add(new(id, name ?? $"GPU #{id}", Numeric(property.Value)));
            }
        }
        var shares = Field(algorithm, "shares");
        return new(now, Text(root, "miner_version"), Text(algorithm, "name"), Number(root, "mining_time"),
            gpus.OrderBy(g => g.Id).ToArray(), new(Count(shares, "accepted"), Count(shares, "rejected")), Number(rates, "now"));
    }

    public static IReadOnlyDictionary<int, int> DeviceBuses(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var devices = Field(doc.RootElement, "gpu_devices");
        var result = new Dictionary<int, int>();
        if (devices.ValueKind == JsonValueKind.Array)
            foreach (var item in devices.EnumerateArray().Select((d, index) => (Device: d, Index: index)))
                if (Number(item.Device, "bus_id") is double bus)
                    result[(int)(Number(item.Device, "device_id") ?? item.Index)] = (int)bus;
        return result;
    }
    private static JsonElement Field(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : default;
    private static string? Text(JsonElement e, string key) => Field(e, key) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Numeric(JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out var d) && double.IsFinite(d) && d >= 0 ? d : null;
    private static double? Number(JsonElement e, string key) => Numeric(Field(e, key));
    private static long? Count(JsonElement e, string key) => Field(e, key) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;
}

public sealed class ApiReader : IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(3) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    public async Task<(MinerSnapshot Snapshot, IReadOnlyDictionary<int, int> Buses)> ReadAsync(int port, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var response = await _client.GetAsync(new Uri($"http://127.0.0.1:{port}"), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 2 * 1024 * 1024) throw new FormatException("API 回應超過 2 MB 上限。");
                buffer.Write(chunk, 0, read);
            }
            var json = System.Text.Encoding.UTF8.GetString(buffer.ToArray());
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("礦工 API 尚未提供資料，正在等待初始化完成。");
            return (ApiParser.Parse(json, DateTimeOffset.Now), ApiParser.DeviceBuses(json));
        }
        finally { _gate.Release(); }
    }
    public void Dispose() { _client.Dispose(); _gate.Dispose(); }
}
