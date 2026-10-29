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
                Assert.True(log.TryWrite(new(Start, "accepted", session, i, 1, 1250000.5, Start, 2.5, 2.5, 2.5, 1000, Start)));
            Assert.True(log.TryWrite(new(Start.AddDays(1), "session_start", Guid.NewGuid(), 0)));
            await log.DisposeAsync();
            Assert.Null(log.Error);
            var files = Directory.GetFiles(directory, "*.log"); Assert.Equal(2, files.Length);
            var lines = files.SelectMany(File.ReadAllLines).ToArray(); Assert.Equal(101, lines.Length);
            foreach (var line in lines)
            {
                using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
                Assert.True(root.TryGetProperty("timestamp", out _)); Assert.True(root.TryGetProperty("session", out _));
                Assert.False(root.TryGetProperty("wallet", out _));
                if (root.GetProperty("kind").GetString() == "session_start")
                    Assert.Equal(JsonValueKind.Null, root.GetProperty("pool_difficulty").ValueKind);
                else
                {
                    Assert.Equal(1000, root.GetProperty("pool_difficulty").GetDouble());
                    Assert.Equal(1250000.5, root.GetProperty("hashrate_hps").GetDouble());
                    Assert.Equal(2.5, root.GetProperty("accepted_10m_per_minute").GetDouble());
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
            Assert.True(log.TryWrite(new(Start, "accepted", Guid.NewGuid(), 1, 1)));
            await log.FlushAsync(); Assert.NotNull(log.Error);
        }
        finally { File.Delete(path); }
    }
}
