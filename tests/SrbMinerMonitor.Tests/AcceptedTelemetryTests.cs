using System.Text.Json;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class AcceptedTelemetryTests
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
    [Fact] public void RollingWindowsUseIncrementsRatherThanAverageCumulativeTotals()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        for (var minute = 1; minute <= 90; minute++) h.Observe(Start.AddMinutes(minute), minute * 10);
        foreach (var minutes in new[] { 10, 30, 60 })
        {
            var w = h.Window(TimeSpan.FromMinutes(minutes));
            Assert.Equal(minutes * 10, w.Count); Assert.Equal(10, w.PerMinute);
            Assert.Equal(TimeSpan.FromMinutes(minutes), w.Coverage);
        }
    }
    [Fact] public void WarmupUsesActualCoverageAndNewSessionClearsPriorCounts()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        Assert.Null(h.Window(TimeSpan.FromMinutes(10)).PerMinute);
        h.Observe(Start.AddMinutes(5), 20);
        Assert.Equal(4, h.Window(TimeSpan.FromMinutes(10)).PerMinute);
        Assert.Equal(TimeSpan.FromMinutes(5), h.Window(TimeSpan.FromHours(1)).Coverage);
        h.Begin(Guid.NewGuid(), Start.AddMinutes(6)); h.Observe(Start.AddMinutes(7), 3);
        Assert.Equal(3, h.Window(TimeSpan.FromMinutes(30)).Count);
        Assert.Equal(3, h.Window(TimeSpan.FromMinutes(30)).PerMinute);
    }
    [Fact] public void DuplicateApiAndBufferedOutputDoNotAddSharesTwice()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        Assert.Equal(5, h.Observe(Start.AddSeconds(30), 5));
        Assert.Equal(0, h.Observe(Start.AddSeconds(40), 5));
        Assert.Equal(0, h.Observe(Start.AddSeconds(50), 4));
        Assert.Equal(1, h.Observe(Start.AddMinutes(1), 6));
        Assert.Equal(6, h.Window(TimeSpan.FromMinutes(10)).Count);
        Assert.Equal(6, h.Window(TimeSpan.FromMinutes(10)).PerMinute);
    }
    [Fact] public void UnknownWindowBoundaryDoesNotAssignOutageJumpToRecentWindow()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        h.Observe(Start.AddMinutes(20), 100);
        Assert.True(h.Window(TimeSpan.FromMinutes(10)).MissingBoundary);
        Assert.Null(h.Window(TimeSpan.FromMinutes(10)).PerMinute);
        Assert.Equal(5, h.Window(TimeSpan.FromMinutes(30)).PerMinute);
    }
    [Fact] public void CursorPositionOutputIsDeliveredWithoutNewlinesAndRepaintIsNotDuplicated()
    {
        const string share = "GPU0[t0] share accepted [100ms] [fixture][0]";
        var rows = new List<string>();
        var text = "\u001b[2;1H" + share + "\u001b[3;1H" + share + "\u001b[4;1H"
            + "\u001b[2;1H" + share + "\u001b[4;1H";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        TerminalOutputReader.Pump(stream, rows.Add);
        Assert.Equal(2, rows.Count(e => MinerOutput.Outcome(e) == "成功"));
    }
    [Fact] public void ScrollingPreservesIdenticalNewSharesAndUtf8Text()
    {
        var rows = new List<string>();
        var text = string.Concat(Enumerable.Repeat("GPU0[t0] share accepted 測試\r\n", 200));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        TerminalOutputReader.Pump(stream, rows.Add);
        Assert.Equal(200, rows.Count); Assert.All(rows, row => Assert.Contains("測試", row));
    }
    [Fact] public void TerminalTitleAndColorSequencesDoNotAlterAcceptedText()
    {
        var line = "\u001b]0;fixture\u0007\u001b[32mGPU0[t0] share accepted [100ms] [fixture][0]\u001b[0m";
        Assert.Equal("GPU0[t0] share accepted [100ms] [fixture][0]", MinerOutput.Clean(line));
        Assert.Equal("成功", MinerOutput.Outcome(line));
    }
    [Fact] public void CountAtStartAndOutOfOrderTimestampDoNotLoseShares()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        h.Observe(Start, 1); h.Observe(Start.AddMinutes(1), 2);
        h.Observe(Start.AddSeconds(30), 3);
        Assert.Equal(3, h.Window(TimeSpan.FromMinutes(10)).Count);
        Assert.Equal(3, h.Window(TimeSpan.FromMinutes(10)).PerMinute);
    }
    [Theory]
    [InlineData("Difficulty 1000.000 [fixture][0]", 1000)]
    [InlineData("[2025-01-01 12:00:00] Difficulty: 2.5e3 [fixture][0]", 2500)]
    [InlineData("Job received [fixture] block height 1 [diff: 2000] [fixture][0]", 2000)]
    [InlineData("Job received [fixture] [difficulty=1.25e-2]", 0.0125)]
    public void ParsesOnlyReportedMiningJobDifficulty(string line, double expected) => Assert.Equal(expected, PoolDifficultyReader.Read(line));
    [Theory]
    [InlineData("Network Difficulty 1000")]
    [InlineData("Difficulty 0")]
    [InlineData("Difficulty NaN")]
    [InlineData("Difficulty 1e999")]
    [InlineData("DEVFEE Difficulty 1000")]
    [InlineData("Job received without difficulty")]
    public void MissingOrInvalidDifficultyRemainsUnknown(string line) => Assert.Null(PoolDifficultyReader.Read(line));
    [Fact] public async Task LogsDrainOnDisposeAndPreserveNumbersUnknownsAndDailyFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mining-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new MiningLogWriter(directory); var session = Guid.NewGuid();
            for (var i = 0; i < 100; i++)
                Assert.True(log.TryWrite(new(Start, session, "GPU0[t0] share accepted [58ms] [fixture][0]", null, 1250000.5, Start, 1000, Start)));
            Assert.True(log.TryWrite(new(Start.AddDays(1), Guid.NewGuid(), "GPU0[t0] share accepted [58ms] [fixture][0]", null)));
            await log.DisposeAsync();
            Assert.Null(log.Error);
            var files = Directory.GetFiles(directory, "*.log"); Assert.Equal(2, files.Length);
            var lines = files.SelectMany(File.ReadAllLines).ToArray(); Assert.Equal(101, lines.Length);
            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                Assert.True(root.TryGetProperty("timestamp", out _)); Assert.True(root.TryGetProperty("session", out _));
                Assert.False(root.TryGetProperty("wallet", out _));
                if (root.GetProperty("hashrate_hps").ValueKind == JsonValueKind.Null)
                    Assert.Equal(JsonValueKind.Null, root.GetProperty("pool_difficulty").ValueKind);
                else
                {
                    Assert.Equal(1000, root.GetProperty("pool_difficulty").GetDouble());
                    Assert.Equal(1250000.5, root.GetProperty("hashrate_hps").GetDouble());
                    Assert.False(root.TryGetProperty("accepted_10m_per_minute", out _));
                    Assert.Contains("share accepted", root.GetProperty("share_text").GetString());
                }
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact] public async Task DiskFailureIsReportedWithoutStoppingTheConsumer()
    {
        var path = Path.Combine(Path.GetTempPath(), "mining-log-file-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(path, "not a directory");
        try
        {
            await using var log = new MiningLogWriter(path);
            Assert.True(log.TryWrite(new(Start, Guid.NewGuid(), "GPU0[t0] share accepted", null)));
            await log.FlushAsync(); Assert.NotNull(log.Error);
        }
        finally { File.Delete(path); }
    }
    [Fact] public void OverallPreservesSessionBaselineBeyondRollingHourAndResets()
    {
        var h = new AcceptedHistory(); h.Begin(Guid.NewGuid(), Start);
        for (var minute = 1; minute <= 180; minute++) h.Observe(Start.AddMinutes(minute), minute * minute);
        Assert.Equal(32400, h.Overall().Count); Assert.Equal(180, h.Overall().PerMinute);
        Assert.Equal(TimeSpan.FromHours(3), h.Overall().Coverage);
        Assert.NotEqual(h.Window(TimeSpan.FromMinutes(10)).PerMinute, h.Overall().PerMinute);
        h.Begin(Guid.NewGuid(), Start.AddHours(4)); Assert.Null(h.Overall().PerMinute);
    }
    [Fact] public void ShareLogPreservesOriginalTimestampTextAndPrecedingDifficulty()
    {
        const string raw = "[2026-10-30 18:39:40] GPU0[t0] share accepted [   58ms] [fixture][0]";
        var snapshot = new MinerSnapshot(Start.AddSeconds(-1), null, null, null, [], new(), 1234000);
        var entry = ShareLog.FromOutput(raw, Guid.NewGuid(), Start, snapshot, 2000, Start.AddSeconds(-5))!;
        Assert.Equal(raw, entry.ShareText); Assert.Equal(Start, entry.Timestamp);
        Assert.Equal(new DateTime(2026, 10, 30, 18, 39, 40), entry.ShareTimestamp!.Value.DateTime);
        Assert.Equal(1234000, entry.HashrateHps); Assert.Equal(snapshot.Timestamp, entry.HashrateTimestamp);
        Assert.Equal(2000, entry.PoolDifficulty); Assert.Equal(Start.AddSeconds(-5), entry.PoolDifficultyTimestamp);
        Assert.Null(ShareLog.FromOutput("GPU0 share rejected", Guid.NewGuid(), Start, snapshot, 2000, Start));
        Assert.Null(ShareLog.FromOutput("DEVFEE GPU0 share accepted", Guid.NewGuid(), Start, snapshot, 2000, Start));
        Assert.Null(ShareLog.FromOutput("Accepted: 5 API", Guid.NewGuid(), Start, snapshot, 2000, Start));
    }
    [Fact] public void ShareLogDoesNotFabricateUnknownOrStaleHashrateAndTimestamps()
    {
        var old = new MinerSnapshot(Start.AddSeconds(-16), null, null, null, [], new(), 1234000);
        var entry = ShareLog.FromOutput("GPU0 share accepted", Guid.NewGuid(), Start, old, null, null)!;
        Assert.Null(entry.HashrateHps); Assert.Null(entry.HashrateTimestamp);
        Assert.Null(entry.ShareTimestamp); Assert.Null(entry.PoolDifficulty);
    }
    [Fact] public async Task DirectoryChangeKeepsAlreadyQueuedSharesAtTheirOriginalDestination()
    {
        var parent = Path.Combine(Path.GetTempPath(), "log-switch-" + Guid.NewGuid().ToString("N"));
        var first = Path.Combine(parent, "first"); var second = Path.Combine(parent, "second");
        try
        {
            await using var log = new MiningLogWriter(first);
            Assert.True(log.TryWrite(new(Start, Guid.NewGuid(), "GPU0 share accepted FIRST", null)));
            log.SetDirectory(second);
            Assert.True(log.TryWrite(new(Start, Guid.NewGuid(), "GPU0 share accepted SECOND", null)));
            await log.FlushAsync();
            Assert.Contains("FIRST", File.ReadAllText(Directory.GetFiles(first).Single()));
            Assert.DoesNotContain("SECOND", File.ReadAllText(Directory.GetFiles(first).Single()));
            Assert.Contains("SECOND", File.ReadAllText(Directory.GetFiles(second).Single()));
        }
        finally { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
    }
    [Fact] public void InvalidCustomDirectoryCannotBeStoredAsRelativeOrMalformedPath()
    {
        Assert.NotNull(new MinerSettings { MiningLogDirectory = "relative-folder" }.ValidateLogDirectory());
        Assert.NotNull(new MinerSettings { MiningLogDirectory = "\0" }.ValidateLogDirectory());
        Assert.Null(new MinerSettings().ValidateLogDirectory());
        Assert.Null(new MinerSettings { MiningLogDirectory = Path.GetTempPath() }.ValidateLogDirectory());
    }

}
