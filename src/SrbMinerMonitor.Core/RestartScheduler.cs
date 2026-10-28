namespace SrbMinerMonitor.Core;

public enum SchedulePhase { Disabled, Running, Stopping, Waiting, Starting, Error }

public sealed class RestartScheduler(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _started;
    private TimeSpan _duration;
    public RestartOptions Options { get; private set; } = new();
    public SchedulePhase Phase { get; private set; }
    public string? Error { get; private set; }
    public bool Armed => Phase is not (SchedulePhase.Disabled or SchedulePhase.Error);
    public TimeSpan Remaining => Phase is SchedulePhase.Running or SchedulePhase.Waiting
        ? TimeSpan.FromTicks(Math.Max(0, (_duration - _time.GetElapsedTime(_started)).Ticks)) : TimeSpan.Zero;
    public bool Due => Phase is SchedulePhase.Running or SchedulePhase.Waiting && Remaining <= TimeSpan.Zero;

    public void Apply(RestartOptions options, bool ownedRunning)
    {
        if (options.Validate() is string error) throw new ArgumentException(error);
        if (options.Enabled && !ownedRunning) throw new InvalidOperationException("請先啟動由 GUI 管理的礦工，再套用排程。");
        Options = options;
        Error = null;
        if (!options.Enabled) { Disable(); return; }
        Begin(SchedulePhase.Running, options.RunDuration);
    }
    public void BeginStop() => Phase = SchedulePhase.Stopping;
    public void ConfirmStopped() => Begin(SchedulePhase.Waiting, TimeSpan.FromMinutes(Options.WaitMinutes));
    public void BeginStart() => Phase = SchedulePhase.Starting;
    public void ConfirmStarted(bool automatic)
    {
        if (!Options.Enabled) return;
        if (automatic && !Options.Repeat) { Disable(); return; }
        Begin(SchedulePhase.Running, Options.RunDuration);
    }
    public void Disable() { Phase = SchedulePhase.Disabled; Options = Options with { Enabled = false }; Error = null; }
    public void Fail(string message) { Phase = SchedulePhase.Error; Error = message; }
    private void Begin(SchedulePhase phase, TimeSpan duration) { Phase = phase; _duration = duration; _started = _time.GetTimestamp(); }
}

public interface IMinerProcess
{
    bool IsRunning { get; }
    Task StartAsync(MinerSettings settings, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

// All lifecycle transitions share one lock. A pending stop never leaves a timer that can start a second process.
public sealed class MinerController(IMinerProcess process, TimeProvider? time = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MinerSettings? _active;
    private bool _expectedRunning;
    public RestartScheduler Scheduler { get; } = new(time);
    public bool IsRunning => process.IsRunning;
    public bool IsTransitioning { get; private set; }
    public Guid Session { get; private set; } = Guid.NewGuid();
    public long AutomaticRestarts { get; private set; }
    public event Action<string>? Event;

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
            Scheduler.Fail("礦工非預期退出，排程已暫停。");
            Event?.Invoke(Scheduler.Error!);
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
        else if (Scheduler.Phase == SchedulePhase.Waiting)
        {
            Scheduler.BeginStart();
            Event?.Invoke("自動重啟：等待時間到，正在重新啟動礦工。");
            await StartCore(_active ?? throw new InvalidOperationException("沒有已啟動的設定。"), ct);
            Scheduler.ConfirmStarted(true);
            AutomaticRestarts++;
        }
    }, ct);

    private async Task StartCore(MinerSettings settings, CancellationToken ct)
    {
        await process.StartAsync(settings, ct);
        if (!process.IsRunning) throw new InvalidOperationException("礦工啟動後立即退出，請查看事件紀錄。");
        _active = settings;
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
