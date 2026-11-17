using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The day's traffic, one line under the drives: the bytes read from and written to the disks on the left, the bytes received over
// and sent through the network on the right, each with a glyph in the color of the reading it belongs to. The figures are the
// totals since the day began, in decimal units, as the network's rates are shown (MonitorFormat.Bytes).
internal sealed class TrafficBlock : FrameworkElement
{
    private readonly MonitorColors _colors;
    private readonly double _body, _caption;
    private TrafficTotals? _totals;

    public TrafficBlock(MonitorColors colors, double body, double caption)
    {
        _colors = colors; _body = body; _caption = caption;
        Height = Math.Ceiling(body * 1.7);
    }

    // For screen readers: "今日流量 · 磁盘 读 1.2 GB · 写 340 MB · 网络 收 80 MB · 发 12 MB".
    protected override AutomationPeer OnCreateAutomationPeer() => new DrawnPeer(this, AutomationControlType.Text, () => Loc.T("今日流量 · ", "Today's traffic · ") + DrawnPeer.FirstLine(ToolTip));

    public void Set(TrafficTotals? totals)
    {
        _totals = totals;
        ToolTip = totals is { } t
            ? Loc.T("磁盘 读 ", "Disk read ") + MonitorFormat.Bytes(t.DiskRead).Text
                + Loc.T(" · 写 ", " · written ") + MonitorFormat.Bytes(t.DiskWritten).Text
                + Loc.T(" · 网络 收 ", " · network received ") + MonitorFormat.Bytes(t.NetworkReceived).Text
                + Loc.T(" · 发 ", " · sent ") + MonitorFormat.Bytes(t.NetworkSent).Text
                + Loc.T("\n今日累计，自本地时间零点起算。", "\nCounted since midnight, local time.")
            : Loc.T("暂无读数", "No reading yet");
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, Height);

    protected override void OnRender(DrawingContext drawing)
    {
        double w = ActualWidth, ppd = MonitorDraw.PixelsPerDip(this), line = Math.Ceiling(_body * 1.7);
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, line));
        if (_totals is not { } totals) return;
        double badge = Math.Round(_body + 5), glyph = Math.Round(badge * 9 / 16), baseline = Math.Round(line / 2 + _body * .38);
        // Two pairs of figures share the line: the disks' on the left, the network's on the right, each with its own arrow.
        double half = Math.Floor((w - 12) / 2);
        Pair(drawing, 0, half, totals.DiskRead, totals.DiskWritten, MonitorGlyph.Drive, _colors.Storage, ppd, badge, glyph, baseline);
        Pair(drawing, half + 12, half, totals.NetworkReceived, totals.NetworkSent, MonitorGlyph.Down, _colors.Download, ppd, badge, glyph, baseline);
    }

    // A pair of figures with their glyph: the first on the left of the glyph's arrow, the second after it.
    private void Pair(DrawingContext drawing, double left, double width, long first, long second, MonitorGlyph glyph, string color,
        double ppd, double badge, double arrowSize, double baseline)
    {
        MonitorDraw.Arrow(drawing, new Point(left + (badge - arrowSize) / 2, baseline - _body * .38 - arrowSize / 2), arrowSize, Math.Max(1, arrowSize * .13), glyph, color);
        double x = left + badge + Math.Round(_body * .5);
        var (a, aUnit) = MonitorFormat.Bytes(first);
        var number = MonitorDraw.Text(a, _body, UI.Primary, ppd, strong: true);
        MonitorDraw.Baseline(drawing, number, x, baseline); x += number.WidthIncludingTrailingWhitespace;
        if (aUnit.Length > 0) { var unit = MonitorDraw.Text(aUnit, _caption, UI.Tertiary, ppd); MonitorDraw.Baseline(drawing, unit, x + 2, baseline); x += 2 + unit.WidthIncludingTrailingWhitespace; }
        // The second figure after it, with a slash between them.
        var (b, bUnit) = MonitorFormat.Bytes(second);
        var second2 = MonitorDraw.Text(b, _body, UI.Primary, ppd, strong: true);
        var slash = MonitorDraw.Text("/", _caption, UI.Tertiary, ppd);
        double room = left + width - x;
        if (room >= second2.WidthIncludingTrailingWhitespace + 8)
        {
            MonitorDraw.Baseline(drawing, slash, x + 2, baseline);
            double bx = x + 2 + slash.WidthIncludingTrailingWhitespace + 2;
            MonitorDraw.Baseline(drawing, second2, bx, baseline);
            if (bUnit.Length > 0)
            {
                var unit = MonitorDraw.Text(bUnit, _caption, UI.Tertiary, ppd);
                MonitorDraw.Baseline(drawing, unit, bx + second2.WidthIncludingTrailingWhitespace + 2, baseline);
            }
        }
    }
}
