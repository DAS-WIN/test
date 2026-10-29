namespace SrbMinerMonitor.Core;

public interface IMinerProcess
{
    bool IsRunning { get; }
    Task StartAsync(MinerSettings settings, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
