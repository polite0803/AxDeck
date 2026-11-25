using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// 一行更新在界面上的状态。控制器推进它，视图只按它画：排队中与更新中不接点击，失败后可以重试。
internal enum SoftwareRowState { Idle, Busy, Done, Failed, Queued }

// 面板上的一行。Name 与两个版本串都已成形，视图不再加工；Action 是按钮上的字（更新 / 下载），
// Badge 是来源标记（WinGet / GitHub / 厂商名），为空则不画。
internal sealed record SoftwareItem(
    string Name,
    string Current,
    string Available,
    string? Badge,
    string Key,
    bool Actionable,
    string Action,
    SoftwareRowState State = SoftwareRowState.Idle);

// 页面顶部的状态行与空态文字。视图不判断「为什么没有结果」，只说控制器给的话：
// 「未检测到 WinGet」和「暂时无法检查更新」是两种完全不同的处境，不能合并成一句。
internal sealed record SoftwareStatus(string Title, string? Note, string Empty, string? EmptyNote, bool Checking, bool CanCheck);

// 软件更新页：一张自绘的列表。用法与 DevicesView 相同——Update 喂数据，视图自己缓动、滚动、命中测试。
// 行高固定、行内只有两次文字测量，内容不变时不重绘，所以空闲时这一页没有开销。
internal sealed class SoftwareView : FrameworkElement
{
    private const double StatusHeight = 30, RowHeight = 46;
    private const double ActionWidth = 64, ActionHeight = 24;
    private const double CheckWidth = 74, CheckHeight = 22;

    private sealed class Row
    {
        public required string Key;
        public required SoftwareItem Item;
        public Eased Presence = new(0), Y, Hover;
        public bool Placed, Leaving, ActionHot;
        public FormattedText? Name, Version;
        public (string Text, double Width) NameKey, VersionKey;
    }

    // alpha=1 的底色：分层窗口上 alpha=0 的像素会被系统穿透给下层，这一页的按钮就点不到了。
    private static readonly Brush Clickable = Frozen(Color.FromArgb(1, 0, 0, 0));
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }

    private readonly List<Row> _rows = [];
    private SoftwareStatus _status = new("", null, "", null, false, false);
    private bool _updated, _settled = true;
    private double _scroll;
    private Row? _hovered, _pressed;
    private bool _checkHot, _checkPressed;

    public event Action<SoftwareItem>? UpdateRequested;
    public event Action? CheckRequested;

    public SoftwareView()
    {
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        MouseMove += (_, e) => Hover(e.GetPosition(this));
        MouseLeave += (_, _) => Hover(new Point(-1, -1));
        SizeChanged += (_, _) => Place(motion: false);
    }

    // settled: 没有正在进行的检查。animate: 行可以缓动到新位置（刚打开页面时直接到位）。
    public void Update(IReadOnlyList<SoftwareItem> items, SoftwareStatus status, bool settled, bool animate)
    {
        var current = Shown.ToList();
        bool same = _updated && _settled == settled && _status == status &&
            current.Count == items.Count && items.Select((item, i) => current[i].Item == item).All(equal => equal);
        if (same) return;

        bool motion = animate && _updated;
        _updated = true; _settled = settled; _status = status;
        double now = Eased.Now;

        var keys = items.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in _rows)
            if (!row.Leaving && !keys.Contains(row.Key)) { row.Leaving = true; row.Presence.Set(0, motion, now); }

        foreach (var item in items) Take(item, now, motion);
        foreach (var row in _rows) row.Hover.Set(ReferenceEquals(row, _hovered) ? 1 : 0, motion, now);
        if (!motion) _rows.RemoveAll(row => row.Leaving);

        var order = items.Select((item, i) => (item.Key, i)).ToDictionary(entry => entry.Key, entry => entry.i, StringComparer.Ordinal);
        _rows.Sort((a, b) => a.Leaving != b.Leaving
            ? b.Leaving.CompareTo(a.Leaving)
            : order.GetValueOrDefault(a.Key, -1).CompareTo(order.GetValueOrDefault(b.Key, -1)));

        Place(motion);
    }

    private void Take(SoftwareItem item, double now, bool motion)
    {
        var row = _rows.FirstOrDefault(candidate => candidate.Key == item.Key && !candidate.Leaving);
        if (row is null)
        {
            row = new Row { Key = item.Key, Item = item, Y = new Eased(0, Eased.Seconds) };
            row.Presence.Set(1, motion, now);
            _rows.Add(row);
            return;
        }
        row.Leaving = false;
        row.Item = item;
        row.Presence.Set(1, motion, now);
    }

    // 显示中的行：离开中的不算（它们只占动画最后几帧）。
    private IEnumerable<Row> Shown => _rows.Where(row => !row.Leaving);

    private double MaxScroll
    {
        get
        {
            double visible = Math.Max(0, ActualHeight - StatusHeight);
            return Math.Max(0, Shown.Count() * RowHeight - visible);
        }
    }

    private void Place(bool motion)
    {
        double now = Eased.Now;
        int i = 0;
        foreach (var row in Shown)
        {
            double y = i++ * RowHeight;
            if (!row.Placed) { row.Y = new Eased(y, Eased.Seconds); row.Placed = true; }
            else row.Y.Set(y, motion, now);
        }
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        if (Moving(now)) Motion.Start(this, Step);
        InvalidateVisual();
    }

    private bool Moving(double now) =>
        _rows.Any(row => row.Presence.Moving(now) || row.Y.Moving(now) || row.Hover.Moving(now));

    private bool Step(double now)
    {
        InvalidateVisual();
        if (Moving(now)) return true;
        _rows.RemoveAll(row => row.Leaving);
        return false;
    }

    private Rect RowRect(Row row) =>
        new(0, StatusHeight + row.Y.At(Eased.Now) - _scroll, Math.Max(0, ActualWidth), RowHeight);

    private Rect ActionRect(Row row)
    {
        var rect = RowRect(row);
        return new Rect(Math.Max(0, ActualWidth - 10 - ActionWidth), rect.Y + (RowHeight - ActionHeight) / 2, ActionWidth, ActionHeight);
    }

    private Rect CheckRect() =>
        new(Math.Max(0, ActualWidth - 8 - CheckWidth), (StatusHeight - CheckHeight) / 2, CheckWidth, CheckHeight);

    private void Hover(Point point)
    {
        var check = CheckRect();
        bool checkHot = _status.CanCheck && !_status.Checking && check.Contains(point);
        Row? hovered = null;
        foreach (var row in Shown)
            if (RowRect(row).Contains(point)) { hovered = row; break; }

        bool changed = checkHot != _checkHot;
        _checkHot = checkHot;
        foreach (var row in _rows)
        {
            bool hot = ReferenceEquals(row, hovered);
            bool action = hot && Actionable(row) && ActionRect(row).Contains(point);
            if (row.Hover.Target != (hot ? 1 : 0)) { row.Hover.Fade(hot, Motion.Enabled); changed = true; }
            if (hot && action != row.ActionHot) { row.ActionHot = action; changed = true; }
        }
        if (_hovered != hovered) { _hovered = hovered; changed = true; }
        Cursor = checkHot || hovered is not null && Actionable(hovered) && ActionRect(hovered).Contains(point) ? Cursors.Hand : null;
        if (changed) Motion.Start(this, Step);
    }

    private static bool Actionable(Row row) => row.Item.Actionable && row.Item.State is SoftwareRowState.Idle or SoftwareRowState.Failed;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var point = e.GetPosition(this);
        var check = CheckRect();
        if (_status.CanCheck && !_status.Checking && check.Contains(point))
        {
            _checkPressed = true; CaptureMouse(); e.Handled = true; InvalidateVisual(); return;
        }
        foreach (var row in Shown)
        {
            if (!Actionable(row) || !ActionRect(row).Contains(point)) continue;
            _pressed = row; CaptureMouse(); e.Handled = true; InvalidateVisual(); return;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool check = _checkPressed; var pressed = _pressed;
        _checkPressed = false; _pressed = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
        InvalidateVisual();
        var point = e.GetPosition(this);
        if (check && CheckRect().Contains(point)) { CheckRequested?.Invoke(); e.Handled = true; return; }
        if (pressed is not null && ActionRect(pressed).Contains(point)) { UpdateRequested?.Invoke(pressed.Item); e.Handled = true; }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (MaxScroll <= 0) return;
        _scroll = Math.Clamp(_scroll - e.Delta / 120.0 * RowHeight * 2, 0, MaxScroll);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(0, 0);

    protected override void OnRender(DrawingContext drawing)
    {
        double w = ActualWidth, h = ActualHeight;
        if (!(w > 0) || !(h > 0)) return;
        double now = Eased.Now, ppd = MonitorDraw.PixelsPerDip(this);
        drawing.DrawRectangle(Clickable, null, new Rect(0, 0, w, h));
        DrawStatus(drawing, w, ppd);

        double top = StatusHeight, visible = Math.Max(0, h - top);
        drawing.PushClip(new RectangleGeometry(new Rect(0, top, w, visible)));
        bool any = false;
        foreach (var row in _rows)
        {
            if (!row.Placed) continue;
            double presence = Math.Clamp(row.Presence.At(now), 0, 1);
            if (presence <= .001) continue;
            var rect = RowRect(row);
            if (rect.Bottom < top || rect.Y > h) continue;
            any = true;
            bool partial = presence < .999;
            if (partial) drawing.PushOpacity(presence * presence);
            DrawRow(drawing, row, w, now, ppd);
            if (partial) drawing.Pop();
        }
        drawing.Pop();
        if (!any) Empty(drawing, w, top, visible, ppd);

        double max = MaxScroll, content = Shown.Count() * RowHeight;
        if (max > 0)
        {
            double length = Math.Max(16, visible * visible / content);
            double y = top + (visible - length) * _scroll / max;
            drawing.DrawRoundedRectangle(UI.Brush(PanelTheme.Hex("scrollThumb")), null, new Rect(w - 3, y, 3, length), 1.5, 1.5);
        }
    }

    private void DrawStatus(DrawingContext drawing, double w, double ppd)
    {
        var check = CheckRect();
        double textWidth = Math.Max(20, check.X - 16);
        var title = MonitorDraw.Trimmed(_status.Title, 12, UI.Primary, textWidth, ppd);
        drawing.DrawText(title, new Point(4, (StatusHeight - title.Height) / 2));

        if (_status.Note is { Length: > 0 } note)
        {
            var noteText = MonitorDraw.Trimmed(note, 10.5, UI.Tertiary, Math.Max(20, textWidth - title.WidthIncludingTrailingWhitespace - 10), ppd);
            double x = 4 + title.WidthIncludingTrailingWhitespace + 10;
            if (x < check.X - 20) MonitorDraw.Baseline(drawing, noteText, x, StatusHeight / 2 + 4);
        }

        if (!_status.CanCheck) return;
        bool hot = _checkHot && !_status.Checking;
        double fill = _checkPressed && hot ? .9 : hot ? .55 : 0;
        if (fill > 0) { drawing.PushOpacity(fill); drawing.DrawRoundedRectangle(UI.Brush(PanelTheme.Hex("fillSelected")), null, check, 5, 5); drawing.Pop(); }
        var label = MonitorDraw.Text(_status.Checking ? Loc.T("检查中…", "Checking…") : Loc.T("立即检查", "Check now"), 11,
            _status.Checking ? UI.Tertiary : hot ? UI.Primary : UI.Secondary, ppd);
        drawing.DrawText(label, new Point(check.X + (check.Width - label.WidthIncludingTrailingWhitespace) / 2,
            check.Y + (check.Height - label.Height) / 2));
    }

    private void DrawRow(DrawingContext drawing, Row row, double w, double now, double ppd)
    {
        var item = row.Item;
        var rect = RowRect(row);
        double hover = Math.Clamp(row.Hover.At(now), 0, 1);
        if (hover > .001)
        {
            drawing.PushOpacity(hover * .7);
            drawing.DrawRoundedRectangle(UI.Brush(PanelTheme.Hex("fill")), null,
                new Rect(4, rect.Y + 2, Math.Max(0, w - 8), RowHeight - 4), 6, 6);
            drawing.Pop();
        }
        drawing.DrawRectangle(UI.Brush(PanelTheme.Hex("rowDivider")), null,
            new Rect(8, Math.Round(rect.Bottom) - 1 / ppd, Math.Max(0, w - 16), 1 / ppd));

        double middle = rect.Y + RowHeight / 2;
        var icon = new Rect(12, Math.Round(middle - 13), 26, 26);
        drawing.DrawRoundedRectangle(UI.Brush(PanelTheme.Hex("fill")), null, icon, 7, 7);
        var letter = MonitorDraw.Text(Initial(item.Name), 12, UI.Secondary, ppd, strong: true);
        drawing.DrawText(letter, new Point(icon.X + (icon.Width - letter.WidthIncludingTrailingWhitespace) / 2,
            icon.Y + (icon.Height - letter.Height) / 2));

        var action = ActionRect(row);
        double badgeWidth = 0;
        FormattedText? badge = null;
        if (item.Badge is { Length: > 0 } badgeText)
        {
            badge = MonitorDraw.Text(badgeText, 10.5, UI.Tertiary, ppd);
            badgeWidth = badge.WidthIncludingTrailingWhitespace;
        }
        double textX = icon.Right + 10;
        double textWidth = Math.Max(20, action.X - 12 - (badge is null ? 0 : badgeWidth + 10) - textX);
        if (badge is not null)
            MonitorDraw.Baseline(drawing, badge, action.X - 12 - badgeWidth, middle + 4);

        var (name, version) = Texts(row, textWidth, ppd);
        double top = Math.Round(middle - 14);
        drawing.DrawText(name, new Point(textX, top));
        drawing.DrawText(version, new Point(textX, top + 17));

        DrawAction(drawing, row, action, now, ppd);
    }

    private void DrawAction(DrawingContext drawing, Row row, Rect action, double now, double ppd)
    {
        var item = row.Item;
        bool enabled = item.Actionable && item.State is not (SoftwareRowState.Busy or SoftwareRowState.Queued);
        bool hot = enabled && row.ActionHot;
        bool pressed = hot && ReferenceEquals(_pressed, row);
        double opacity = enabled ? (pressed ? .9 : hot ? .6 : .35) : .25;
        drawing.PushOpacity(opacity);
        drawing.DrawRoundedRectangle(UI.Brush(PanelTheme.Hex("fillSelected")), null, action, 5, 5);
        drawing.Pop();
        string label = item.State switch
        {
            SoftwareRowState.Queued => Loc.T("排队中…", "Queued…"),
            SoftwareRowState.Busy => Loc.T("更新中…", "Updating…"),
            SoftwareRowState.Done => Loc.T("已更新", "Updated"),
            SoftwareRowState.Failed => Loc.T("重试", "Retry"),
            _ => item.Action,
        };
        var text = MonitorDraw.Text(label, 11, enabled ? UI.Primary : UI.Tertiary, ppd);
        drawing.DrawText(text, new Point(action.X + (action.Width - text.WidthIncludingTrailingWhitespace) / 2,
            action.Y + (action.Height - text.Height) / 2));
    }

    private void Empty(DrawingContext drawing, double w, double top, double visible, double ppd)
    {
        if (_status.Empty.Length == 0 || visible < 30) return;
        var title = MonitorDraw.Trimmed(_status.Empty, 12.5, UI.Secondary, Math.Max(40, w - 32), ppd);
        double y = top + visible / 2 - 10;
        drawing.DrawText(title, new Point((w - title.WidthIncludingTrailingWhitespace) / 2, y));
        if (_status.EmptyNote is not { Length: > 0 } note) return;
        var text = MonitorDraw.Trimmed(note, 11, UI.Tertiary, Math.Max(40, w - 32), ppd);
        drawing.DrawText(text, new Point((w - text.WidthIncludingTrailingWhitespace) / 2, y + 20));
    }

    private static (FormattedText Name, FormattedText Version) Texts(Row row, double width, double ppd)
    {
        var item = row.Item;
        var nameKey = (item.Name, Math.Round(width));
        if (row.Name is null || row.NameKey != nameKey)
        {
            row.Name = MonitorDraw.Trimmed(item.Name, 12.5, UI.Primary, width, ppd);
            row.NameKey = nameKey;
        }
        string version = item.Available.Length > 0 ? $"{item.Current}  →  {item.Available}" : item.Current;
        var versionKey = (version, Math.Round(width));
        if (row.Version is null || row.VersionKey != versionKey)
        {
            row.Version = MonitorDraw.Trimmed(version, 11, UI.Tertiary, width, ppd);
            row.VersionKey = versionKey;
        }
        return (row.Name, row.Version);
    }

    // 没有真实图标的占位：名称的第一个字符，拉丁字母转大写。
    private static string Initial(string name)
    {
        var text = name.Trim();
        if (text.Length == 0) return "?";
        var first = text[0];
        return char.IsLetter(first) ? char.ToUpper(first).ToString() : first.ToString();
    }
}
