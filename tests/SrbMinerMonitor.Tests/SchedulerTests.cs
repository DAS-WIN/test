using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class SchedulerTests
{
    private static readonly MinerSettings Settings = new();
    private static readonly RestartOptions Options = new(true, 1, false, 1);
    [Fact] public void RestoredOptionsDoNotArmNewScheduler()
    { var scheduler = new RestartScheduler(); Assert.False(scheduler.Armed); Assert.Equal(SchedulePhase.Disabled, scheduler.Phase); }
    [Fact] public async Task FirstCountdownStartsAtApplyRatherThanProcessStart()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); clock.Advance(TimeSpan.FromMinutes(5)); await controller.ApplyScheduleAsync(Options);
        Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
    }
    [Fact] public async Task WaitStartsOnlyAfterMinerHasCompletelyExited()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        miner.BeforeStop = () => stopped.Task;
        clock.Advance(TimeSpan.FromMinutes(1)); var tick = controller.TickAsync();
        Assert.Equal(SchedulePhase.Stopping, controller.Scheduler.Phase);
        clock.Advance(TimeSpan.FromMinutes(5)); Assert.Equal(1, miner.Starts);
        stopped.SetResult(); await tick;
        Assert.Equal(SchedulePhase.Waiting, controller.Scheduler.Phase);
        Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
        clock.Advance(TimeSpan.FromSeconds(59)); await controller.TickAsync(); Assert.Equal(1, miner.Starts);
        clock.Advance(TimeSpan.FromSeconds(1)); await controller.TickAsync(); Assert.Equal(2, miner.Starts);
        Assert.Equal(SchedulePhase.Running, controller.Scheduler.Phase);
    }
    [Fact] public async Task ManualStopCancelsPendingAutomaticStart()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options);
        clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync();
        await controller.StopAsync(); clock.Advance(TimeSpan.FromDays(1)); await controller.TickAsync();
        Assert.Equal(1, miner.Starts); Assert.False(controller.Scheduler.Armed); Assert.False(miner.IsRunning);
    }
    [Fact] public async Task ManualRestartResetsScheduleAfterNewProcessStarted()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options); clock.Advance(TimeSpan.FromSeconds(50));
        await controller.RestartAsync(Settings); Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
        Assert.Equal(2, miner.Starts);
    }
    [Fact] public async Task OneShotScheduleRestartsOnceThenDisarms()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options with { Repeat = false });
        clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync(); clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync();
        Assert.True(miner.IsRunning); Assert.False(controller.Scheduler.Armed); Assert.Equal(2, miner.Starts);
    }
    [Fact] public async Task StopFailurePausesScheduleAndNeverStartsSecondMiner()
    {
        var clock = new Clock(); var miner = new FakeMiner { FailStop = true }; var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options);
        clock.Advance(TimeSpan.FromMinutes(1)); await Assert.ThrowsAsync<IOException>(() => controller.TickAsync());
        clock.Advance(TimeSpan.FromMinutes(10)); await controller.TickAsync();
        Assert.Equal(SchedulePhase.Error, controller.Scheduler.Phase); Assert.Equal(1, miner.Starts); Assert.True(miner.IsRunning);
    }
    [Fact] public async Task StartFailureAfterWaitRetriesAfterAnotherFullWait()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options);
        clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync(); miner.FailStart = true;
        clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync();
        Assert.Equal(SchedulePhase.Waiting, controller.Scheduler.Phase); Assert.False(miner.IsRunning);
        Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
        miner.FailStart = false; clock.Advance(TimeSpan.FromSeconds(59)); await controller.TickAsync(); Assert.Equal(1, miner.Starts);
        clock.Advance(TimeSpan.FromSeconds(1)); await controller.TickAsync(); Assert.Equal(2, miner.Starts);
    }
    [Fact] public async Task UnexpectedCrashWithoutEitherOptionDoesNotAutoRestart()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); miner.Crash();
        clock.Advance(TimeSpan.FromMinutes(10)); await controller.TickAsync();
        Assert.Equal(SchedulePhase.Error, controller.Scheduler.Phase); Assert.Equal(1, miner.Starts);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScheduledCrashUsesConfiguredWaitAndRestartsOnlyOnce(bool recoveryEnabled)
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings with { RestartOnUnexpectedExit = recoveryEnabled });
        await controller.ApplyScheduleAsync(Options); clock.Advance(TimeSpan.FromSeconds(20)); miner.Crash();
        await controller.TickAsync();
        Assert.Equal(SchedulePhase.Waiting, controller.Scheduler.Phase);
        Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
        clock.Advance(TimeSpan.FromSeconds(59)); await controller.TickAsync(); Assert.Equal(1, miner.Starts);
        clock.Advance(TimeSpan.FromSeconds(1)); await Task.WhenAll(controller.TickAsync(), controller.TickAsync());
        Assert.Equal(2, miner.Starts); Assert.True(miner.IsRunning); Assert.Equal(1, controller.AutomaticRestarts);
        Assert.Equal(SchedulePhase.Running, controller.Scheduler.Phase); Assert.Equal(TimeSpan.FromMinutes(1), controller.Scheduler.Remaining);
    }
    [Fact] public async Task RecoveryWorksWithoutScheduleAndCanRecoverRepeatedCrashes()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings with { RestartOnUnexpectedExit = true });
        for (var i = 0; i < 2; i++)
        {
            miner.Crash(); await controller.TickAsync(); Assert.Equal(SchedulePhase.Recovering, controller.Scheduler.Phase);
            Assert.False(controller.Scheduler.Options.Enabled);
            clock.Advance(TimeSpan.FromSeconds(9)); await controller.TickAsync(); Assert.False(miner.IsRunning);
            clock.Advance(TimeSpan.FromSeconds(1)); await controller.TickAsync(); Assert.True(miner.IsRunning);
            Assert.Equal(SchedulePhase.Disabled, controller.Scheduler.Phase);
        }
        Assert.Equal(3, miner.Starts); Assert.Equal(2, controller.AutomaticRestarts);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ManualStopOrDisablingRecoveryCancelsPendingRecovery(bool manualStop)
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyRecoveryAsync(true); miner.Crash(); await controller.TickAsync();
        Assert.Equal(SchedulePhase.Recovering, controller.Scheduler.Phase);
        if (manualStop) await controller.StopAsync(); else await controller.ApplyRecoveryAsync(false);
        clock.Advance(TimeSpan.FromDays(1)); await controller.TickAsync();
        Assert.Equal(1, miner.Starts); Assert.False(miner.IsRunning); Assert.False(controller.Scheduler.Armed);
    }
    [Fact] public async Task RecoveryStartFailureRetriesWithoutCompletingOneShotSchedule()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options with { Repeat = false });
        miner.Crash(); await controller.TickAsync(); miner.FailStart = true;
        clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync(); Assert.True(controller.Scheduler.Armed);
        miner.FailStart = false; clock.Advance(TimeSpan.FromMinutes(1)); await controller.TickAsync();
        Assert.Equal(2, miner.Starts); Assert.False(controller.Scheduler.Armed); Assert.Equal(1, controller.AutomaticRestarts);
    }
    [Fact] public async Task StandaloneRecoveryRetriesStartFailureAndManualStartCancelsOldCountdown()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings with { RestartOnUnexpectedExit = true }); miner.Crash(); await controller.TickAsync();
        miner.FailStart = true; clock.Advance(TimeSpan.FromSeconds(10)); await controller.TickAsync();
        Assert.Equal(SchedulePhase.Recovering, controller.Scheduler.Phase); Assert.Equal(TimeSpan.FromSeconds(10), controller.Scheduler.Remaining);
        miner.FailStart = false; await controller.StartAsync(Settings with { RestartOnUnexpectedExit = true });
        clock.Advance(TimeSpan.FromSeconds(20)); await controller.TickAsync();
        Assert.Equal(2, miner.Starts); Assert.True(miner.IsRunning); Assert.False(controller.Scheduler.Armed);
    }
    [Fact] public async Task RecoveryDoesNotArmOnFreshApplicationOrAfterIntentionalStop()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.ApplyRecoveryAsync(true); clock.Advance(TimeSpan.FromDays(1)); await controller.TickAsync(); Assert.Equal(0, miner.Starts);
        await controller.StartAsync(Settings with { RestartOnUnexpectedExit = true }); await controller.StopAsync();
        clock.Advance(TimeSpan.FromDays(1)); await controller.TickAsync(); Assert.Equal(1, miner.Starts);
    }
    [Fact] public async Task DisablingScheduleKeepsMinerRunning()
    {
        var clock = new Clock(); var miner = new FakeMiner(); var controller = new MinerController(miner, clock);
        await controller.StartAsync(Settings); await controller.ApplyScheduleAsync(Options); await controller.ApplyScheduleAsync(Options with { Enabled = false });
        clock.Advance(TimeSpan.FromMinutes(10)); await controller.TickAsync(); Assert.True(miner.IsRunning); Assert.Equal(0, miner.Stops);
    }
    [Fact] public async Task ConcurrentStartsAreSerialized()
    {
        var miner = new FakeMiner(); var controller = new MinerController(miner);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ => { try { await controller.StartAsync(Settings); return true; } catch (InvalidOperationException) { return false; } }));
        Assert.Single(results, r => r); Assert.Equal(1, miner.Starts);
    }
    [Fact] public async Task CannotArmScheduleWithoutOwnedMiner()
    {
        var controller = new MinerController(new FakeMiner());
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.ApplyScheduleAsync(Options));
    }
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(_ticks);
        public void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }
    private sealed class FakeMiner : IMinerProcess
    {
        public bool IsRunning { get; private set; }
        public int Starts { get; private set; }
        public int Stops { get; private set; }
        public bool FailStart { get; set; }
        public bool FailStop { get; set; }
        public Func<Task>? BeforeStop { get; set; }
        public Task StartAsync(MinerSettings settings, CancellationToken ct)
        {
            if (IsRunning) throw new InvalidOperationException("Already running");
            if (FailStart) throw new IOException("Start failed");
            Starts++; IsRunning = true; return Task.CompletedTask;
        }
        public async Task StopAsync(CancellationToken ct)
        {
            if (FailStop) throw new IOException("Stop failed");
            if (BeforeStop is not null) await BeforeStop();
            Stops++; IsRunning = false;
        }
        public void Crash() => IsRunning = false;
    }
}
