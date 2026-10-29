namespace SrbMinerMonitor.Core;

public enum SchedulePhase { Disabled, Running, Stopping, Waiting, Starting, Error, Recovering }

public sealed class RestartScheduler(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private long _started;
    private TimeSpan _duration;
    public RestartOptions Options { get; private set; } = new();
    public SchedulePhase Phase { get; private set; }
    public string? Error { get; private set; }
    public bool Armed => Phase is not (SchedulePhase.Disabled or SchedulePhase.Error);
    public TimeSpan Remaining => Phase is SchedulePhase.Running or SchedulePhase.Waiting or SchedulePhase.Recovering
        ? TimeSpan.FromTicks(Math.Max(0, (_duration - _time.GetElapsedTime(_started)).Ticks)) : TimeSpan.Zero;
    public bool Due => Phase is SchedulePhase.Running or SchedulePhase.Waiting or SchedulePhase.Recovering && Remaining <= TimeSpan.Zero;

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
    public void BeginRecovery() { Error = null; Begin(SchedulePhase.Recovering, TimeSpan.FromSeconds(10)); }
    public void BeginStart() => Phase = SchedulePhase.Starting;
    public void ConfirmStarted(bool automatic)
    {
        if (!Options.Enabled) { Disable(); return; }
        if (automatic && !Options.Repeat) { Disable(); return; }
        Begin(SchedulePhase.Running, Options.RunDuration);
    }
    public void Disable() { Phase = SchedulePhase.Disabled; Options = Options with { Enabled = false }; Error = null; }
    public void Fail(string message) { Phase = SchedulePhase.Error; Error = message; }
    private void Begin(SchedulePhase phase, TimeSpan duration) { Phase = phase; _duration = duration; _started = _time.GetTimestamp(); }
}
