using System.Globalization;

namespace SrbMinerMonitor.Core;

public static class ShareLog
{
    public static MiningLogEntry? FromOutput(string line, Guid session, DateTimeOffset received,
        MinerSnapshot? latest, double? difficulty, DateTimeOffset? difficultyTime)
    {
        var text = MinerOutput.Clean(line);
        if (MinerOutput.Outcome(text) != "成功") return null;
        DateTimeOffset? shareTime = null;
        if (text.Length >= 21 && text[0] == '[' && text[20] == ']'
            && DateTime.TryParseExact(text.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var local))
            shareTime = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        // Keep the sample timestamp explicit: a polled rate is not an instantaneous share measurement.
        var rate = latest is not null && received >= latest.Timestamp
            && received - latest.Timestamp <= TimeSpan.FromSeconds(15) ? latest : null;
        return new(received, session, text, shareTime, rate?.TotalHashrate, rate?.Timestamp, difficulty, difficultyTime);
    }
}
