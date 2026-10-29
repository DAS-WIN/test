namespace SrbMinerMonitor.Core;

// All lifecycle transitions share one lock. A pending stop never leaves a timer that can start a second process.
public sealed class MinerController(IMinerProcess process, TimeProvider? time = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MinerSettings? _active;
    private bool _expectedRunning;
    private bool _recoverUnexpectedExit;
    public RestartScheduler Scheduler { get; } = new(time);
    public bool IsRunning => process.IsRunning;
    public bool IsTransitioning { get; private set; }
    public Guid Session { get; private set; } = Guid.NewGuid();
    public long AutomaticRestarts { get; private set; }
    public event Action<string>? Event;

    // Applies live without replacing the BAT/settings used by the currently owned miner.
    public Task ApplyRecoveryAsync(bool enabled, CancellationToken ct = default) => Serialize(() =>
    {
        _recoverUnexpectedExit = enabled;
        if (_active is not null) _active = _active with { RestartOnUnexpectedExit = enabled };
        if (!enabled && Scheduler.Phase == SchedulePhase.Recovering) Scheduler.Disable();
        return Task.CompletedTask;
    }, ct);

    public Task StartAsync(MinerSettings settings, CancellationToken ct = default) => Serialize(async () =>
    {
        if (process.IsRunning) throw new InvalidOperationException("礦工已在運行，不能啟動第二個程序。");
        await StartCore(settings, ct);
        Scheduler.ConfirmStarted(false);
    }, ct);
    public Task StopAsync(CancellationToken ct = default) => Serialize(async () =>
    {
        Scheduler.Disable();
        _expectedRunning = false;
        await process.StopAsync(ct);
        Event?.Invoke("手動停止完成；自動重啟排程已取消。");
    }, ct);
    public Task RestartAsync(MinerSettings settings, CancellationToken ct = default) => Serialize(async () =>
    {
        _expectedRunning = false;
        await process.StopAsync(ct);
        await StartCore(settings, ct);
        Scheduler.ConfirmStarted(false);
    }, ct);
    public Task ApplyScheduleAsync(RestartOptions options, CancellationToken ct = default) => Serialize(() =>
    {
        Scheduler.Apply(options, process.IsRunning);
        Event?.Invoke(options.Enabled ? "已套用自動重啟排程；從現在開始倒數。" : "自動重啟已關閉。");
        return Task.CompletedTask;
    }, ct);

    public Task TickAsync(CancellationToken ct = default) => Serialize(async () =>
    {
        if (_expectedRunning && !process.IsRunning)
        {
            _expectedRunning = false;
            // Clean up the old BAT and confirm port release before arming any new launch.
            await process.StopAsync(ct);
            if (Scheduler.Armed && Scheduler.Options.Enabled)
            {
                Scheduler.ConfirmStopped();
                Event?.Invoke($"礦工非預期退出；依排程等待 {Scheduler.Options.WaitMinutes} 分鐘後重啟，監控視窗保持開啟。");
            }
            else if (_recoverUnexpectedExit)
            {
                Scheduler.BeginRecovery();
                Event?.Invoke("礦工非預期退出；10 秒後自動重啟，監控視窗保持開啟。");
            }
            else
            {
                Scheduler.Fail("礦工非預期退出；未啟用異常退出自動重啟。");
                Event?.Invoke(Scheduler.Error!);
            }
            return;
        }
        if (!Scheduler.Due) return;
        if (Scheduler.Phase == SchedulePhase.Running)
        {
            Scheduler.BeginStop();
            Event?.Invoke("自動重啟：運行時間到，正在停止礦工；監控視窗保持開啟。");
            _expectedRunning = false;
            await process.StopAsync(ct);
            if (process.IsRunning) throw new InvalidOperationException("舊礦工仍在運行，排程已暫停。");
            Scheduler.ConfirmStopped();
            Event?.Invoke("礦工已完全停止，開始等待自動重啟。");
        }
        else if (Scheduler.Phase is SchedulePhase.Waiting or SchedulePhase.Recovering)
        {
            var scheduled = Scheduler.Phase == SchedulePhase.Waiting;
            Scheduler.BeginStart();
            Event?.Invoke("自動重啟：等待時間到，正在重新啟動礦工。");
            try { await StartCore(_active ?? throw new InvalidOperationException("沒有已啟動的設定。"), ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A failed recovery must not silently freeze the timer or create a second miner.
                _expectedRunning = false;
                await process.StopAsync(ct);
                if (process.IsRunning) throw new InvalidOperationException("舊礦工仍在運行，禁止自動啟動第二個礦工。", e);
                if (scheduled) Scheduler.ConfirmStopped(); else Scheduler.BeginRecovery();
                Event?.Invoke($"自動啟動失敗：{e.Message}；將依倒數再次嘗試。");
                return;
            }
            Scheduler.ConfirmStarted(true);
            AutomaticRestarts++;
        }
    }, ct);

    private async Task StartCore(MinerSettings settings, CancellationToken ct)
    {
        await process.StartAsync(settings, ct);
        if (!process.IsRunning) throw new InvalidOperationException("礦工啟動後立即退出，請查看事件紀錄。");
        _active = settings;
        _recoverUnexpectedExit = settings.RestartOnUnexpectedExit;
        _expectedRunning = true;
        Session = Guid.NewGuid();
        Event?.Invoke("礦工已啟動，建立新的統計階段。");
    }
    private async Task Serialize(Func<Task> action, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        IsTransitioning = true;
        try { await action(); }
        catch (Exception e) { Scheduler.Fail(e.Message); throw; }
        finally { IsTransitioning = false; _gate.Release(); }
    }
}
