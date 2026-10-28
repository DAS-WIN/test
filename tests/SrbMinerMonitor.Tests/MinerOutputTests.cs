using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class MinerOutputTests
{
    [Fact] public void GreenAcceptedLinesCountOncePerNewLineAndKeepGpuIds()
    {
        var output = new MinerOutput();
        output.Read("[2026-10-28 21:45:25] \u001b[32mGPU0[t0] share accepted [ 213ms ][pearlhash][0]\u001b[0m");
        output.Read("GPU0[t0] share accepted [ 211ms ][pearlhash][0]");
        output.Read("GPU2[t0] share rejected [ 10ms ][pearlhash][0]");
        output.Read("DEVFEE GPU0[t0] share accepted");
        Assert.Equal(2, output.Total.Accepted); Assert.Equal(1, output.Total.Rejected);
        Assert.Equal(2, output.Gpus[0].Accepted); Assert.Equal(1, output.Gpus[2].Rejected);
        Assert.Null(output.Total.Timeout);
    }
    [Fact] public void StatisticsRowsSetCumulativeCountersInsteadOfAddingAgain()
    {
        var output = new MinerOutput();
        output.Read("GPU0[t0] share accepted [ 213ms ][pearlhash][0]");
        output.Read("[21:45:55] ID Name Hashrate Power Eff Fan Temp CClk MClk A R HW");
        const string row = "[21:45:55] #0 RTX 5080 200.50 TH/s 252.1W 0.795 59% 61C 2640 14802 4 0 0";
        output.Read(row); output.Read(row);
        Assert.Equal(4, output.Total.Accepted); Assert.Equal(0, output.Total.HardwareErrors);
        output.Read("GPU0[t0] share accepted [ 213ms ][pearlhash][0]");
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
        output.Read("[21:45:55] Job received, share acceptance ratio unknown");
        Assert.Null(output.Total.Accepted);
        output.Read("GPU0[t0] share accepted [ 213ms ][pearlhash][0]");
        Assert.Equal(1, MinerOutput.Max(new(), output.Total).Accepted);
    }
    [Theory]
    [InlineData("SRBMiner-MULTI.exe --pool prl.kryptex.network:8048", "prl.kryptex.network:8048")]
    [InlineData("echo --pool wrong:3333\nSRBMiner-MULTI.exe --pool \"right.example:4444\"", "right.example:4444")]
    [InlineData("SRBMiner-MULTI.exe ^\n --pool right.example:4444", "right.example:4444")]
    [InlineData("SRBMiner-MULTI.exe --pool %MY_POOL%", null)]
    [InlineData("SRBMiner-MULTI.exe --pool stratum://user:secret@pool:4444", null)]
    public void PoolComesFromMinerCommandRatherThanEchoOrCredentials(string script, string? expected) =>
        Assert.Equal(expected, BatchFileInspector.Inspect(script, Path.GetTempPath()).Pool);
}
