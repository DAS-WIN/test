using System.Globalization;

namespace SrbMinerMonitor.Core;

public static class Display
{
    public static string Uptime(double? seconds) => seconds is double value && double.IsFinite(value) && value >= 0
        ? TimeSpan.FromSeconds(Math.Min(value, TimeSpan.MaxValue.TotalSeconds / 2)).ToString(@"d' 天 'hh\:mm\:ss", CultureInfo.InvariantCulture) : "—";
    public static string Number(double? value, string unit = "", string format = "0.0") =>
        value is double n && double.IsFinite(n) ? $"{n.ToString(format, CultureInfo.InvariantCulture)}{(unit.Length > 0 ? " " + unit : "")}" : "—";
    public static string Hash(double? value, bool efficiency = false, double? unitReference = null)
    {
        if (value is not double v || !double.IsFinite(v)) return "—";
        string[] units = ["H/s", "kH/s", "MH/s", "GH/s", "TH/s", "PH/s"];
        var i = 0;
        var reference = unitReference ?? v;
        while (Math.Abs(reference) >= 1000 && i < units.Length - 1) { v /= 1000; reference /= 1000; i++; }
        return Number(v, units[i] + (efficiency ? "/W" : ""), efficiency ? "0.000" : "0.00");
    }
    public static string Count(long? v) => v?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";
}
