using System.Text;
using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.Tests;

public sealed class MinuteAndBatchTests
{
    private static MinerSnapshot Snapshot(DateTimeOffset time, double rate, Guid session) => new(time, "test", "testhash", 10,
        [new(0, "GPU", rate)], new(4, 1), rate, Session: session);
    [Fact] public void HashrateHoldsWithinMinuteAndChangesAtNextClockMinute()
    {
        var metrics = new MinuteMetrics(); var session = Guid.NewGuid();
        var time = new DateTimeOffset(2025, 1, 1, 12, 0, 31, TimeSpan.Zero);
        Assert.True(metrics.Update(Snapshot(time, 100, session)));
        Assert.False(metrics.Update(Snapshot(time.AddSeconds(28), 200, session)));
        Assert.Equal(100, metrics.Snapshot!.TotalHashrate);
        Assert.True(metrics.Update(Snapshot(time.AddSeconds(29), 300, session)));
        Assert.Equal(300, metrics.Snapshot!.Gpus[0].Hashrate);
    }
    [Fact] public void NewMinerSessionRefreshesImmediatelyEvenWithinSameMinute()
    {
        var metrics = new MinuteMetrics(); var time = DateTimeOffset.Now;
        metrics.Update(Snapshot(time, 100, Guid.NewGuid()));
        Assert.True(metrics.Update(Snapshot(time, 5, Guid.NewGuid())));
        Assert.Equal(5, metrics.Snapshot!.TotalHashrate);
    }
    [Fact] public void SharesAfterApiBaselineUpdateImmediatelyAndOldApiCannotRollBack()
    {
        var output = new MinerOutput();
        var snapshot = Snapshot(DateTimeOffset.Now, 100, Guid.NewGuid()) with { Shares = new(100, 2) };
        output.Read("ID Name Hashrate Power Eff Fan Temp CClk MClk A R HW");
        output.Read("#0 GPU 1 MH/s 100W 0.1 50% 60C 2000 9000 100 2 0");
        output.Reconcile(snapshot); output.Read("GPU0[t0] share accepted [ 100ms ][testhash][0]");
        Assert.Equal(101, output.Total.Accepted); Assert.Equal(101, output.Gpus[0].Accepted);
        output.Reconcile(snapshot); Assert.Equal(101, output.Total.Accepted);
        output.Reconcile(snapshot with { Shares = new(101, 2) }); Assert.Equal(101, output.Total.Accepted);
        output.Reset(); output.Read("GPU0[t0] share accepted"); Assert.Equal(1, output.Total.Accepted);
    }
    [Fact] public void ApiArrivingBeforeBufferedOutputDoesNotDoubleCountShare()
    {
        var output = new MinerOutput();
        var snapshot = Snapshot(DateTimeOffset.Now, 100, Guid.NewGuid()) with { Shares = new(1, 0) };
        output.Reconcile(snapshot); output.Read("GPU0[t0] share accepted");
        Assert.Equal(1, output.Total.Accepted);
        output.Read("GPU0[t0] share accepted"); Assert.Equal(2, output.Total.Accepted);
        output.Reconcile(snapshot with { Shares = new(2, 0) }); Assert.Equal(2, output.Total.Accepted);
    }
    [Theory]
    [InlineData("GPU0[t0] share accepted [ 100ms ]", "成功")]
    [InlineData("GPU0[t0] share rejected [ 15ms ]", "失敗")]
    [InlineData("GPU0[t0] share failed", "失敗")]
    [InlineData("GPU0[t0] share timeout", "逾時")]
    [InlineData("GPU0[t0] share timed out", "逾時")]
    [InlineData("Job received difficulty 2097152", null)]
    [InlineData("Hashrate 185 TH/s 0 0 0", null)]
    [InlineData("API request timeout", null)]
    [InlineData("DEVFEE GPU0[t0] share accepted", null)]
    public void EventClassifierKeepsOnlyShareOutcomes(string line, string? expected) => Assert.Equal(expected, MinerOutput.Outcome(line));
    [Fact] public void TimeoutCountsOnlyExplicitShareTimeouts()
    {
        var output = new MinerOutput(); output.Read("API request timeout"); Assert.Null(output.Total.Timeout);
        output.Read("GPU0[t0] share timed out"); Assert.Equal(1, output.Total.Timeout);
    }
    [Fact] public void PastedModeRequiresContentAndWorkingFolderButNoFormSettings()
    {
        var settings = new MinerSettings { UsePastedBatch = true };
        Assert.NotNull(settings.Validate(false));
        settings = settings with { BatchContent = "SRBMiner-MULTI.exe --algorithm-gpu testhash" };
        Assert.NotNull(settings.Validate(false));
        Assert.Null((settings with { BatchDirectory = Path.GetTempPath() }).Validate());
    }
    [Fact] public async Task PastedBatchAddsApiPreservesMiningArgumentsAndWritesInWorkingFolder()
    {
        var directory = Path.Combine(Path.GetTempPath(), "srb-bat-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var content = "@echo off\r\ncd /d \"%~dp0\"\r\n\"%~dp0SRBMiner-MULTI.exe\" --algorithm-gpu testhash --pool pool.example.test:4444 --wallet TEST\r\npause\r\n";
        try
        {
            var settings = new MinerSettings { UsePastedBatch = true, BatchContent = content, BatchDirectory = directory };
            var path = await PastedBatch.CreateAsync(settings, CancellationToken.None);
            Assert.Equal(directory, Path.GetDirectoryName(path));
            var text = await File.ReadAllTextAsync(path);
            Assert.StartsWith("@chcp 65001 >nul\r\n@echo off", text);
            Assert.Contains("--pool pool.example.test:4444 --wallet TEST --api-enable --api-port 21550", text);
            Assert.Contains("%~dp0SRBMiner-MULTI.exe", text); Assert.Equal(content, settings.BatchContent);
        }
        finally { Directory.Delete(directory, true); }
    }
}
