using System.Globalization;
using System.Text.RegularExpressions;

namespace SrbMinerMonitor.Core;

public static partial class PoolDifficultyReader
{
    public static double? Read(string line)
    {
        line = MinerOutput.Clean(line);
        if (line.Contains("devfee", StringComparison.OrdinalIgnoreCase) || line.Contains("donation", StringComparison.OrdinalIgnoreCase)) return null;
        var match = DifficultyLine().Match(line);
        if (!match.Success && line.Contains("Job received", StringComparison.OrdinalIgnoreCase)) match = JobDifficulty().Match(line);
        return match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && double.IsFinite(value) && value > 0 ? value : null;
    }
    [GeneratedRegex(@"(?:^|\]\s*)Difficulty\b\s*[:=]?\s*(\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)(?=\s|\]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex DifficultyLine();
    [GeneratedRegex(@"\bdiff(?:iculty)?\s*[:=]\s*(\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)(?=\s|\]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex JobDifficulty();
}
