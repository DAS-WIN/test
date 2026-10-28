using System.Net.NetworkInformation;

namespace SrbMinerMonitor.Core;

public static class LocalApiPort
{
    public static async Task<bool> WaitUntilReleasedAsync(int port, TimeSpan timeout)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port))
        {
            if (watch.Elapsed >= timeout) return false;
            await Task.Delay(50);
        }
        return true;
    }
    public static void EnsureAvailable(int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        // Connecting to an unused Windows loopback port may time out before returning refusal.
        // Inspect the local listener table instead; no socket is opened and no timeout is mistaken for a busy port.
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port))
            throw new InvalidOperationException($"API 連接埠 {port} 已有程序使用。外部礦工僅供監控，請先關閉該礦工或變更連接埠。");
    }
}
