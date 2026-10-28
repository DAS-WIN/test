using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.App.Services;

public sealed class DemoMiner : IMinerProcess
{
    public bool IsRunning { get; private set; } = true;
    private DateTimeOffset _started = DateTimeOffset.Now;
    public Task StartAsync(MinerSettings settings, CancellationToken ct) { IsRunning = true; _started = DateTimeOffset.Now; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { IsRunning = false; return Task.CompletedTask; }
    public MinerSnapshot Snapshot(Guid session)
    {
        var seconds = (DateTimeOffset.Now - _started).TotalSeconds;
        var rate = (202.91 + Math.Sin(seconds / 17) * 1.2) * 1e12;
        var shares = new ShareStats(16 + (long)(seconds / 30), 0, null, null, 0);
        return new(DateTimeOffset.Now, "3.8.0 · 模擬", "pearlhash", seconds,
            [new(0, "NVIDIA GeForce RTX 5080", rate, 251.9, 69, 66, 2617, 14802, shares)],
            shares, rate, "pool.example.com:3333", true, null, DateTimeOffset.Now.AddSeconds(-seconds % 30), session);
    }
}
