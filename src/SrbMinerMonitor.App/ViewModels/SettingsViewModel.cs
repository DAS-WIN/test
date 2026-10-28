using System.Collections.ObjectModel;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private string _executable = "", _algorithm = "", _rigName = "rig-01", _poolHost = "", _poolPort = "3333", _wallet = "", _worker = "rig-01", _password = "", _apiPort = "21550", _gpuIds = "";
    private bool _tls;
    public string Executable { get => _executable; set { if (Set(ref _executable, value)) Changed(nameof(AlgorithmHint)); } }
    public string Algorithm { get => _algorithm; set => Set(ref _algorithm, value); }
    public string RigName { get => _rigName; set => Set(ref _rigName, value); }
    public string PoolHost { get => _poolHost; set => Set(ref _poolHost, value); }
    public string PoolPort { get => _poolPort; set => Set(ref _poolPort, value); }
    public string Wallet { get => _wallet; set => Set(ref _wallet, value); }
    public string Worker { get => _worker; set => Set(ref _worker, value); }
    public string Password { get => _password; set => Set(ref _password, value); }
    public string ApiPort { get => _apiPort; set => Set(ref _apiPort, value); }
    public string GpuIds
    {
        get => _gpuIds;
        set
        {
            if (!Set(ref _gpuIds, value)) return;
            var selected = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            foreach (var device in Devices) device.SetSelectionFromIds(selected.Contains(device.Id.ToString()));
        }
    }
    public bool Tls { get => _tls; set => Set(ref _tls, value); }
    public string AlgorithmSource { get; private set; } = "";
    public string AlgorithmHint => string.IsNullOrEmpty(AlgorithmSource) ? "尚未讀取 · 清單以所選礦工版本為準" : AlgorithmSource == Executable
        ? $"已讀取 {Algorithms.Count} 個演算法 · 清單自動去重" : "執行檔已變更，請重新讀取演算法清單";
    public ObservableCollection<string> Algorithms { get; } = [];
    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    public void Load(MinerSettings s)
    {
        Executable = s.Executable; RigName = s.RigName; PoolHost = s.PoolHost; PoolPort = s.PoolPort.ToString();
        Wallet = s.Wallet; Worker = s.Worker; Password = s.Password; Tls = s.Tls; ApiPort = s.ApiPort.ToString();
        GpuIds = string.Join(',', s.GpuIds); Algorithms.Clear();
        foreach (var algorithm in s.Algorithms) Algorithms.Add(algorithm);
        Algorithm = s.Algorithm; AlgorithmSource = s.AlgorithmSource;
        Devices.Clear(); Changed(nameof(AlgorithmHint));
    }
    public void SetAlgorithms(string[] algorithms, string source)
    {
        var selected = Algorithm;
        Algorithms.Clear(); foreach (var name in algorithms) Algorithms.Add(name);
        Algorithm = algorithms.Contains(selected, StringComparer.OrdinalIgnoreCase) ? algorithms.First(a => a.Equals(selected, StringComparison.OrdinalIgnoreCase)) : "";
        AlgorithmSource = source; Changed(nameof(AlgorithmHint));
    }
    public MinerSettings ToSettings(RestartOptions restart)
    {
        if (!int.TryParse(ApiPort, out var api) || api is < 1 or > 65535 || !int.TryParse(PoolPort, out var pool) || pool is < 1 or > 65535)
            throw new ArgumentException("API 與礦池連接埠必須介於 1–65535。");
        var ids = new List<int>();
        foreach (var token in GpuIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(token, out var id) || id < 0) throw new ArgumentException("GPU ID 請使用以逗號分隔的非負整數。");
            ids.Add(id);
        }
        return new() { Executable = Executable.Trim(), Algorithm = Algorithm.Trim(), RigName = RigName.Trim(),
            PoolHost = PoolHost.Trim(), PoolPort = pool, Wallet = Wallet.Trim(), Worker = Worker.Trim(), Password = Password,
            Tls = Tls, ApiPort = api, GpuIds = ids.Distinct().Order().ToArray(), Algorithms = Algorithms.ToArray(),
            AlgorithmSource = AlgorithmSource, Restart = restart };
    }
}

public sealed class DeviceChoice(int id, string name, bool selected, Action selectionChanged) : ObservableObject
{
    private bool _selected = selected;
    public int Id { get; } = id;
    public string Name { get; } = $"#{id}  {name}";
    public bool Selected { get => _selected; set { if (Set(ref _selected, value)) selectionChanged(); } }
    public void SetSelectionFromIds(bool selected) => Set(ref _selected, selected, nameof(Selected));
}
