using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.Tests;

public sealed class StopSignalTests
{
    [Fact] public void NormalStartupIsNotConsumedByInternalSignalEntryPoint()
    {
        Assert.False(ConsoleStopSignal.TryHandleArguments([], out var code)); Assert.Equal(0, code);
        Assert.False(ConsoleStopSignal.TryHandleArguments(["--verify-startup"], out _));
    }
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-process")]
    public void InvalidOrBroadcastGroupsAreRejectedBeforeSending(string value)
    {
        Assert.True(ConsoleStopSignal.TryHandleArguments(["--internal-miner-stop-signal", value], out var code));
        Assert.Equal(2, code);
    }
    [Fact] public async Task SenderNeverSignalsCallingApplicationOrBroadcastGroup()
    {
        Assert.False(await ConsoleStopSignal.SendAsync([0, (uint)Environment.ProcessId]));
        Assert.False(await ConsoleStopSignal.SendAsync([]));
    }
}
