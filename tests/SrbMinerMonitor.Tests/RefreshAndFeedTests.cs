using System.Text.Json;
using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.Tests;

public sealed class RefreshAndFeedTests
{
    [Theory]
    [InlineData(RefreshUnit.Seconds, 2, 2)]
    [InlineData(RefreshUnit.Minutes, 2, 120)]
    [InlineData(RefreshUnit.Hours, 2, 7200)]
    [InlineData(RefreshUnit.Days, 2, 172800)]
    public void UnitsConvertToRequestedInterval(RefreshUnit unit, int value, int seconds) => Assert.Equal(TimeSpan.FromSeconds(seconds), new MetricRefreshOptions(value, unit).Duration);
    [Fact] public void LegacySettingsDefaultToOneMinuteAndCustomIntervalRoundTrips()
    {
        var legacy = JsonSerializer.Deserialize<MinerSettings>("{\"BatchFile\":\"mine.bat\"}")!;
        Assert.Equal(new MetricRefreshOptions(), legacy.MetricRefresh);
        var custom = legacy with { MetricRefresh = new(3, RefreshUnit.Days) };
        Assert.Equal(custom.MetricRefresh, JsonSerializer.Deserialize<MinerSettings>(JsonSerializer.Serialize(custom))!.MetricRefresh);
    }
    [Theory]
    [InlineData(0, RefreshUnit.Minutes)]
    [InlineData(-1, RefreshUnit.Seconds)]
    [InlineData(10001, RefreshUnit.Days)]
    [InlineData(1, (RefreshUnit)99)]
    public void InvalidIntervalsFailValidation(int value, RefreshUnit unit) => Assert.NotNull(new MetricRefreshOptions(value, unit).Validate());
    [Fact] public void AllHardwareValuesHoldUntilConfiguredBucketChanges()
    {
        var sampler = new MinuteMetrics(); var session = Guid.NewGuid(); var interval = new MetricRefreshOptions(2, RefreshUnit.Seconds);
        var time = new DateTimeOffset(2026, 10, 28, 23, 24, 0, TimeSpan.FromHours(8));
        MinerSnapshot Sample(int second, int value) => new(time.AddSeconds(second), "test", "pearlhash", second,
            [new(0, "GPU", value, value, value, value, value, value)], new(value, 0), value, Session: session);
        var original = Sample(0, 100); Assert.True(sampler.Update(original, interval));
        Assert.False(sampler.Update(Sample(1, 200), interval)); Assert.Same(original, sampler.Snapshot);
        Assert.Equal(100, sampler.Snapshot!.Gpus[0].Power); Assert.Equal(100, sampler.Snapshot.MaxTemperature);
        Assert.True(sampler.Update(Sample(2, 300), interval)); Assert.Equal(300, sampler.Snapshot!.Gpus[0].MemoryClock);
        Assert.True(sampler.Update(Sample(2, 400), new(1, RefreshUnit.Days))); // Saving a new interval applies immediately.
    }
    [Fact] public void ApiFirstPublishesImmediatelyThenRawOutputEnrichesSameEntry()
    {
        var feed = new ShareFeed(); var now = DateTimeOffset.Now;
        var api = Assert.Single(feed.ObserveApi(new(1, 0), now, true));
        Assert.Equal("成功", api.Category); Assert.Contains("API 已確認", api.Message);
        var raw = feed.ObserveOutput("GPU0[t0] share accepted [213ms]", new(1, 0), now.AddSeconds(10))!;
        Assert.True(raw.Replace); Assert.Equal(api.Id, raw.Id); Assert.Equal(now, raw.Time);
        Assert.Empty(feed.ObserveApi(new(1, 0), now, true));
    }
    [Fact] public void OutputFirstIsNotDuplicatedByApiAndMixedOrderingPreservesEachShare()
    {
        var feed = new ShareFeed(); var now = DateTimeOffset.Now;
        var raw = feed.ObserveOutput("GPU0[t0] share accepted", new(1, 0), now)!;
        Assert.False(raw.Replace); Assert.Empty(feed.ObserveApi(new(1, 0), now, true));
        var notices = feed.ObserveApi(new(3, 0), now, true); Assert.Equal(2, notices.Count);
        Assert.Equal(notices[0].Id, feed.ObserveOutput("GPU0[t0] share accepted", new(2, 0), now)!.Id);
        Assert.Equal(notices[1].Id, feed.ObserveOutput("GPU0[t0] share accepted", new(3, 0), now)!.Id);
    }
    [Fact] public void ExternalHistoricalTotalDoesNotBecomeRecentShareAndResetStartsFresh()
    {
        var feed = new ShareFeed(); var now = DateTimeOffset.Now;
        Assert.Empty(feed.ObserveApi(new(450, 0), now, false));
        Assert.Single(feed.ObserveApi(new(451, 0), now, false));
        feed.Reset(); Assert.Single(feed.ObserveApi(new(1, 0), now, true));
    }
    [Fact] public void MissingFieldsDoNotCreateEventsAndLargeJumpsStayBounded()
    {
        var feed = new ShareFeed(); var now = DateTimeOffset.Now;
        Assert.Empty(feed.ObserveApi(new(), now, true));
        Assert.Equal(100, feed.ObserveApi(new(long.MaxValue, 0), now, true).Count);
        Assert.Empty(feed.ObserveApi(new(long.MaxValue, 0), now, true));
    }
}
