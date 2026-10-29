using System.Text.Json;
using SrbMinerMonitor.Core;
namespace SrbMinerMonitor.Tests;

public sealed class RefreshIntervalTests
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
        var time = new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero);
        MinerSnapshot Sample(int second, int value) => new(time.AddSeconds(second), "test", "testhash", second,
            [new(0, "GPU", value, value, value, value, value, value)], new(value, 0), value, Session: session);
        var original = Sample(0, 100); Assert.True(sampler.Update(original, interval));
        Assert.False(sampler.Update(Sample(1, 200), interval)); Assert.Same(original, sampler.Snapshot);
        Assert.Equal(100, sampler.Snapshot!.Gpus[0].Power); Assert.Equal(100, sampler.Snapshot.MaxTemperature);
        Assert.True(sampler.Update(Sample(2, 300), interval)); Assert.Equal(300, sampler.Snapshot!.Gpus[0].MemoryClock);
        Assert.True(sampler.Update(Sample(2, 400), new(1, RefreshUnit.Days))); // Saving a new interval applies immediately.
    }
}
