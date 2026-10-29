using System.Text.Json;
namespace SrbMinerMonitor.Core;

public static class SettingsFormat
{
    public static MinerSettings Read(string json)
    {
        var settings = JsonSerializer.Deserialize<MinerSettings>(json) ?? throw new FormatException("設定檔內容為空。");
        EnsureSupported(settings);
        return settings;
    }
    public static void EnsureSupported(MinerSettings settings)
    {
        if (settings.SchemaVersion != MinerSettings.CurrentSchemaVersion)
            throw new NotSupportedException("設定格式版本不支援，請更新應用程式。原設定已保留。");
        _ = MinerAdapterRegistry.Default.Get(settings.MinerId);
    }
    public static bool NeedsMigration(string json)
    {
        using var document = JsonDocument.Parse(json);
        return !document.RootElement.TryGetProperty(nameof(MinerSettings.SchemaVersion), out _)
            || !document.RootElement.TryGetProperty(nameof(MinerSettings.MinerId), out _);
    }
}
