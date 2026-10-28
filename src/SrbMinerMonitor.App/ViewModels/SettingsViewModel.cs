using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.App.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private string _mode = "選擇現有 BAT", _batchFile = "", _batchContent = "", _directory = "", _rigName = "rig-01", _apiPort = "21550";
    private string _refreshValue = "1", _refreshUnit = "分";
    private bool _restartOnUnexpectedExit;
    public bool RestartOnUnexpectedExit { get => _restartOnUnexpectedExit; set => Set(ref _restartOnUnexpectedExit, value); }
    public string[] RefreshUnits { get; } = ["秒", "分", "時", "天"];
    public string RefreshValue { get => _refreshValue; set => Set(ref _refreshValue, value); }
    public string RefreshUnit { get => _refreshUnit; set => Set(ref _refreshUnit, value); }
    private string _hint = "選擇自己的 BAT，或貼上 BAT 內容。";
    public string[] BatchModes { get; } = ["選擇現有 BAT", "貼上 BAT 內容"];
    public string BatchInputMode { get => _mode; set { if (Set(ref _mode, value)) { Changed(nameof(IsPastedBatch)); Changed(nameof(IsExistingBatch)); } } }
    public bool IsPastedBatch => BatchInputMode == BatchModes[1];
    public bool IsExistingBatch => !IsPastedBatch;
    public string BatchFile { get => _batchFile; set => Set(ref _batchFile, value); }
    public string BatchContent { get => _batchContent; set => Set(ref _batchContent, value); }
    public string BatchDirectory { get => _directory; set => Set(ref _directory, value); }
    public string BatchHint { get => _hint; set => Set(ref _hint, value); }
    public string RigName { get => _rigName; set => Set(ref _rigName, value); }
    public string ApiPort { get => _apiPort; set => Set(ref _apiPort, value); }
    public void Load(MinerSettings settings)
    {
        BatchInputMode = BatchModes[settings.UsePastedBatch ? 1 : 0]; BatchFile = settings.BatchFile;
        BatchContent = settings.BatchContent; BatchDirectory = settings.BatchDirectory;
        if (BatchDirectory.Length == 0 && BatchFile.Length > 0) BatchDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(BatchFile)) ?? "";
        RigName = settings.RigName; ApiPort = settings.ApiPort.ToString();
        RefreshValue = settings.MetricRefresh.Value.ToString(); RefreshUnit = RefreshUnits[(int)settings.MetricRefresh.Unit];
        RestartOnUnexpectedExit = settings.RestartOnUnexpectedExit;
    }
    public MinerSettings ToSettings(RestartOptions restart)
    {
        if (!int.TryParse(ApiPort, out var port) || port is < 1 or > 65535) throw new ArgumentException("API 連接埠必須介於 1–65535。");
        if (!int.TryParse(RefreshValue, out var intervalValue)) throw new ArgumentException("更新間隔必須是正整數。");
        var index = Array.IndexOf(RefreshUnits, RefreshUnit);
        var refresh = new MetricRefreshOptions(intervalValue, (SrbMinerMonitor.Core.RefreshUnit)index);
        if (refresh.Validate() is string error) throw new ArgumentException(error);
        return new() { RestartOnUnexpectedExit = RestartOnUnexpectedExit, MetricRefresh = refresh, BatchFile = BatchFile.Trim(), UsePastedBatch = IsPastedBatch, BatchContent = BatchContent,
            BatchDirectory = BatchDirectory.Trim(), RigName = RigName.Trim(), ApiPort = port, Restart = restart };
    }
}
