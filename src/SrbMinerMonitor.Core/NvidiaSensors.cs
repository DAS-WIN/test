using System.Diagnostics;
using System.Globalization;

namespace SrbMinerMonitor.Core;

public sealed record NvidiaSensor(int Bus, string Name, double? Power, double? Fan, double? Temperature, double? Core, double? Memory);

public static class NvidiaSensors
{
    public static async Task<IReadOnlyList<NvidiaSensor>> ReadAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        using var process = new Process { StartInfo = new("nvidia-smi")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--query-gpu=pci.bus_id,name,power.draw,fan.speed,temperature.gpu,clocks.current.graphics,clocks.current.memory");
        process.StartInfo.ArgumentList.Add("--format=csv,noheader,nounits");
        try
        {
            process.Start();
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(timeout.Token));
            return process.ExitCode == 0 ? Parse(stdout.Result) : [];
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or OperationCanceledException) { return []; }
        finally { if (process.IdOrNull() is not null && !process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    private static int? IdOrNull(this Process process) { try { return process.Id; } catch (InvalidOperationException) { return null; } }

    public static IReadOnlyList<NvidiaSensor> Parse(string csv)
    {
        var result = new List<NvidiaSensor>();
        foreach (var row in csv.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var cols = row.Split(',').Select(v => v.Trim()).ToArray();
            if (cols.Length != 7) continue;
            var pci = cols[0].Split(':');
            if (pci.Length != 3 || !int.TryParse(pci[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bus)) continue;
            double? N(int i) => double.TryParse(cols[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) && n >= 0 ? n : null;
            result.Add(new(bus, cols[1], N(2), N(3), N(4), N(5), N(6)));
        }
        return result;
    }
    public static MinerSnapshot Merge(MinerSnapshot snapshot, IReadOnlyDictionary<int, int> buses, IReadOnlyList<NvidiaSensor> sensors) =>
        snapshot with { Gpus = snapshot.Gpus.Select(g =>
        {
            if (!buses.TryGetValue(g.Id, out var bus)) return g;
            var matches = sensors.Where(s => s.Bus == bus).ToArray();
            if (matches.Length != 1) return g;
            var s = matches[0];
            return g with { Name = s.Name, Power = s.Power, Fan = s.Fan, Temperature = s.Temperature, CoreClock = s.Core, MemoryClock = s.Memory };
        }).ToArray() };
}
