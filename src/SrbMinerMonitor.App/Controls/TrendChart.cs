using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SrbMinerMonitor.App.ViewModels;
using SrbMinerMonitor.Core;

namespace SrbMinerMonitor.App.Controls;

public sealed class TrendChart : FrameworkElement
{
    public static readonly DependencyProperty MaxGapSecondsProperty = DependencyProperty.Register(nameof(MaxGapSeconds), typeof(double), typeof(TrendChart), new FrameworkPropertyMetadata(75d, FrameworkPropertyMetadataOptions.AffectsRender));
    public double MaxGapSeconds { get => (double)GetValue(MaxGapSecondsProperty); set => SetValue(MaxGapSecondsProperty, value); }
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(nameof(Samples), typeof(IReadOnlyList<ChartSample>), typeof(TrendChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(nameof(Unit), typeof(string), typeof(TrendChart), new FrameworkPropertyMetadata("H/s", FrameworkPropertyMetadataOptions.AffectsRender));
    public IReadOnlyList<ChartSample>? Samples { get => (IReadOnlyList<ChartSample>?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var muted = new SolidColorBrush(Color.FromRgb(140, 158, 175));
        var accent = new SolidColorBrush(Color.FromRgb(85, 212, 238));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(42, 57, 74)), 1);
        var rect = new Rect(70, 12, Math.Max(1, ActualWidth - 82), Math.Max(1, ActualHeight - 40));
        void Text(string text, double x, double y, Brush? brush = null) => dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Microsoft JhengHei UI"), 10, brush ?? muted, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x, y));
        var samples = Samples ?? [];
        var valid = samples.Where(s => s.Value is double n && double.IsFinite(n)).ToArray();
        for (var i = 0; i <= 3; i++) dc.DrawLine(grid, new(rect.Left, rect.Top + rect.Height * i / 3), new(rect.Right, rect.Top + rect.Height * i / 3));
        if (valid.Length == 0) { Text("等待可用資料 · 未提供的數據不繪製", rect.Left + 15, rect.Top + rect.Height / 2); return; }
        var min = valid.Min(s => s.Value!.Value); var max = valid.Max(s => s.Value!.Value);
        var padding = Math.Max((max - min) * 0.2, Math.Max(max * 0.02, 1));
        min = Math.Max(0, min - padding); max += padding;
        var first = samples[0].Time; var last = samples[^1].Time;
        var duration = Math.Max(60, (last - first).TotalSeconds);
        Point Point(ChartSample s) => new(rect.Left + (s.Time - first).TotalSeconds / duration * rect.Width,
            rect.Bottom - (s.Value!.Value - min) / (max - min) * rect.Height);
        for (var i = 0; i <= 3; i++)
        {
            var v = max - (max - min) * i / 3;
            Text(Unit == "H/s" ? Display.Hash(v) : Display.Number(v, Unit), 0, rect.Top + rect.Height * i / 3 - 6);
        }
        ChartSample? previous = null;
        foreach (var sample in samples)
        {
            if (sample.Value is not double n || !double.IsFinite(n)) { previous = null; continue; }
            if (previous is { } p && p.Session == sample.Session && sample.Time - p.Time <= TimeSpan.FromSeconds(MaxGapSeconds))
                dc.DrawLine(new Pen(accent, 2), Point(p), Point(sample));
            else if (previous is { } boundary && boundary.Session != sample.Session)
            {
                var pen = new Pen(muted, 1) { DashStyle = DashStyles.Dash };
                dc.DrawLine(pen, new(Point(sample).X, rect.Top), new(Point(sample).X, rect.Bottom));
                Text("重啟", Point(sample).X + 3, rect.Top);
            }
            dc.DrawEllipse(accent, null, Point(sample), valid.Length < 4 ? 3 : 1.4, valid.Length < 4 ? 3 : 1.4);
            previous = sample;
        }
        Text(first.ToLocalTime().ToString("HH:mm:ss"), rect.Left, rect.Bottom + 10);
        Text(last.ToLocalTime().ToString("HH:mm:ss"), rect.Right - 50, rect.Bottom + 10);
    }
}
