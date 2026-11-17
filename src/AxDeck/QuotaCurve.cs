using System.Windows;
using System.Windows.Media;

namespace AxDeck;

// The used share of a quota over the last day, drawn faintly behind the quota's track: a line over a fill, newest at the right
// edge, so a glance shows whether a quota has been climbing steadily or is only high now. Nothing is drawn with fewer than
// three readings: two points are a line, not a trend, and an empty area would read as "no history" wrongly.
internal sealed class QuotaCurve : FrameworkElement
{
    private readonly IReadOnlyList<double> _history;
    private readonly string _color;

    public QuotaCurve(IReadOnlyList<double> history, string color)
    {
        _history = history; _color = color;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    // Three readings is the least a curve can say something with; the same floor MonitorDraw.Sparkline keeps.
    public static bool Worth(IReadOnlyList<double> history) => history.Count >= 3;

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0 || !Worth(_history)) return;
        // Shares are 0 to 100, so the area's height is the whole range: a quota at 30% sits three tenths up, which keeps two
        // quotas' curves comparable at a glance.
        var points = new Point[_history.Count];
        double step = w / Math.Max(1, _history.Count - 1);
        for (int i = 0; i < points.Length; i++)
            points[i] = new Point(i * step, h - 1 - Math.Clamp(_history[i] / 100, 0, 1) * (h - 2));
        var line = new StreamGeometry();
        var under = new StreamGeometry();
        using (var context = line.Open()) Curve(context, points, false, h);
        using (var context = under.Open()) Curve(context, points, true, h);
        line.Freeze(); under.Freeze();
        drawing.DrawGeometry(UI.Brush(MonitorDraw.Faint(_color, .14)), null, under);
        var pen = new Pen(UI.Brush(_color), 1.2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        // A curve tall enough to see is drawn at a third of the fill's strength: it is behind the track's number, not beside it.
        drawing.PushOpacity(.5);
        drawing.DrawGeometry(null, pen, line);
        drawing.Pop();
    }

    // A straight polyline: a quota's history has no shape worth smoothing, and a smoothed curve would draw a rise between two
    // readings that was never measured.
    private static void Curve(StreamGeometryContext context, Point[] points, bool closed, double bottom)
    {
        context.BeginFigure(closed ? new Point(points[0].X, bottom) : points[0], closed, closed);
        // A closed figure starts at the bottom left, rises through the readings, and comes back along the bottom edge.
        if (closed) context.LineTo(points[0], false, false);
        foreach (var point in points) context.LineTo(point, true, false);
        if (closed) context.LineTo(new Point(points[^1].X, bottom), false, false);
    }
}