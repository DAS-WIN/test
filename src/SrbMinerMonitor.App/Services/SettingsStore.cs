using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.App.Services;

public sealed class SettingsStore
{
    public string DirectoryPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SRBMiner Monitor");
    private string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public async Task<MinerSettings> LoadAsync()
    {
        if (!File.Exists(FilePath)) return new();
        var settings = JsonSerializer.Deserialize<MinerSettings>(await File.ReadAllTextAsync(FilePath))
            ?? throw new FormatException("設定檔內容為空。");
        if (settings.ProtectedPassword is { Length: > 0 } encrypted)
            settings = settings with { Password = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted), null, DataProtectionScope.CurrentUser)) };
        return settings;
    }
    public async Task SaveAsync(MinerSettings settings)
    {
        Directory.CreateDirectory(DirectoryPath);
        var encrypted = string.IsNullOrEmpty(settings.Password) ? null : Convert.ToBase64String(
            ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.Password), null, DataProtectionScope.CurrentUser));
        var json = JsonSerializer.Serialize(settings with { ProtectedPassword = encrypted }, new JsonSerializerOptions { WriteIndented = true });
        var temp = FilePath + ".tmp";
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, FilePath, true);
    }
}
