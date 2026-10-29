using System.Text;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class BatchFileTests
{
    // Synthetic BAT fixture covering conditionals, relative paths, and API arguments.
    private const string Script = "@echo off\r\nsetlocal\r\ncd /d \"%~dp0\"\r\ntitle Mining Fixture\r\nif not exist \"SRBMiner-MULTI.exe\" (\r\n  echo ERROR: SRBMiner-MULTI.exe is missing.\r\n  pause\r\n  exit /b 1\r\n)\r\nif not exist \"logs\" mkdir \"logs\"\r\n\"%~dp0SRBMiner-MULTI.exe\" --algorithm-gpu testhash --disable-cpu --gpu-disable-oc --gpu-off-temperature 80 --pool pool.example.test:4444 --tls true --wallet EXAMPLE-WALLET.worker-01 --worker worker-01 --log-file \"logs\\fixture.log\" --log-file-mode 1\r\npause\r\nendlocal\r\n";

    [Fact] public void BatchModeDoesNotRequireFormWalletAlgorithmOrGpu()
    {
        var settings = new MinerSettings { BatchFile = "mining.BAT" };
        Assert.Null(settings.Validate(false));
        Assert.NotNull((settings with { BatchFile = "miner.exe" }).Validate(false));
        Assert.NotNull((settings with { ApiPort = 0 }).Validate(false));
    }
    [Fact] public void InspectRecognizesMissingApiAndDoesNotConfusePoolPortWithApiPort()
    {
        var result = BatchFileInspector.Inspect(Script, Path.GetTempPath());
        Assert.False(result.ApiEnabled); Assert.Null(result.ApiPort);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "SRBMiner-MULTI.exe"), result.Executable);
    }
    [Fact] public void MonitoringCopyPreservesControlFlowAndMiningArguments()
    {
        var bytes = Encoding.UTF8.GetBytes(Script);
        var original = bytes.ToArray();
        var copy = Encoding.UTF8.GetString(BatchFileInspector.CreateMonitoringCopy(bytes, 21550));
        Assert.Equal(original, bytes);
        Assert.Equal(Script.Replace("--log-file-mode 1\r\n", "--log-file-mode 1 --api-enable --api-port 21550\r\n"), copy);
        var info = BatchFileInspector.Inspect(copy, Path.GetTempPath());
        Assert.True(info.ApiEnabled); Assert.Equal(21550, info.ApiPort);
    }
    [Fact] public void ExistingApiFlagIsNotDuplicatedAndPortIsUpdated()
    {
        var bytes = Encoding.UTF8.GetBytes("SRBMiner-MULTI.exe --api-enable --api-port 22000\r\npause\r\n");
        var copy = Encoding.UTF8.GetString(BatchFileInspector.CreateMonitoringCopy(bytes, 21550));
        Assert.Equal("SRBMiner-MULTI.exe --api-enable --api-port 21550\r\npause\r\n", copy);
    }
    [Fact] public void CommentsAndPrintedInstructionsDoNotClaimApiIsEnabled()
    {
        var info = BatchFileInspector.Inspect("rem --api-enable\n:: --api-enable\necho --api-enable\nSRBMiner-MULTI.exe --pool test:4444", Path.GetTempPath());
        Assert.False(info.ApiEnabled);
    }
    [Fact] public void MultiCommandScriptRequiresManualConfiguration()
    {
        Assert.Throws<FormatException>(() => BatchFileInspector.CreateMonitoringCopy(Encoding.UTF8.GetBytes("SRBMiner-MULTI.exe --algorithm x && pause"), 21550));
        Assert.Throws<FormatException>(() => BatchFileInspector.CreateMonitoringCopy(Encoding.UTF8.GetBytes("SRBMiner-MULTI.exe --algorithm x\nSRBMiner-MULTI.exe --algorithm y"), 21550));
    }
    [Fact] public void MonitoringCopyPreservesNonUtf8Bytes()
    {
        var bytes = new byte[] { 0x40, 0x65, 0x63, 0x68, 0x6F, 0x20, 0xA4, 0xA4, 0xA4, 0xE5, 0x0D, 0x0A }
            .Concat(Encoding.ASCII.GetBytes("SRBMiner-MULTI.exe --algorithm x\r\n")).ToArray();
        var copy = BatchFileInspector.CreateMonitoringCopy(bytes, 21550);
        Assert.Equal(bytes[..12], copy[..12]);
    }
}
