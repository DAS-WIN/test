using System.Collections.ObjectModel;
using System.IO;
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
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly MinerController _controller;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly MinerOutput _minerOutput = new();
    private readonly MinuteMetrics _minuteMetrics = new();
    private readonly Queue<string> _outputLines = new();
    private MinerSettings _saved = new();
    private MinerSettings? _activeSettings;
    private RestartOptions _restart = new();
    private MinerSnapshot? _snapshot;
    private Guid _outputSession, _lastControllerSession, _lastSession;
    private DateTimeOffset _nextPoll;
    private bool _busy, _scheduleOpen, _apiHealthy, _settingsLoaded = true;
    private string _page = "Overview", _notice = "請選擇自己的 BAT，或在挖礦設定貼上 BAT 內容。";
    private string _eventFilter = "全部", _metric = "算力", _range = "15 分鐘", _apiStatus = "尚未連線 API";
    private string? _batchPool;
    private GpuRow? _selectedGpu;
    public SettingsViewModel Settings { get; } = new();
    public MetricHistory History { get; } = new();
    public ObservableCollection<GpuRow> Gpus { get; } = [];
    public ObservableCollection<MonitorEvent> Events { get; } = [];
    public IEnumerable<MonitorEvent> FilteredEvents => Events.Where(e => EventFilter == "全部" || e.Category == EventFilter).Take(500);
    public IEnumerable<MonitorEvent> RecentShares => Events.Where(e => e.Category == "成功").Take(3);
    public string[] EventFilters { get; } = ["全部", "成功", "失敗", "逾時"];
    public string[] Metrics { get; } = ["算力", "功耗", "溫度"];
    public string[] Ranges { get; } = ["15 分鐘", "1 小時", "6 小時"];
    public ICommand OverviewCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand EventsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand BrowseBatchCommand { get; }
    public ICommand BrowseDirectoryCommand { get; }
    public ICommand CreateMonitoringBatchCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand SaveRestartCommand { get; }
    public ICommand CancelSettingsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand ScheduleCommand { get; }
    public ICommand ApplyScheduleCommand { get; }
    public ICommand CancelScheduleCommand { get; }
    public ICommand AllGpusCommand { get; }
    public ICommand ExitCommand { get; }
    public event Action? ExitRequested;
    public bool IsBusy { get => _busy; private set { Set(ref _busy, value); Refresh(); } }
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public string ApiStatus { get => _apiStatus; private set => Set(ref _apiStatus, value); }
    public bool IsOverview => _page == "Overview";
    public bool IsSettings => _page == "Settings";
    public bool IsEvents => _page == "Events";
    private MinerSettings MonitoringSettings => _activeSettings ?? _saved;
    public string ApiEndpoint => $"http://127.0.0.1:{MonitoringSettings.ApiPort}";
    public string StartRequirement { get { try { return Settings.ToSettings(_restart).Validate() ?? "已準備好，可儲存並啟動。"; } catch (ArgumentException e) { return e.Message; } } }
    public string SaveStartText => _controller.IsRunning ? "儲存並重啟礦工" : "儲存並啟動 BAT";
    public string SettingsFooterHint => "更新間隔與異常重啟選項儲存後立即套用；BAT 變更在啟動或重啟後套用。";
    public string Status => _controller.IsTransitioning ? "礦工處理中" : _controller.IsRunning ? (_apiHealthy ? "BAT 運行中 · API 已連線" : "BAT 運行中 · 等待 API") : _apiHealthy ? "外部礦工 · 僅監控" : "礦工已停止";
    public string RigTitle => MonitoringSettings.RigName + " / " + (_snapshot?.Algorithm ?? "等待演算法資訊");
    public string AppVersion => $"v{typeof(MainViewModel).Assembly.GetName().Version?.ToString(3)} · SRBMiner GUI";
    public string Version => "SRBMiner " + (_snapshot?.Version ?? "—") + "  ·  BAT 監控";
    public string Uptime => Display.Uptime(_snapshot?.UptimeSeconds);
    public string LastUpdate => _snapshot?.Timestamp.ToLocalTime().ToString("HH:mm:ss") ?? "—";
    public string HashrateUpdate => _minuteMetrics.Snapshot?.Timestamp.ToLocalTime().ToString("MM-dd HH:mm:ss") ?? "—";
    public string MetricRefreshSummary => $"硬體資訊每 {_saved.MetricRefresh.Label} 更新";
    public double ChartMaxGapSeconds => _saved.MetricRefresh.Duration.TotalSeconds + 15;
    public string Freshness => _snapshot is null ? "等待資料 · Accepted 即時更新" : DateTimeOffset.Now - _snapshot.Timestamp > TimeSpan.FromSeconds(15) ? "API 資料過期 · 分享訊息持續接收" : MetricRefreshSummary + " · 分享即時更新";
    public string Hashrate => Display.Hash(_minuteMetrics.Snapshot?.TotalHashrate);
    public string Power => Display.Number(_minuteMetrics.Snapshot?.TotalPower, "W");
    public string Efficiency => Display.Hash(_minuteMetrics.Snapshot?.Efficiency, true, _minuteMetrics.Snapshot?.TotalHashrate);
    public string Temperature => Display.Number(_minuteMetrics.Snapshot?.MaxTemperature, "°C", "0");
    private MinerSnapshot? CurrentSnapshot => _controller.IsRunning && _lastControllerSession != _controller.Session ? null : _snapshot;
    private ShareStats TotalShares => MinerOutput.Max(CurrentSnapshot?.Shares, _minerOutput.Total);
    private ShareStats Shares => SelectedGpu is null ? TotalShares : MinerOutput.Max(
        CurrentSnapshot?.Gpus.FirstOrDefault(g => g.Id == SelectedGpu.Metrics.Id)?.Shares ?? (Gpus.Count == 1 ? CurrentSnapshot?.Shares : null),
        _minerOutput.Gpus.GetValueOrDefault(SelectedGpu.Metrics.Id) ?? (Gpus.Count == 1 ? TotalShares : null));
    public string Accepted => Display.Count(Shares.Accepted);
    public string TotalAccepted => Display.Count(TotalShares.Accepted);
    public string Rejected => Display.Count(Shares.Rejected);
    public string Stale => Display.Count(Shares.Stale);
    public string Timeout => Display.Count(Shares.Timeout);
    public string HardwareErrors => Display.Count(Shares.HardwareErrors);
    public string Acceptance => Display.Number(Shares.Acceptance, "%", "0.0");
    public string ShareTitle => SelectedGpu is null ? "全部 GPU 分享統計" : $"GPU #{SelectedGpu.Metrics.Id} 分享統計";
    public string Pool => _snapshot?.Pool ?? _batchPool ?? "等待礦池位址";
    public string PoolSource => _snapshot?.Pool is not null ? "礦工回報" : "目前 BAT 的礦池設定";
    public string GpuCount => $"{Gpus.Count} GPU";
    public string MinerOutputText => string.Join(Environment.NewLine, _outputLines);
    public string EventFilter { get => _eventFilter; set { Set(ref _eventFilter, value); Changed(nameof(FilteredEvents)); } }
    public string Metric { get => _metric; set { Set(ref _metric, value); Changed(nameof(ChartSamples)); Changed(nameof(ChartUnit)); } }
    public string Range { get => _range; set { Set(ref _range, value); Changed(nameof(ChartSamples)); } }
    public string ChartUnit => Metric == "算力" ? "H/s" : Metric == "功耗" ? "W" : "°C";
    public GpuRow? SelectedGpu { get => _selectedGpu; set { Set(ref _selectedGpu, value); Refresh(); } }
    public IReadOnlyList<ChartSample> ChartSamples => History.Window(_minuteMetrics.Snapshot?.Timestamp ?? DateTimeOffset.Now, Range == "6 小時" ? TimeSpan.FromHours(6) : Range == "1 小時" ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(15))
        .Select(s => { var gpu = SelectedGpu is null ? null : s.Gpus.FirstOrDefault(g => g.Id == SelectedGpu.Metrics.Id);
            var value = Metric switch { "功耗" => SelectedGpu is null ? s.TotalPower : gpu?.Power, "溫度" => SelectedGpu is null ? s.MaxTemperature : gpu?.Temperature, _ => SelectedGpu is null ? s.TotalHashrate : gpu?.Hashrate };
            return new ChartSample(s.Timestamp, value, s.Session); }).ToArray();
    public string Average1m => Average(TimeSpan.FromMinutes(1));
    public string Average15m => Average(TimeSpan.FromMinutes(15));
    public string Average1h => Average(TimeSpan.FromHours(1));
    public string Average6h => Average(TimeSpan.FromHours(6));
    private string Average(TimeSpan span)
    {
        var (value, coverage) = History.Average(_minuteMetrics.Snapshot?.Timestamp ?? DateTimeOffset.Now, span, SelectedGpu?.Metrics.Id);
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
        SchedulePhase.Recovering => "異常退出 · 等待重啟 " + Countdown,
        SchedulePhase.Starting => "啟動中 · 等待新程序",
        SchedulePhase.Error => "排程錯誤 · " + _controller.Scheduler.Error,
        _ => _restart.Enabled ? "排程設定已儲存 · 尚未啟用" : _saved.RestartOnUnexpectedExit ? "異常退出自動重啟已啟用 · 定時排程未啟用" : "自動重啟未啟用"
    };
    public string ScheduleDetail => _restart.Enabled
        ? $"運行 {_restart.RunValue} {(_restart.Hours ? "小時" : "分鐘")} → 停止 {_restart.WaitMinutes} 分鐘 → 重新啟動"
        : _saved.RestartOnUnexpectedExit ? "非預期退出 → 等待 10 秒 → 重啟礦工" : "定時重啟與異常退出自動重啟皆未啟用";
    private string Countdown => $"{(int)_controller.Scheduler.Remaining.TotalHours:00}:{_controller.Scheduler.Remaining.Minutes:00}:{_controller.Scheduler.Remaining.Seconds:00}";

    public MainViewModel(SettingsStore? store = null)
    {
        _store = store ?? new SettingsStore();
        _controller = new MinerController(_process);
        _controller.Event += text => { Notice = Redact(text); Refresh(); };
        Settings.PropertyChanged += (_, _) => { Changed(nameof(StartRequirement)); CommandManager.InvalidateRequerySuggested(); };
        _process.Log += line =>
        {
            var session = _process.OutputSession;
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (session != _process.OutputSession) return;
                if (_outputSession != session) { _outputSession = session; _minerOutput.Reset(); }
                var clean = MinerOutput.Clean(line);
                _minerOutput.Read(clean);
                _outputLines.Enqueue(Redact(clean)); while (_outputLines.Count > 500) _outputLines.Dequeue();
                if (MinerOutput.Outcome(clean) is string outcome) AddShareEvent(outcome, clean);
                Changed(nameof(MinerOutputText)); Refresh();
            });
        };
        AsyncCommand Command(Func<Task> action, Func<bool>? can = null) => new(() => Busy(action), ShowError, () => !IsBusy && !_controller.IsTransitioning && (can?.Invoke() ?? true));
        OverviewCommand = new RelayCommand(() => Navigate("Overview"));
        SettingsCommand = new RelayCommand(() => Navigate("Settings"));
        EventsCommand = new RelayCommand(() => Navigate("Events"));
        StartCommand = Command(Start, () => !_controller.IsRunning && !_apiHealthy);
        StopCommand = Command(Stop, () => _controller.IsRunning || _controller.Scheduler.Armed);
        RestartCommand = Command(RestartSaved, () => _controller.IsRunning);
        BrowseBatchCommand = Command(BrowseBatch);
        BrowseDirectoryCommand = new RelayCommand(() => { var dialog = new OpenFolderDialog { Title = "選擇 SRBMiner 所在資料夾" }; if (dialog.ShowDialog() == true) Settings.BatchDirectory = dialog.FolderName; }, () => !IsBusy);
        CreateMonitoringBatchCommand = Command(CreateMonitoringBatch, () => Settings.IsExistingBatch && File.Exists(Settings.BatchFile));
        SaveCommand = Command(() => Save(false));
        SaveRestartCommand = Command(() => Save(true), SettingsValid);
        CancelSettingsCommand = new RelayCommand(() => { Settings.Load(_saved); Notice = "已恢復最近儲存的設定。"; }, () => !IsBusy);
        ExportCommand = Command(ExportEvents);
        ScheduleCommand = new RelayCommand(() => { LoadScheduleFields(); ScheduleOpen = !ScheduleOpen; });
        CancelScheduleCommand = new RelayCommand(() => ScheduleOpen = false);
        ApplyScheduleCommand = Command(ApplySchedule);
        AllGpusCommand = new RelayCommand(() => SelectedGpu = null);
        ExitCommand = Command(async () => { await Stop(); _lifetime.Cancel(); ExitRequested?.Invoke(); });
    }
    public async Task InitializeAsync()
    {
        try { _saved = await _store.LoadAsync(); _restart = _saved.Restart; Settings.Load(_saved); }
        catch (Exception e) { _settingsLoaded = false; ShowError(new InvalidOperationException("設定讀取失敗，原檔保留；請重新儲存。" + e.Message)); }
        LoadScheduleFields();
        if (!Settings.IsPastedBatch && File.Exists(Settings.BatchFile)) await InspectBatch();
        _ = MonitorLoop(); Refresh();
    }
    private async Task Busy(Func<Task> action) { IsBusy = true; try { await action(); } finally { IsBusy = false; } }
    public void ShowError(Exception e) { Notice = Redact(e.Message); StartupDiagnostics.Write("Operation failed: " + Redact(e.ToString())); }
    private void Navigate(string page) { _page = page; Changed(nameof(IsOverview)); Changed(nameof(IsSettings)); Changed(nameof(IsEvents)); }
    private static string Redact(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"(?i)(password|passwd|--password|--pass)\s*[:= ]\s*[^\s]+", "$1 [已遮蔽]");
    private void AddShareEvent(string category, string text)
    {
        // Event text is only the miner's actual stdout/stderr; API counts never create messages.
        Events.Insert(0, new(DateTimeOffset.Now, category, Redact(text), Guid.NewGuid()));
        while (Events.Count > 2000) Events.RemoveAt(Events.Count - 1);
        Changed(nameof(FilteredEvents)); Changed(nameof(RecentShares));
    }
    private async Task Start()
    {
        if (_saved.Validate() is string error) { Navigate("Settings"); throw new ArgumentException(error); }
        await ReadBatchPool(_saved); _apiHealthy = false; ApiStatus = "等待新程序的 API…";
        Notice = "正在背景啟動 BAT…";
        await _controller.StartAsync(_saved); _activeSettings = _saved;
        _nextPoll = default; await Poll();
    }
    private async Task RestartSaved()
    {
        if (_saved.Validate() is string error) throw new ArgumentException(error);
        await ReadBatchPool(_saved); _apiHealthy = false; ApiStatus = "正在重啟礦工…";
        await _controller.RestartAsync(_saved); _activeSettings = _saved;
        _nextPoll = default; Notice = "礦工已重新啟動，監控應用程式持續運行。"; await Poll();
    }
    private async Task Stop()
    {
        Notice = "正在停止挖礦程序，監控視窗會保持開啟。";
        await _controller.StopAsync(); _restart = _restart with { Enabled = false };
        if (_settingsLoaded) { _saved = _saved with { Restart = _restart }; await _store.SaveAsync(_saved); }
        _activeSettings = null; _apiHealthy = false; ApiStatus = "礦工已停止。"; Notice = "礦工已停止，自動重啟已取消。";
    }
    private async Task BrowseBatch()
    {
        var dialog = new OpenFileDialog { Filter = "BAT / CMD 檔案 (*.bat;*.cmd)|*.bat;*.cmd", Title = "選擇已建立的挖礦 BAT" };
        if (dialog.ShowDialog() != true) return;
        Settings.BatchFile = dialog.FileName; Settings.BatchInputMode = Settings.BatchModes[0]; Settings.BatchDirectory = Path.GetDirectoryName(dialog.FileName)!;
        await InspectBatch();
    }
    private async Task InspectBatch()
    {
        var path = Path.GetFullPath(Settings.BatchFile);
        if (new FileInfo(path).Length > 1024 * 1024) throw new FormatException("BAT 超過 1 MB，請確認檔案內容。");
        var info = BatchFileInspector.Inspect(await File.ReadAllTextAsync(path), Path.GetDirectoryName(path)!);
        _batchPool = info.Pool;
        if (info.ApiPort is int port) Settings.ApiPort = port.ToString();
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
        Notice = "已建立並選用監控副本；原 BAT 保留。按「儲存並啟動」運行。";
    }
    private bool SettingsValid() { try { return Settings.ToSettings(_restart).Validate() is null; } catch (ArgumentException) { return false; } }
    private async Task Save(bool restart)
    {
        var settings = Settings.ToSettings(_restart);
        if (restart && settings.Validate() is string error) throw new ArgumentException(error);
        // Validate automatic API insertion before stopping a running miner.
        if (restart && settings.UsePastedBatch) _ = PastedBatch.Prepare(settings.BatchContent, settings.ApiPort);
        await _store.SaveAsync(settings); _saved = settings; _settingsLoaded = true;
        await _controller.ApplyRecoveryAsync(settings.RestartOnUnexpectedExit);
        Notice = "監控更新間隔與異常重啟選項已套用；BAT 變更需重啟礦工。";
        if (!restart) { _nextPoll = default; await Poll(); return; }
        await ReadBatchPool(settings); _apiHealthy = false; ApiStatus = "正在啟動 BAT，等待 API…";
        await _controller.RestartAsync(settings); _activeSettings = settings;
        Notice = "BAT 已在背景啟動。"; _nextPoll = default; await Poll();
    }
    private async Task ReadBatchPool(MinerSettings settings)
    {
        var text = settings.UsePastedBatch ? settings.BatchContent : await File.ReadAllTextAsync(settings.BatchFile);
        var directory = settings.UsePastedBatch ? settings.BatchDirectory : Path.GetDirectoryName(Path.GetFullPath(settings.BatchFile))!;
        _batchPool = BatchFileInspector.Inspect(text, directory).Pool;
    }
    private async Task ExportEvents()
    {
        var dialog = new SaveFileDialog { Filter = "文字紀錄 (*.txt)|*.txt", FileName = $"SRBMiner-shares-{DateTime.Now:yyyyMMdd-HHmmss}.txt" };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllLinesAsync(dialog.FileName, FilteredEvents.Reverse().Select(e => Redact($"{e.Time:O}\t{e.Category}\t{e.Message}")));
        Notice = "已匯出分享成功、失敗與逾時紀錄。";
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
        await _store.SaveAsync(_saved with { Restart = options });
        await _controller.ApplyScheduleAsync(options);
        _restart = options;
        _saved = _saved with { Restart = options };
        ScheduleOpen = false; Notice = options.Enabled ? "排程已啟用，開始第一次倒數。" : "排程已關閉，運行中的礦工會繼續運行。";
    }
    private async Task MonitorLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(_lifetime.Token))
            {
                var automaticRestarts = _controller.AutomaticRestarts;
                try
                {
                    await _controller.TickAsync(_lifetime.Token);
                    if (_controller.AutomaticRestarts != automaticRestarts)
                    {
                        _apiHealthy = false; ApiStatus = "自動重啟完成，等待新的 API…"; _nextPoll = default;
                        Notice = "礦工已自動重新啟動；監控視窗持續開啟。";
                        if (_controller.Scheduler.Phase == SchedulePhase.Disabled)
                        {
                            _restart = _restart with { Enabled = false }; _saved = _saved with { Restart = _restart }; await _store.SaveAsync(_saved);
                        }
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
        await _pollGate.WaitAsync(_lifetime.Token);
        try { await PollCore(); }
        finally { _pollGate.Release(); }
    }
    private async Task PollCore()
    {
        _nextPoll = DateTimeOffset.Now.AddSeconds(1);
        var session = _controller.Session;
        try
        {
            var data = await _api.ReadAsync(MonitoringSettings.ApiPort, _lifetime.Token);
            if (_controller.IsTransitioning || session != _controller.Session) return;
            var snapshot = data.Snapshot;
            if (_snapshot is null || _lastControllerSession != session) _lastSession = session;
            else if (snapshot.UptimeSeconds < _snapshot.UptimeSeconds)
            {
                _lastSession = Guid.NewGuid(); _minerOutput.Reset();
            }
            _lastControllerSession = session; snapshot = snapshot with { Session = _lastSession };
            _snapshot = snapshot; _apiHealthy = true; ApiStatus = $"API 已連線 · {snapshot.Timestamp:HH:mm:ss}";
            _minerOutput.Reconcile(snapshot);
            Refresh(); // Publish recent shares before waiting for hardware sensors.
            var refresh = _saved.MetricRefresh;
            if (_minuteMetrics.IsDue(snapshot.Timestamp, snapshot.Session, refresh))
            {
                var sensors = await NvidiaSensors.ReadAsync(_lifetime.Token);
                if (_controller.IsTransitioning || session != _controller.Session || refresh != _saved.MetricRefresh) return;
                var hardware = NvidiaSensors.Merge(snapshot, data.Buses, sensors);
                if (_minuteMetrics.Update(hardware, refresh))
                {
                    History.Add(hardware);
                    var selectedId = SelectedGpu?.Metrics.Id;
                    Gpus.Clear(); foreach (var gpu in hardware.Gpus) Gpus.Add(new(gpu));
                    _selectedGpu = selectedId is int id ? Gpus.FirstOrDefault(g => g.Metrics.Id == id) : null; Changed(nameof(SelectedGpu));
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) { _apiHealthy = false; ApiStatus = "API 尚未連線：" + Redact(e.Message) + "；BAT 的成功分享訊息仍即時接收。"; }
        Refresh();
    }
    private void Refresh()
    {
        foreach (var property in new[] { nameof(Status), nameof(RigTitle), nameof(Version), nameof(Uptime), nameof(LastUpdate), nameof(HashrateUpdate), nameof(MetricRefreshSummary), nameof(ChartMaxGapSeconds), nameof(Freshness), nameof(Hashrate), nameof(Power), nameof(Efficiency), nameof(Temperature), nameof(Accepted), nameof(TotalAccepted), nameof(Rejected), nameof(Stale), nameof(Timeout), nameof(HardwareErrors), nameof(Acceptance), nameof(ShareTitle), nameof(Pool), nameof(PoolSource), nameof(GpuCount), nameof(ChartSamples), nameof(Average1m), nameof(Average15m), nameof(Average1h), nameof(Average6h), nameof(ScheduleSummary), nameof(ScheduleDetail), nameof(ApiEndpoint), nameof(SaveStartText), nameof(StartRequirement) }) Changed(property);
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
