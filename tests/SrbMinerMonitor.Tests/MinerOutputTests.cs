using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class MinerOutputTests
{
    [Fact] public void GreenAcceptedLinesCountOncePerNewLineAndKeepGpuIds()
    {
        var output = new MinerOutput();
        output.Read("[2025-01-01 12:00:00] \u001b[32mGPU0[t0] share accepted [ 100ms ][testhash][0]\u001b[0m");
        output.Read("GPU0[t0] share accepted [ 150ms ][testhash][0]");
        output.Read("GPU2[t0] share rejected [ 10ms ][testhash][0]");
        output.Read("DEVFEE GPU0[t0] share accepted");
        Assert.Equal(2, output.Total.Accepted); Assert.Equal(1, output.Total.Rejected);
        Assert.Equal(2, output.Gpus[0].Accepted); Assert.Equal(1, output.Gpus[2].Rejected);
        Assert.Null(output.Total.Timeout);
    }
    [Fact] public void StatisticsRowsSetCumulativeCountersInsteadOfAddingAgain()
    {
        var output = new MinerOutput();
        output.Read("GPU0[t0] share accepted [ 100ms ][testhash][0]");
        output.Read("[12:00:00] ID Name Hashrate Power Eff Fan Temp CClk MClk A R HW");
        const string row = "[12:00:00] #0 Fixture GPU 12.50 MH/s 100.0W 0.125 50% 60C 1500 5000 4 0 0";
        output.Read(row); output.Read(row);
        Assert.Equal(4, output.Total.Accepted); Assert.Equal(0, output.Total.HardwareErrors);
        output.Read("GPU0[t0] share accepted [ 100ms ][testhash][0]");
        Assert.Equal(5, output.Total.Accepted);
        output.Reset(); Assert.Null(output.Total.Accepted);
    }
    [Fact] public void CumulativeCountersMergeWithoutDoubleCounting()
    {
        var output = new ShareStats(4, 1, HardwareErrors: 0);
        var api = new ShareStats(4, 1);
        var combined = MinerOutput.Max(api, output);
        Assert.Equal(4, combined.Accepted); Assert.Equal(80, combined.Acceptance);
        Assert.Equal(0, combined.HardwareErrors); Assert.Null(combined.Stale);
    }
    [Fact] public void MissingApiCountersUseObservedOutputAndUnrelatedLinesDoNotCount()
    {
        var output = new MinerOutput();
        output.Read("[12:00:00] Job received, share acceptance ratio unknown");
        Assert.Null(output.Total.Accepted);
        output.Read("GPU0[t0] share accepted [ 100ms ][testhash][0]");
        Assert.Equal(1, MinerOutput.Max(new(), output.Total).Accepted);
    }
    [Theory]
    [InlineData("SRBMiner-MULTI.exe --pool pool.example.test:4444", "pool.example.test:4444")]
    [InlineData("echo --pool wrong:3333\nSRBMiner-MULTI.exe --pool \"right.example:4444\"", "right.example:4444")]
    [InlineData("SRBMiner-MULTI.exe ^\n --pool right.example:4444", "right.example:4444")]
    [InlineData("SRBMiner-MULTI.exe --pool %MY_POOL%", null)]
    [InlineData("SRBMiner-MULTI.exe --pool stratum://fixture:EXAMPLE-PASSWORD@pool:4444", null)]
    public void PoolComesFromMinerCommandRatherThanEchoOrCredentials(string script, string? expected) =>
        Assert.Equal(expected, BatchFileInspector.Inspect(script, Path.GetTempPath()).Pool);
}
