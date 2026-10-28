namespace SrbMinerMonitor.Core;

public enum RefreshUnit { Seconds, Minutes, Hours, Days }
public sealed record MetricRefreshOptions(int Value = 1, RefreshUnit Unit = RefreshUnit.Minutes)
{
    public string? Validate() => Value is < 1 or > 10000 || !Enum.IsDefined(Unit)
        ? "更新間隔請填入 1–10000 的正整數，並選擇秒／分／時／天。" : null;
    public TimeSpan Duration => Unit switch
    {
        RefreshUnit.Seconds => TimeSpan.FromSeconds(Value), RefreshUnit.Minutes => TimeSpan.FromMinutes(Value),
        RefreshUnit.Hours => TimeSpan.FromHours(Value), RefreshUnit.Days => TimeSpan.FromDays(Value),
        _ => throw new ArgumentOutOfRangeException(nameof(Unit))
    };
    public string Label => $"{Value} {Unit switch { RefreshUnit.Seconds => "秒", RefreshUnit.Minutes => "分", RefreshUnit.Hours => "時", _ => "天" }}";
}
