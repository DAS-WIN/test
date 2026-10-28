using System.Collections.ObjectModel;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SrbMinerMonitor.App.Services;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly SettingsStore _store;
    private readonly MinerProcess _process = new();
    private readonly ApiReader _api = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HashSet<string> _secrets = [];
    private MinerController _controller;
    private DemoMiner? _demoMiner;
    private MinerSettings _saved = new();
    private MinerSettings? _activeSettings;
    private RestartOptions _restart = new();
    private MinerSnapshot? _snapshot;
    private readonly MinerOutput _minerOutput = new();
    private readonly Queue<string> _outputLines = new();
    private Guid _outputSession;
    private string? _batchPool;
    private bool _demo, _busy, _scheduleOpen, _apiHealthy;
    private string _page = "Overview", _notice = "請先設定礦工，或開啟示範模式預覽介面。", _eventFilter = "全部", _metric = "算力", _range = "15 分鐘";
    private GpuRow? _selectedGpu;
    private Guid _lastSession;
    private Guid _lastControllerSession;
    private bool _settingsLoaded = true;
    private DateTimeOffset _nextPoll;
    private bool _readingAlgorithms, _detectingDevices, _probeVisible;
    private string _probeTitle = "讀取結果", _probeStatus = "尚未執行", _probeDetails = "", _apiStatus = "尚未連線 API";
    public SettingsViewModel Settings { get; } = new();
    public MetricHistory History { get; } = new();
    public ObservableCollection<GpuRow> Gpus { get; } = [];
    public ObservableCollection<MonitorEvent> Events { get; } = [];
    public IEnumerable<MonitorEvent> FilteredEvents => Events.Where(e => EventFilter == "全部" || e.Category == EventFilter).Take(500);
    public IEnumerable<MonitorEvent> RecentEvents => Events.Take(4);
    public string[] EventFilters { get; } = ["全部", "分享", "連線", "礦工", "排程", "錯誤"];
    public string[] Metrics { get; } = ["算力", "功耗", "溫度"];
    public string[] Ranges { get; } = ["15 分鐘", "1 小時", "6 小時"];
    public ICommand OverviewCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand EventsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand BrowseBatchCommand { get; }
    public ICommand CreateMonitoringBatchCommand { get; }
    public ICommand CopyApiArgumentsCommand { get; }
    public ICommand CopyProbeCommand { get; }
    public ICommand SwitchLiveCommand { get; }
    public ICommand ReadAlgorithmsCommand { get; }
    public ICommand DetectDevicesCommand { get; }
    public ICommand TestPoolCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveRestartCommand { get; }
    public ICommand CancelSettingsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ScheduleCommand { get; }
    public ICommand ApplyScheduleCommand { get; }
    public ICommand CancelScheduleCommand { get; }
    public ICommand AllGpusCommand { get; }
    public ICommand ToggleDemoCommand { get; }
    public ICommand ExitCommand { get; }
    public event Action? ExitRequested;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Refresh(); } }
    public bool IsDemo => _demo;
    public bool IsLive => !_demo;
    public bool ReadingAlgorithms { get => _readingAlgorithms; private set { Set(ref _readingAlgorithms, value); Changed(nameof(AlgorithmButtonText)); Changed(nameof(ProbeBusy)); } }
    public bool DetectingDevices { get => _detectingDevices; private set { Set(ref _detectingDevices, value); Changed(nameof(DeviceButtonText)); Changed(nameof(ProbeBusy)); } }
    public bool ProbeBusy => ReadingAlgorithms || DetectingDevices;
    public string AlgorithmButtonText => ReadingAlgorithms ? "讀取中…" : "讀取支援演算法";
    public string DeviceButtonText => DetectingDevices ? "偵測中…" : "偵測顯卡";
    public bool ProbeVisible { get => _probeVisible; private set => Set(ref _probeVisible, value); }
    public string ProbeTitle { get => _probeTitle; private set => Set(ref _probeTitle, value); }
    public string ProbeStatus { get => _probeStatus; private set => Set(ref _probeStatus, value); }
    public string ProbeDetails { get => _probeDetails; private set => Set(ref _probeDetails, value); }
    public string ApiStatus { get => _apiStatus; private set => Set(ref _apiStatus, value); }
    public string ApiEndpoint => $"http://127.0.0.1:{MonitoringSettings.ApiPort}";
    public string StartRequirement { get { if (_demo) return "目前是示範模式，請先切換至實際監控。"; try { return Settings.ToSettings(_restart).Validate() ?? "已準備好，可儲存並啟動。"; } catch (ArgumentException e) { return e.Message; } } }
    public string SaveStartText => _controller.IsRunning ? "儲存並重啟" : Settings.UseBatchFile ? "儲存並啟動 BAT" : "儲存並啟動";
    public string SettingsFooterHint => _demo ? "示範模式不啟動礦工；請先切換至實際監控。唯讀偵測仍可執行。" : Settings.UseBatchFile ? "指定 BAT 模式：使用 BAT 的挖礦設定啟動。" : "儲存草稿不影響運行中的礦工。";
    public bool IsOverview => _page == "Overview";
    public bool IsSettings => _page == "Settings";
    public bool IsEvents => _page == "Events";
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public string ModeLabel => _demo ? "示範模式 · 模擬資料" : "實際監控";
    public string ModeAction => _demo ? "切換至實際監控" : "開啟示範模式";
    public string Status => _controller.IsTransitioning ? "處理中" : _controller.IsRunning ? (_demo ? "模擬運行中" : MonitoringSettings.UseBatchFile ? (_apiHealthy ? "BAT 運行中 · API 已連線" : "BAT 運行中 · 等待 API") : "礦工運行中") :
        !_demo && _apiHealthy ? "外部礦工 · 僅監控" : "礦工已停止";
    private MinerSettings MonitoringSettings => _activeSettings ?? _saved;
    public string RigTitle => (_demo ? "本機礦機" : MonitoringSettings.RigName) + " / " + (_snapshot?.Algorithm ?? (string.IsNullOrEmpty(MonitoringSettings.Algorithm) ? "尚未設定演算法" : MonitoringSettings.Algorithm));
    public string Version => "SRBMiner " + (_snapshot?.Version ?? "—") + "  ·  單機 / 單一演算法";
    public string Uptime => Display.Uptime(_snapshot?.UptimeSeconds);
    public string LastUpdate => _snapshot?.Timestamp.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public string Freshness => _snapshot is null ? "等待資料" : DateTimeOffset.Now - _snapshot.Timestamp > TimeSpan.FromSeconds(15)
        ? "資料過期" : _apiHealthy ? "每 5 秒更新" : "更新失敗 · 保留最後讀值";
    public string Hashrate => Display.Hash(_snapshot?.TotalHashrate);
    public string Power => Display.Number(_snapshot?.TotalPower, "W");
    public string Efficiency => Display.Hash(_snapshot?.Efficiency, true, _snapshot?.TotalHashrate);
    public string Temperature => Display.Number(_snapshot?.MaxTemperature, "°C", "0");
    private ShareStats TotalShares => MinerOutput.Prefer(CurrentSnapshot?.Shares, _minerOutput.Total);
    private MinerSnapshot? CurrentSnapshot => !_demo && _controller.IsRunning && _lastControllerSession != _controller.Session ? null : _snapshot;
    private ShareStats Shares => SelectedGpu is null ? TotalShares :
        MinerOutput.Prefer(CurrentSnapshot?.Gpus.FirstOrDefault(g => g.Id == SelectedGpu.Metrics.Id)?.Shares ?? (Gpus.Count == 1 ? CurrentSnapshot?.Shares : null),
            _minerOutput.Gpus.GetValueOrDefault(SelectedGpu.Metrics.Id) ?? (Gpus.Count == 1 ? TotalShares : null));
    public string TotalAccepted => Display.Count(TotalShares.Accepted);
    public string MinerOutputText => string.Join(Environment.NewLine, _outputLines);
    public IEnumerable<MonitorEvent> RecentShares => Events.Where(e => e.Category == "分享" && e.Message.Contains("accepted", StringComparison.OrdinalIgnoreCase)).Take(3);
    public string Accepted => Display.Count(Shares.Accepted);
    public string Rejected => Display.Count(Shares.Rejected);
    public string Stale => Display.Count(Shares.Stale);
    public string Timeout => Display.Count(Shares.Timeout);
    public string HardwareErrors => Display.Count(Shares.HardwareErrors);
    public string Acceptance => Display.Number(Shares.Acceptance, "%", "0.0");
    public string ShareTitle => SelectedGpu is null ? "全部 GPU 分享統計" : $"GPU #{SelectedGpu.Metrics.Id} 分享統計";
    public string Pool => _snapshot?.Pool ?? (_demo ? "pool.example.com:3333" : MonitoringSettings.UseBatchFile ? _batchPool ?? "等待礦池位址" : string.IsNullOrEmpty(MonitoringSettings.PoolHost) ? "尚未設定" : MonitoringSettings.PoolAddress);
    public string PoolSource => _snapshot?.Pool is not null ? "礦工回報" : MonitoringSettings.UseBatchFile ? "目前 BAT 的礦池設定" : "目前挖礦設定";
    public string Worker => _demo ? "rig-01" : MonitoringSettings.UseBatchFile ? "由 BAT 設定" : MonitoringSettings.Worker;
    public string PoolStatus => _snapshot?.PoolConnected switch { true => "已連線", false => "已斷線", _ => "未提供" };
    public string Latency => Display.Number(_snapshot?.Latency, "ms", "0");
    public string LastShare => _snapshot?.LastShare?.ToLocalTime().ToString("HH:mm:ss") ?? "未提供";
    public string GpuCount => $"{Gpus.Count} GPU";
    public string EventFilter { get => _eventFilter; set { Set(ref _eventFilter, value); Changed(nameof(FilteredEvents)); } }
    public string Metric { get => _metric; set { Set(ref _metric, value); Changed(nameof(ChartSamples)); Changed(nameof(ChartUnit)); } }
    public string Range { get => _range; set { Set(ref _range, value); Changed(nameof(ChartSamples)); } }
    public string ChartUnit => Metric == "算力" ? "H/s" : Metric == "功耗" ? "W" : "°C";
    public GpuRow? SelectedGpu { get => _selectedGpu; set { Set(ref _selectedGpu, value); Refresh(); } }
    public IReadOnlyList<ChartSample> ChartSamples => History.Window(DateTimeOffset.Now, Range == "6 小時" ? TimeSpan.FromHours(6) : Range == "1 小時" ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(15))
        .Select(s => { var gpu = SelectedGpu is null ? null : s.Gpus.FirstOrDefault(g => g.Id == SelectedGpu.Metrics.Id);
            var value = Metric switch { "功耗" => SelectedGpu is null ? s.TotalPower : gpu?.Power, "溫度" => SelectedGpu is null ? s.MaxTemperature : gpu?.Temperature, _ => SelectedGpu is null ? s.TotalHashrate : gpu?.Hashrate };
            return new ChartSample(s.Timestamp, value, s.Session); }).ToArray();
    public string Average1m => Average(TimeSpan.FromMinutes(1));
    public string Average15m => Average(TimeSpan.FromMinutes(15));
    public string Average1h => Average(TimeSpan.FromHours(1));
    public string Average6h => Average(TimeSpan.FromHours(6));
    private string Average(TimeSpan span)
    {
        var (value, coverage) = History.Average(DateTimeOffset.Now, span, SelectedGpu?.Metrics.Id);
        return Display.Hash(value) + (value is null ? "" : $"  · {coverage.TotalMinutes:0.0} 分鐘樣本");
    }
    public bool ScheduleOpen { get => _scheduleOpen; set => Set(ref _scheduleOpen, value); }
    public bool ScheduleEnabled { get; set; }
    public string RunValue { get; set; } = "6";
    public string RunUnit { get; set; } = "小時";
    public string[] RunUnits { get; } = ["小時", "分鐘"];
    public string WaitMinutes { get; set; } = "5";
    public bool Repeat { get; set; } = true;
    public string ScheduleSummary => _controller.Scheduler.Phase switch
    {
        SchedulePhase.Running => "運行中 · 距離停止 " + Countdown,
        SchedulePhase.Stopping => "停止中 · 等待礦工完全退出",
        SchedulePhase.Waiting => "等待重啟 · 剩餘 " + Countdown,
        SchedulePhase.Starting => "啟動中 · 等待新程序",
        SchedulePhase.Error => "排程錯誤 · " + _controller.Scheduler.Error,
        _ => _restart.Enabled ? "排程設定已儲存 · 尚未啟用" : "自動重啟未啟用"
    };
    public string ScheduleDetail => $"運行 {_restart.RunValue} {(_restart.Hours ? "小時" : "分鐘")} → 停止 {_restart.WaitMinutes} 分鐘 → 重新啟動";
    private string Countdown => $"{(int)_controller.Scheduler.Remaining.TotalHours:00}:{_controller.Scheduler.Remaining.Minutes:00}:{_controller.Scheduler.Remaining.Seconds:00}";

    public MainViewModel(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        _controller = CreateController(_process);
        Settings.PropertyChanged += (_, _) => { Changed(nameof(SettingsFooterHint)); Changed(nameof(SaveStartText)); Changed(nameof(StartRequirement)); CommandManager.InvalidateRequerySuggested(); };
        _process.Log += line =>
        {
            var session = _process.OutputSession;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (session != _process.OutputSession) return;
                if (_outputSession != session) { _outputSession = session; _minerOutput.Reset(); }
                var clean = MinerOutput.Clean(line);
                _minerOutput.Read(clean);
                _outputLines.Enqueue(Redact(clean));
                while (_outputLines.Count > 500) _outputLines.Dequeue();
                AddEvent(Classify(clean), clean);
                Changed(nameof(MinerOutputText)); Changed(nameof(RecentShares)); Refresh();
            });
        };
        AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(() => Busy(action), ShowError, () => !IsBusy && (can?.Invoke() ?? true));
        OverviewCommand = new RelayCommand(() => Navigate("Overview"));
        SettingsCommand = new RelayCommand(() => Navigate("Settings"));
        EventsCommand = new RelayCommand(() => Navigate("Events"));
        StartCommand = Command(Start, () => !_controller.IsRunning && !_controller.IsTransitioning && (_demo || !_apiHealthy));
        StopCommand = Command(Stop, () => _controller.IsRunning || _controller.Scheduler.Armed);
        RestartCommand = Command(RestartSaved, () => _controller.IsRunning);
        BrowseCommand = new RelayCommand(Browse, () => !IsBusy);
        BrowseBatchCommand = Command(BrowseBatch);
        CreateMonitoringBatchCommand = Command(CreateMonitoringBatch);
        CopyApiArgumentsCommand = Command(() => { if (!int.TryParse(Settings.ApiPort, out var port) || port is < 1 or > 65535) throw new ArgumentException("請填入有效 API 連接埠。"); Clipboard.SetText($"--api-enable --api-port {port}"); Notice = "已複製 API 參數，請加入 BAT 的 SRBMiner 指令。"; return Task.CompletedTask; });
        CopyProbeCommand = Command(() => { Clipboard.SetText(ProbeDetails); return Task.CompletedTask; }, () => ProbeDetails.Length > 0);
        SwitchLiveCommand = Command(async () => { if (_demo) await ToggleDemo(); }, () => _demo);
        ReadAlgorithmsCommand = Command(ReadAlgorithms);
        DetectDevicesCommand = Command(DetectDevices);
        TestPoolCommand = Command(TestPool);
        SaveCommand = Command(() => Save(false));
        SaveRestartCommand = Command(() => Save(true), () => !_demo && SettingsValid());
        CancelSettingsCommand = new RelayCommand(() => { Settings.Load(_saved); Notice = "已恢復最近儲存的設定。"; }, () => !IsBusy);
        ExportCommand = Command(ExportEvents);
        ScheduleCommand = new RelayCommand(() => { LoadScheduleFields(); ScheduleOpen = !ScheduleOpen; });
        CancelScheduleCommand = new RelayCommand(() => ScheduleOpen = false);
        ApplyScheduleCommand = Command(ApplySchedule);
        AllGpusCommand = new RelayCommand(() => SelectedGpu = null);
        ToggleDemoCommand = Command(ToggleDemo, () => !_controller.IsRunning || _demo);
        ExitCommand = Command(async () => { await Stop(); _lifetime.Cancel(); ExitRequested?.Invoke(); });
    }

    public async Task InitializeAsync(bool demo)
    {
        try { _saved = await _store.LoadAsync(); _restart = _saved.Restart; Settings.Load(_saved); RememberSecret(_saved.Password); }
        catch (Exception e) { _settingsLoaded = false; ShowError(new InvalidOperationException("設定讀取失敗，原檔保留；請確認後重新儲存。" + e.Message)); }
        LoadScheduleFields();
        if (demo) await ToggleDemo();
        _ = MonitorLoop();
        if (Settings.UseBatchFile && File.Exists(Settings.BatchFile)) await InspectBatch();
    }
    private MinerController CreateController(IMinerProcess process)
    {
        var controller = new MinerController(process);
        controller.Event += text => { AddEvent(text.Contains("排程") || text.Contains("重啟") ? "排程" : "礦工", text); Refresh(); };
        return controller;
    }
    private async Task Busy(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }
    public void ShowError(Exception e) { Notice = Redact(e.Message); AddEvent("錯誤", e.Message); }
    private void Navigate(string page) { _page = page; Changed(nameof(IsOverview)); Changed(nameof(IsSettings)); Changed(nameof(IsEvents)); }
    private void RememberSecret(string secret) { if (secret.Length > 0) _secrets.Add(secret); }
    private string Redact(string text)
    {
        foreach (var secret in _secrets.OrderByDescending(s => s.Length)) text = text.Replace(secret, "[已遮蔽]", StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(text, @"(?i)(password|passwd|--password|--pass)\s*[:= ]\s*\S+", "$1 [已遮蔽]");
    }
    private void AddEvent(string category, string text)
    {
        Events.Insert(0, new(DateTimeOffset.Now, category, Redact(text)));
        while (Events.Count > 2000) Events.RemoveAt(Events.Count - 1);
        Changed(nameof(FilteredEvents)); Changed(nameof(RecentEvents));
    }
    private static string Classify(string line) => line.Contains("error", StringComparison.OrdinalIgnoreCase) ? "錯誤" :
        line.Contains("accepted", StringComparison.OrdinalIgnoreCase) || line.Contains("rejected", StringComparison.OrdinalIgnoreCase) ? "分享" :
        line.Contains("connect", StringComparison.OrdinalIgnoreCase) ? "連線" : "礦工";

    private async Task Start()
    {
        if (!_demo)
        {
            if (_saved.Validate() is string error) { Navigate("Settings"); throw new ArgumentException(error); }
            await EnsureApiPortFree(_saved.ApiPort);
            RememberSecret(_saved.Password);
            await ReadBatchPool(_saved);
        }
        Notice = _saved.UseBatchFile ? "正在啟動指定 BAT…" : "正在啟動礦工…";
        _apiHealthy = false; ApiStatus = "等待新程序的 API…";
        await _controller.StartAsync(_saved);
        if (!_demo) _activeSettings = _saved;
        _nextPoll = default;
        if (!_demo) { Notice = _saved.UseBatchFile ? "已啟動指定 BAT，等待本機 API。" : "已啟動礦工，等待本機 API。"; await Poll(); }
    }
    private async Task RestartSaved()
    {
        if (!_demo && _saved.Validate() is string error) throw new ArgumentException(error);
        if (!_demo) await ReadBatchPool(_saved);
        _apiHealthy = false; ApiStatus = "正在重啟，等待新程序的 API…";
        await _controller.RestartAsync(_saved);
        if (!_demo) _activeSettings = _saved;
        _nextPoll = default;
        if (!_demo) { Notice = _saved.UseBatchFile ? "已啟動指定 BAT，等待本機 API。" : "已啟動礦工，等待本機 API。"; await Poll(); }
    }
    private async Task Stop()
    {
        await _controller.StopAsync();
        _restart = _restart with { Enabled = false };
        if (!_demo && _settingsLoaded) { _saved = _saved with { Restart = _restart }; await _store.SaveAsync(_saved); }
        _activeSettings = null;
        _apiHealthy = false;
        ApiStatus = "礦工已停止，等待下一次 API 連線。";
        Notice = "礦工已停止，自動重啟已取消。";
    }
    private void Browse()
    {
        var dialog = new OpenFileDialog { Filter = "SRBMiner 執行檔 (*.exe)|*.exe", Title = "選擇官方 SRBMiner-MULTI.exe" };
        if (dialog.ShowDialog() == true) Settings.Executable = dialog.FileName;
    }
    private async Task BrowseBatch()
    {
        var dialog = new OpenFileDialog { Filter = "BAT / CMD 檔案 (*.bat;*.cmd)|*.bat;*.cmd", Title = "選擇已建立的挖礦 BAT" };
        if (dialog.ShowDialog() != true) return;
        Settings.BatchFile = dialog.FileName; Settings.UseBatchFile = true;
        await InspectBatch();
    }
    private async Task InspectBatch()
    {
        var path = Path.GetFullPath(Settings.BatchFile);
        if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException("BAT 超過 1 MB，請確認檔案內容。");
        var info = BatchFileInspector.Inspect(await File.ReadAllTextAsync(path), Path.GetDirectoryName(path)!);
        _batchPool = info.Pool;
        if (info.ApiPort is int port) Settings.ApiPort = port.ToString();
        if (info.Executable is string exe && File.Exists(exe)) Settings.Executable = exe;
        Settings.BatchHint = info.ApiEnabled ? $"已找到 --api-enable；監控連接埠 {Settings.ApiPort}。儲存後即可啟動。" :
            "此 BAT 未明確啟用 API。請建立監控副本，或把複製的 API 參數加入 SRBMiner 指令。";
        Notice = Settings.BatchHint;
    }
    private async Task CreateMonitoringBatch()
    {
        var source = Path.GetFullPath(Settings.BatchFile);
        if (!File.Exists(source)) throw new ArgumentException("請先選擇 BAT 檔案。");
        if (!int.TryParse(Settings.ApiPort, out var port) || port is < 1 or > 65535) throw new ArgumentException("請填入有效 API 連接埠。");
        var bytes = BatchFileInspector.CreateMonitoringCopy(await File.ReadAllBytesAsync(source), port);
        var dialog = new SaveFileDialog { Filter = "BAT 檔案 (*.bat)|*.bat", InitialDirectory = Path.GetDirectoryName(source), FileName = Path.GetFileNameWithoutExtension(source) + ".monitor.bat", Title = "將監控副本另存於原 BAT 的資料夾" };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(Path.GetFullPath(dialog.FileName), source, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("請使用新檔名，保留原 BAT。");
        if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(dialog.FileName)), Path.GetDirectoryName(source), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("請存於原 BAT 的相同資料夾，以保留 %~dp0 與相對路徑的意義。");
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        Settings.BatchFile = dialog.FileName;
        await InspectBatch();
        Notice = "已建立並選用監控副本；原 BAT 保留。切換實際監控後，按「儲存並啟動」運行。";
    }
    private async Task ReadAlgorithms()
    {
        ReadingAlgorithms = true;
        try
        {
            await RunProbe("讀取支援演算法", "--list-algorithms", output =>
            {
                var names = ExecutableProbe.ParseAlgorithms(output);
                var previous = Settings.Algorithm;
                Settings.SetAlgorithms(names, Settings.Executable);
                return $"完成：取得 {names.Length} 個演算法。" + (Settings.Algorithm.Length == 0 && previous.Length > 0 ? "原選項已不支援，請重新選擇。" : "請從清單選擇後儲存。");
            });
        }
        finally { ReadingAlgorithms = false; }
    }
    private async Task DetectDevices()
    {
        DetectingDevices = true;
        try
        {
            await RunProbe("偵測顯卡", "--list-devices", output =>
            {
                var devices = ExecutableProbe.ParseDevices(output);
                var selected = Settings.GpuIds.Split(',').Select(s => s.Trim()).ToHashSet();
                Settings.Devices.Clear();
                foreach (var d in devices) Settings.Devices.Add(new(d.Id, d.Name, selected.Contains(d.Id.ToString()), () => Settings.GpuIds = string.Join(',', Settings.Devices.Where(g => g.Selected).Select(g => g.Id))));
                return $"完成：辨識 {devices.Count} 張 GPU，請勾選要使用的裝置。";
            });
        }
        finally { DetectingDevices = false; }
    }
    private async Task RunProbe(string title, string argument, Func<string, string> apply)
    {
        ProbeVisible = true; ProbeTitle = title; ProbeStatus = "執行中…最多等待 10 秒";
        var source = Settings.Executable;
        ProbeDetails = $"執行檔：{source}\n參數：{argument}\n正在等待 stdout / stderr…";
        Notice = title + "：執行中…";
        try
        {
            var result = await ExecutableProbe.ExecuteAsync(source, argument, _lifetime.Token);
            ProbeDetails = $"執行檔：{source}\n參數：{argument}\n{Redact(result.Details)}";
            result.EnsureSuccess();
            if (!source.Equals(Settings.Executable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("執行檔已變更，請重新執行；原清單保留。");
            ProbeStatus = apply(result.Output);
            Notice = ProbeStatus;
        }
        catch (Exception e)
        {
            ProbeStatus = "失敗：" + Redact(e.Message);
            ProbeDetails += "\n\n" + Redact(e.Message);
            Notice = ProbeStatus; AddEvent("錯誤", title + "：" + e.Message);
        }
    }
    private async Task TestPool()
    {
        if (!int.TryParse(Settings.PoolPort, out var port) || port is < 1 or > 65535 || Uri.CheckHostName(Settings.PoolHost) == UriHostNameType.Unknown)
            throw new ArgumentException("請輸入有效的礦池主機與連接埠。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(Settings.PoolHost, port, timeout.Token);
        if (Settings.Tls)
        {
            using var ssl = new SslStream(client.GetStream());
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = Settings.PoolHost }, timeout.Token);
        }
        Notice = "礦池網路／TLS 連通；此測試不驗證錢包與分享接受結果。";
        AddEvent("連線", Notice);
    }
    private bool SettingsValid() { try { return Settings.ToSettings(_restart).Validate() is null; } catch (ArgumentException) { return false; } }
    private async Task Save(bool restart)
    {
        var settings = Settings.ToSettings(_restart);
        if (restart && settings.Validate() is string error) throw new ArgumentException(error);
        RememberSecret(settings.Password);
        await _store.SaveAsync(settings);
        _saved = settings;
        _settingsLoaded = true;
        Notice = "設定已儲存；運行中的礦工將在重啟後套用。";
        if (restart)
        {
            await ReadBatchPool(settings);
            if (!_controller.IsRunning) await EnsureApiPortFree(settings.ApiPort);
            _apiHealthy = false; ApiStatus = "正在啟動，等待新程序的 API…";
            Notice = settings.UseBatchFile ? "正在停止舊程序並啟動指定 BAT…" : "正在重新啟動礦工…";
            await _controller.RestartAsync(settings);
            _activeSettings = settings;
            _nextPoll = default;
            Notice = settings.UseBatchFile ? "已啟動指定 BAT，等待 API 連線。" : "設定已儲存，礦工已重新啟動。";
            await Poll();
        }
    }
    private async Task ReadBatchPool(MinerSettings settings)
    {
        _batchPool = settings.UseBatchFile ? BatchFileInspector.Inspect(await File.ReadAllTextAsync(settings.BatchFile), Path.GetDirectoryName(Path.GetFullPath(settings.BatchFile))!).Pool : null;
    }
    private static Task EnsureApiPortFree(int port)
    {
        LocalApiPort.EnsureAvailable(port);
        return Task.CompletedTask;
    }
    private async Task ExportEvents()
    {
        var dialog = new SaveFileDialog { Filter = "文字紀錄 (*.txt)|*.txt", FileName = $"SRBMiner-events-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog() != true) return;
        var lines = FilteredEvents.Reverse().Select(e => Redact($"{e.Time:O}\t{e.Category}\t{e.Message}"));
        await File.WriteAllLinesAsync(dialog.FileName, lines);
        Notice = "已匯出目前篩選的事件紀錄，密碼已遮蔽。";
    }
    private void LoadScheduleFields()
    {
        ScheduleEnabled = _restart.Enabled; RunValue = _restart.RunValue.ToString(); RunUnit = _restart.Hours ? "小時" : "分鐘";
        WaitMinutes = _restart.WaitMinutes.ToString(); Repeat = _restart.Repeat;
        foreach (var property in new[] { nameof(ScheduleEnabled), nameof(RunValue), nameof(RunUnit), nameof(WaitMinutes), nameof(Repeat) }) Changed(property);
    }
    private async Task ApplySchedule()
    {
        if (!int.TryParse(RunValue, out var run) || !int.TryParse(WaitMinutes, out var wait)) throw new ArgumentException("運行與等待時間必須為正整數。");
        var options = new RestartOptions(ScheduleEnabled, run, RunUnit == "小時", wait, Repeat);
        if (options.Validate() is string error) throw new ArgumentException(error);
        // Persist before arming so a disk failure cannot leave an unreported active schedule.
        if (options.Enabled && !_controller.IsRunning) throw new InvalidOperationException("請先啟動由 GUI 管理的礦工，再啟用排程。");
        if (!_demo) await _store.SaveAsync(_saved with { Restart = options });
        await _controller.ApplyScheduleAsync(options);
        _restart = options;
        if (!_demo) _saved = _saved with { Restart = options };
        ScheduleOpen = false; Notice = options.Enabled ? "排程已啟用，開始第一次倒數。" : "排程已關閉，運行中的礦工會繼續運行。";
    }
    private async Task ToggleDemo()
    {
        if (!_demo && _controller.IsRunning) throw new InvalidOperationException("請先停止受管理的礦工，再切換模式。");
        if (_demo) await _controller.StopAsync();
        _demo = !_demo;
        _demoMiner = _demo ? new DemoMiner() : null;
        _controller = CreateController(_demoMiner is not null ? _demoMiner : _process);
        _snapshot = null; _activeSettings = null; _lastSession = Guid.Empty; _lastControllerSession = Guid.Empty;
        History.Clear(); Gpus.Clear(); SelectedGpu = null;
        _minerOutput.Reset(); _outputLines.Clear(); _outputSession = Guid.Empty; _apiHealthy = false; _nextPoll = default;
        _restart = _demo ? new() : _saved.Restart;
        Notice = _demo ? "示範模式：所有 GPU、分享與趨勢皆為模擬資料，未連接實際礦工。" : "已切回本機監控，等待 API 資料。";
        AddEvent("礦工", Notice);
        await Poll(); Refresh();
    }
    private async Task MonitorLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                var phase = _controller.Scheduler.Phase;
                var session = _controller.Session;
                try
                {
                    await _controller.TickAsync(_lifetime.Token);
                    if (session != _controller.Session)
                    {
                        _apiHealthy = false; ApiStatus = "自動重啟礦工完成，等待新程序的 API…"; _nextPoll = default;
                        Notice = "礦工已自動重新啟動；監控應用程式持續運行。";
                    }
                    if (phase == SchedulePhase.Waiting && _controller.Scheduler.Phase == SchedulePhase.Disabled)
                    {
                        _restart = _restart with { Enabled = false };
                        if (!_demo) { _saved = _saved with { Restart = _restart }; await _store.SaveAsync(_saved); }
                        AddEvent("排程", "單次自動重啟已完成。礦工繼續運行，排程已關閉。");
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException) { ShowError(e); }
                if (DateTimeOffset.Now >= _nextPoll && !_controller.IsTransitioning) await Poll();
                Refresh();
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task Poll()
    {
        _nextPoll = DateTimeOffset.Now.AddSeconds(5);
        var controller = _controller;
        var demo = _demo;
        try
        {
            MinerSnapshot snapshot;
            if (demo)
            {
                if (_demoMiner is not { IsRunning: true }) { _apiHealthy = false; return; }
                snapshot = _demoMiner.Snapshot(controller.Session);
            }
            else
            {
                var data = await _api.ReadAsync(MonitoringSettings.ApiPort, _lifetime.Token);
                var sensors = await NvidiaSensors.ReadAsync(_lifetime.Token);
                snapshot = NvidiaSensors.Merge(data.Snapshot, data.Buses, sensors) with { Session = controller.Session };
            }
            if (controller != _controller || demo != _demo) return;
            if (!_apiHealthy) AddEvent("連線", demo ? "示範資料更新已啟用。" : "本機 API 已連線。");
            _apiHealthy = true;
            ApiStatus = demo ? "示範資料（沒有連接實際 API）" : $"API 已連線 · {snapshot.Timestamp:HH:mm:ss}";
            if (_snapshot is null || _lastControllerSession != controller.Session) _lastSession = controller.Session;
            else if (_snapshot is { } previous && snapshot.UptimeSeconds < previous.UptimeSeconds)
            {
                _lastSession = Guid.NewGuid();
                AddEvent("礦工", "偵測到外部礦工統計重新開始，建立趨勢分界。");
            }
            _lastControllerSession = controller.Session;
            snapshot = snapshot with { Session = _lastSession };
            _snapshot = snapshot; History.Add(snapshot);
            var selectedId = SelectedGpu?.Metrics.Id;
            Gpus.Clear(); foreach (var g in snapshot.Gpus) Gpus.Add(new(g));
            _selectedGpu = selectedId is int id ? Gpus.FirstOrDefault(g => g.Metrics.Id == id) : null;
            Changed(nameof(SelectedGpu));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e)
        {
            if (controller != _controller) return;
            if (_apiHealthy) AddEvent("連線", "API 更新失敗，保留最後讀值。" + e.Message);
            _apiHealthy = false;
            ApiStatus = demo ? "模擬礦工已停止" : "API 尚未連線：" + Redact(e.Message) + (MonitoringSettings.UseBatchFile ? " 請確認 BAT 含 --api-enable，且 API 連接埠一致。" : " 請設定並啟動礦工。");
        }
        Refresh();
    }
    private void Refresh()
    {
        foreach (var property in new[] { nameof(Status), nameof(RigTitle), nameof(Version), nameof(Uptime), nameof(LastUpdate), nameof(Freshness), nameof(Hashrate), nameof(Power), nameof(Efficiency), nameof(Temperature), nameof(TotalAccepted), nameof(MinerOutputText), nameof(PoolSource), nameof(RecentShares), nameof(Accepted), nameof(Rejected), nameof(Stale), nameof(Timeout), nameof(HardwareErrors), nameof(Acceptance), nameof(ShareTitle), nameof(Pool), nameof(Worker), nameof(PoolStatus), nameof(Latency), nameof(LastShare), nameof(GpuCount), nameof(ChartSamples), nameof(Average1m), nameof(Average15m), nameof(Average1h), nameof(Average6h), nameof(ScheduleSummary), nameof(ScheduleDetail), nameof(IsDemo), nameof(ModeLabel), nameof(ModeAction), nameof(IsLive), nameof(ApiEndpoint), nameof(SettingsFooterHint), nameof(SaveStartText), nameof(StartRequirement) }) Changed(property);
        CommandManager.InvalidateRequerySuggested();
    }
    public void Dispose() { _lifetime.Cancel(); _api.Dispose(); _process.Dispose(); _lifetime.Dispose(); }
}

public sealed record ChartSample(DateTimeOffset Time, double? Value, Guid Session);
public sealed record GpuRow(GpuMetrics Metrics)
{
    public string Id => $"●  #{Metrics.Id}";
    public string Name => Metrics.Name;
    public string Hashrate => Display.Hash(Metrics.Hashrate);
    public string Power => Display.Number(Metrics.Power, "W");
    public string Efficiency => Display.Hash(Metrics.Efficiency, true, Metrics.Hashrate);
    public string Fan => Display.Number(Metrics.Fan, "%", "0");
    public string Temperature => Display.Number(Metrics.Temperature, "°C", "0");
    public string Core => Display.Number(Metrics.CoreClock, "MHz", "0");
    public string Memory => Display.Number(Metrics.MemoryClock, "MHz", "0");
}
