using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.App.Services;

public sealed class SettingsStore
{
    public string DirectoryPath { get; }
    private readonly SemaphoreSlim _writes = new(1, 1);
    public SettingsStore(string? directory = null) => DirectoryPath = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SRBMiner Monitor");
    private string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public async Task<MinerSettings> LoadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        // Unrecognized JSON fields are ignored when loading settings.
        var settings = JsonSerializer.Deserialize<MinerSettings>(await File.ReadAllTextAsync(FilePath)) ?? throw new FormatException("設定檔內容為空。");
        if (settings.ProtectedBatchContent is { Length: > 0 } encrypted)
            settings = settings with { BatchContent = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser)) };
        return settings;
    }
    public async Task SaveAsync(MinerSettings settings)
    {
        await _writes.WaitAsync();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            var encrypted = string.IsNullOrEmpty(settings.BatchContent) ? null : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.BatchContent), null, DataProtectionScope.CurrentUser));
            var json = JsonSerializer.Serialize(settings with { ProtectedBatchContent = encrypted }, new JsonSerializerOptions { WriteIndented = true });
            var temp = FilePath + ".tmp"; await File.WriteAllTextAsync(temp, json); File.Move(temp, FilePath, true);
        }
        finally { _writes.Release(); }
    }
}
