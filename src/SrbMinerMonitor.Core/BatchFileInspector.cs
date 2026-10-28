using System.Text;
using System.Text.RegularExpressions;

namespace SrbMinerMonitor.Core;

public sealed record BatchInspection(bool ApiEnabled, int? ApiPort, string? Executable, string? Pool = null);

public static partial class BatchFileInspector
{
    public static BatchInspection Inspect(string text, string directory)
    {
        var commands = string.Join('\n', text.Split('\n').Where(line => !IgnoreLine().IsMatch(line)));
        var enabled = ApiFlag().IsMatch(commands);
        var port = ApiPortPattern().Match(commands);
        int? apiPort = port.Success && int.TryParse(port.Groups[1].Value, out var p) && p is > 0 and <= 65535 ? p : null;
        var executable = MinerPath().Match(commands);
        string? path = null;
        if (executable.Success)
        {
            var value = executable.Groups[1].Success ? executable.Groups[1].Value : executable.Groups[2].Value;
            value = value.Replace("%~dp0", directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            if (!value.Contains('%')) path = Path.GetFullPath(value, directory);
        }
        var minerLines = text.Replace("^\r\n", " ").Replace("^\n", " ").Split('\n')
            .Where(line => !IgnoreLine().IsMatch(line) && !ControlLine().IsMatch(line) && MinerPath().IsMatch(line)).ToArray();
        var pool = minerLines.Length == 1 ? PoolPattern().Match(minerLines[0]) : Match.Empty;
        var poolValue = pool.Success ? (pool.Groups[1].Success ? pool.Groups[1].Value : pool.Groups[2].Value) : null;
        // Do not resolve BAT variables or expose URL credentials in the overview.
        if (poolValue?.IndexOfAny(['%', '!', '@', ';', ',', '&', '|']) >= 0) poolValue = null;
        return new(enabled, apiPort, path, poolValue);
    }

    // Preserve the original bytes/encoding; the only inserted/replaced characters are ASCII API arguments.
    public static byte[] CreateMonitoringCopy(byte[] original, int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (original.Length > 1024 * 1024) throw new FormatException("BAT 超過 1 MB，請手動設定 API 參數。");
        var encoding = original.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()) ? Encoding.Unicode :
            original.AsSpan().StartsWith(Encoding.BigEndianUnicode.GetPreamble()) ? Encoding.BigEndianUnicode : Encoding.Latin1;
        var text = encoding.GetString(original);
        var lines = text.Split('\n');
        var candidates = Enumerable.Range(0, lines.Length).Where(i => !IgnoreLine().IsMatch(lines[i]) && !ControlLine().IsMatch(lines[i]) && MinerPath().IsMatch(lines[i])).ToArray();
        if (candidates.Length != 1) throw new FormatException("需要恰好一行可辨識的 SRBMiner-MULTI.exe 指令，才能建立監控副本；原 BAT 未修改。");
        var start = candidates[0]; var end = start;
        while (lines[end].TrimEnd('\r', ' ', '\t').EndsWith('^') && end + 1 < lines.Length) end++;
        var command = string.Join(' ', lines[start..(end + 1)]).Replace("^\r", "").Replace("^", "");
        var quoted = false;
        foreach (var ch in command)
        {
            if (ch == '"') quoted = !quoted;
            if (!quoted && ch is '&' or '|') throw new FormatException("BAT 指令含有複合命令，請手動加入 API 參數；原 BAT 未修改。");
        }
        if (Regex.IsMatch(command, @"--api-port\s+(?!\d+(?:\s|$))\S+", RegexOptions.IgnoreCase))
            throw new FormatException("API 連接埠使用變數或不支援的格式，請手動確認；原 BAT 未修改。");
        if (ApiPortPattern().IsMatch(command))
        {
            if (!lines[start..(end + 1)].Any(line => ApiPortPattern().IsMatch(line))) throw new FormatException("API 連接埠參數跨行，請手動確認；原 BAT 未修改。");
            for (var i = start; i <= end; i++) lines[i] = ApiPortPattern().Replace(lines[i], "--api-port " + port);
        }
        var suffix = "";
        if (!ApiFlag().IsMatch(command)) suffix += " --api-enable";
        if (!ApiPortPattern().IsMatch(command)) suffix += " --api-port " + port;
        var cr = lines[end].EndsWith('\r') ? "\r" : "";
        lines[end] = lines[end].TrimEnd('\r') + suffix + cr;
        return encoding.GetBytes(string.Join('\n', lines));
    }

    [GeneratedRegex(@"^\s*(?:@?(?:rem\b|echo\b|set\b)|::)", RegexOptions.IgnoreCase)] private static partial Regex IgnoreLine();
    [GeneratedRegex(@"^\s*@?(?:if|for|where|dir|type|copy|move|del)\b", RegexOptions.IgnoreCase)] private static partial Regex ControlLine();
    [GeneratedRegex(@"(?<![\w-])--api-enable(?=\s|$|[\r\n])", RegexOptions.IgnoreCase)] private static partial Regex ApiFlag();
    [GeneratedRegex(@"--api-port\s+(\d+)(?=\s|$)", RegexOptions.IgnoreCase)] private static partial Regex ApiPortPattern();
    [GeneratedRegex("\"([^\"\r\n]*SRBMiner-MULTI\\.exe)\"|([^\\s\"&|]*SRBMiner-MULTI\\.exe)", RegexOptions.IgnoreCase)] private static partial Regex MinerPath();
    [GeneratedRegex("(?:^|\\s)--pool\\s+(?:\"([^\"]+)\"|([^\\s^]+))", RegexOptions.IgnoreCase)] private static partial Regex PoolPattern();
}
