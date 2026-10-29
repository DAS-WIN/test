using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.Tests;

public sealed class MonitoringTests
{
    [Theory]
    [InlineData(0, "0 天 00:00:00")]
    [InlineData(3661, "0 天 01:01:01")]
    [InlineData(90061, "1 天 01:01:01")]
    [InlineData(-1, "—")]
    [InlineData(double.NaN, "—")]
    public void UptimeUsesQuotedChineseLiterals(double seconds, string expected) => Assert.Equal(expected, Display.Uptime(seconds));
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "single-algorithm.synthetic.json"));
    [Fact] public void ParsesDeviceIdsAndCumulativeSharesWithoutCountingTotalsAsAnotherGpu()
    {
        var snapshot = ApiParser.Parse(Json, DateTimeOffset.UtcNow);
        Assert.Equal([0, 2], snapshot.Gpus.Select(g => g.Id));
        Assert.Equal(16, snapshot.Shares.Accepted);
        Assert.Equal(1, snapshot.Shares.Rejected);
        Assert.Null(snapshot.Shares.Stale);
        Assert.Null(snapshot.Shares.Timeout);
        Assert.Null(snapshot.TotalPower);
        var again = ApiParser.Parse(Json, DateTimeOffset.UtcNow);
        Assert.Equal(snapshot.Shares, again.Shares);
    }
    [Fact] public void MatchesNvidiaSensorsByBusInsteadOfEnumerationOrder()
    {
        var sensors = NvidiaSensors.Parse("00000000:0A:00.0, Fixture GPU A, 100, 40, 55, 1500, 5000\n00000000:01:00.0, Fixture GPU B, 150, 50, 60, 1600, 6000\n");
        var snapshot = NvidiaSensors.Merge(ApiParser.Parse(Json, DateTimeOffset.UtcNow), ApiParser.DeviceBuses(Json), sensors);
        Assert.Equal("Fixture GPU B", snapshot.Gpus[0].Name);
        Assert.Equal(150, snapshot.Gpus[0].Power);
        Assert.Equal(250, snapshot.TotalPower!.Value, 5);
        Assert.Equal(6000, snapshot.Gpus[0].MemoryClock);
    }
    [Fact] public void AmbiguousBusDoesNotAssignSensorsToWrongCard()
    {
        var sensor = new NvidiaSensor(1, "GPU", 100, 50, 60, 2000, 9000);
        var snapshot = NvidiaSensors.Merge(ApiParser.Parse(Json, DateTimeOffset.UtcNow), ApiParser.DeviceBuses(Json), [sensor, sensor]);
        Assert.Null(snapshot.Gpus[0].Power);
    }
    [Fact] public void SensorNotSupportedIsUnknownInsteadOfZero()
    {
        var sensors = NvidiaSensors.Parse("00000000:01:00.0, NVIDIA GPU, [N/A], [Not Supported], 60, 2100, 9000\n");
        Assert.Single(sensors); Assert.Null(sensors[0].Power); Assert.Null(sensors[0].Fan);
    }
    [Fact] public void MissingPowerOrZeroNeverProducesEfficiency()
    {
        Assert.Null(new GpuMetrics(0, "GPU", 100, 0).Efficiency);
        Assert.Null(new GpuMetrics(0, "GPU", 100).Efficiency);
        Assert.Equal("—", Display.Hash(null, true));
        Assert.Equal("0.800 TH/s/W", Display.Hash(120e12 / 150, true, 120e12));
    }
    [Fact] public void AcceptanceDoesNotDoubleCountStale()
    {
        Assert.Equal(80, new ShareStats(8, 2, 2).Acceptance);
        Assert.Null(new ShareStats(0, 0).Acceptance);
        Assert.Null(new ShareStats(8, null).Acceptance);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"algorithms\":[]}")]
    [InlineData("{\"algorithms\":[{},{}]}")]
    public void UnsupportedApiIsRejected(string json) => Assert.Throws<FormatException>(() => ApiParser.Parse(json, DateTimeOffset.UtcNow));
    [Fact] public void MissingFieldsRemainNull()
    {
        var snapshot = ApiParser.Parse("{\"algorithms\":[{\"hashrate\":{\"gpu\":{\"gpu0\":null}}}]}", DateTimeOffset.UtcNow);
        Assert.Single(snapshot.Gpus); Assert.Null(snapshot.TotalHashrate); Assert.Null(snapshot.Shares.Accepted);
        Assert.Null(snapshot.PoolConnected);
    }
    [Fact] public void HistoryRetainsSixHoursAndAveragesOnlyCurrentSession()
    {
        var history = new MetricHistory(); var now = DateTimeOffset.UtcNow; var session = Guid.NewGuid();
        var sample = ApiParser.Parse(Json, now) with { Session = session, Hashrate = 100 };
        history.Add(sample with { Timestamp = now.AddHours(-7) });
        history.Add(sample with { Timestamp = now.AddSeconds(-10), Hashrate = 200 });
        history.Add(sample with { Session = Guid.NewGuid(), Hashrate = 300 });
        Assert.Equal(2, history.Samples.Count);
        var average = history.Average(now, TimeSpan.FromMinutes(1));
        Assert.Equal(300, average.Value); Assert.Equal(TimeSpan.Zero, average.Coverage);
    }
}
