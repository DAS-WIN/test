using System.Text;
namespace SrbMinerMonitor.Core;

public static class PastedBatch
{
    public static byte[] Prepare(string content, int port)
    {
        // CMD reads the subsequent UTF-8 lines with code page 65001. No BOM before @echo.
        var monitored = BatchFileInspector.CreateMonitoringCopy(Encoding.UTF8.GetBytes(content), port);
        return Encoding.UTF8.GetBytes("@chcp 65001 >nul\r\n").Concat(monitored).ToArray();
    }
    public static async Task<string> CreateAsync(MinerSettings settings, CancellationToken ct)
    {
        if (settings.Validate() is string error) throw new ArgumentException(error);
        var bytes = Prepare(settings.BatchContent, settings.ApiPort);
        var path = Path.Combine(Path.GetFullPath(settings.BatchDirectory), "SRBMiner-Monitor-" + Guid.NewGuid().ToString("N") + ".bat");
        var created = false;
        try
        {
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true);
            created = true;
            await stream.WriteAsync(bytes, ct);
            return path;
        }
        catch { if (created && File.Exists(path)) File.Delete(path); throw; }
    }
}
