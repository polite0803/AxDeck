using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 一个分组窗口：桌面上的一块透明区域，把桌面上的东西收在一格里。它可以被拖动、被缩放，接住拖进来的文件，
// 并按自己的顺序摆放它们。移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），那里分组也是独立的窗口。
internal sealed class BoxWindow : Window
{
    private readonly BoxService _boxes;
    private readonly DesktopBox _box;
    private readonly Grid _root;
    private readonly Border _frame;
    private readonly Border _title;
    private readonly TextBlock _titleText;
    private readonly WrapPanel _icons;
    private readonly Border _dropMark;
    private readonly DispatcherTimer _idleTimer;

    // 鼠标按住时指着的那一项，以及它会落在哪一格。
    private string? _dragKey;
    private BoxInsertion _insertion = BoxInsertion.None;
    private Point _dragOrigin;
    private bool _dragging;
    // 拖动是用户按 Esc 取消的，还是真的落下去了。
    private bool _cancelled;
    // 标题栏正在拖动窗口，以及开始时鼠标的位置。
    private bool _moving;
    private Point _moveOrigin;
    private double _moveBoxX, _moveBoxY;
    // 窗口正在被某一条边缩放，以及是哪一条边。
    private bool _resizing;
    private BoxResizeDirection? _resizeDirection;
    private double _resizeOriginX, _resizeOriginY, _resizeOriginWidth, _resizeOriginHeight;

    // 窗口此刻的位置和大小，以所在屏幕的 DIP 计。
    private double _x, _y, _width, _height;

    // The icons and the line that marks the landing place, so the window's own drawing can be looked at.
    internal WrapPanel Icons => _icons;
    internal Border DropMark => _dropMark;

    public BoxWindow(BoxService boxes, DesktopBox box)
    {
        _boxes = boxes;
        _box = box;
        Title = box.Title;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        _width = box.Width > 0 ? box.Width : BoxWindowSize.DefaultWidth;
        _height = box.Height > 0 ? box.Height : BoxWindowSize.DefaultHeight;
        _x = box.X;
        _y = box.Y;

        _root = new Grid();
        _frame = new Border
        {
            CornerRadius = new CornerRadius(boxes.Settings.CornerRadius),
            Background = new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(boxes.Settings.BackgroundOpacity * 255 / 100, 0, 255), 28, 28, 32)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            SnapsToDevicePixels = true
        };
        _root.Children.Add(_frame);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BoxWindowSize.TitleHeight) });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _frame.Child = content;

        // 标题栏：写着分组的名字，也是拖动窗口的手柄。
        _titleText = new TextBlock
        {
            Text = box.Title,
            Foreground = Brushes.White,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        _title = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
        _title.Child = _titleText;
        _title.MouseLeftButtonDown += Title_MouseLeftButtonDown;
        _title.MouseMove += Title_MouseMove;
        _title.MouseLeftButtonUp += Title_MouseLeftButtonUp;
        _title.MouseRightButtonUp += Title_MouseRightButtonUp;
        content.Children.Add(_title);

        var area = new Grid { AllowDrop = true };
        _icons = new WrapPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _icons.PreviewMouseLeftButtonDown += Icons_MouseLeftButtonDown;
        _icons.PreviewMouseMove += Icons_MouseMove;
        area.Children.Add(_icons);

        // 拖进来的文件会落在哪一格，由这条竖线指示。它贴在那一个图标的边上，
        // 所以它靠左靠上排：不定对齐的话，Grid 会把它放在中间，线就落在离图标很远的地方。
        _dropMark = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 120, 170, 255)),
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        area.Children.Add(_dropMark);

        area.DragEnter += Area_DragEnter;
        area.DragOver += Area_DragOver;
        area.DragLeave += Area_DragLeave;
        area.Drop += Area_Drop;
        area.MouseLeftButtonUp += Area_MouseLeftButtonUp;
        Grid.SetRow(area, 1);
        content.Children.Add(area);

        // 边缘和四角的手柄，窗口靠它们缩放。
        foreach (var (direction, horizontal, vertical) in Grips())
        {
            var grip = new Border
            {
                Width = direction is BoxResizeDirection.East or BoxResizeDirection.West ? 6 : 12,
                Height = direction is BoxResizeDirection.North or BoxResizeDirection.South ? 6 : 12,
                Background = Brushes.Transparent,
                Cursor = CursorFor(direction),
                HorizontalAlignment = horizontal,
                VerticalAlignment = vertical,
                Tag = direction
            };
            grip.MouseLeftButtonDown += Grip_MouseLeftButtonDown;
            grip.MouseMove += Grip_MouseMove;
            grip.MouseLeftButtonUp += Grip_MouseLeftButtonUp;
            _root.Children.Add(grip);
        }

        Content = _root;

        // 鼠标不在窗口上时它淡出，回到窗口上时又回来。
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _idleTimer.Tick += IdleTimer_Tick;

        Loaded += (_, _) =>
        {
            ApplyPosition();
            Rebuild();
        };
        Closed += (_, _) => _idleTimer.Stop();
        // The drag asks the window whether it may go on; the Escape key ends it, and that is remembered.
        DragDrop.AddQueryContinueDragHandler(this, new QueryContinueDragEventHandler(Icons_QueryContinueDrag));
    }

    // 每条边的手柄：方向，以及它贴在哪一边、哪一端。
    private static IEnumerable<(BoxResizeDirection Direction, HorizontalAlignment Horizontal, VerticalAlignment Vertical)> Grips()
    {
        yield return (BoxResizeDirection.East, HorizontalAlignment.Right, VerticalAlignment.Top);
        yield return (BoxResizeDirection.West, HorizontalAlignment.Left, VerticalAlignment.Top);
        yield return (BoxResizeDirection.North, HorizontalAlignment.Left, VerticalAlignment.Top);
        yield return (BoxResizeDirection.South, HorizontalAlignment.Left, VerticalAlignment.Bottom);
        yield return (BoxResizeDirection.NorthEast, HorizontalAlignment.Right, VerticalAlignment.Top);
        yield return (BoxResizeDirection.NorthWest, HorizontalAlignment.Left, VerticalAlignment.Top);
        yield return (BoxResizeDirection.SouthEast, HorizontalAlignment.Right, VerticalAlignment.Bottom);
        yield return (BoxResizeDirection.SouthWest, HorizontalAlignment.Left, VerticalAlignment.Bottom);
    }

    // ---- 位置与大小 ----

    // 把窗口放到分组所在的位置，以所在屏幕的 DIP 计。
    private void ApplyPosition()
    {
        Left = _x;
        Top = _y;
        Width = _width;
        Height = _height;
    }

    // 窗口此刻的位置和大小，存起来。
    private void SaveBounds()
    {
        _box.X = _x; _box.Y = _y; _box.Width = _width; _box.Height = _height;
        _boxes.Save(_box);
    }

    // ---- 标题 ----

    // 标题栏是拖动窗口的手柄。
    private void Title_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_box.Locked) return;
        _moving = true;
        _moveOrigin = e.GetPosition(this);
        _moveBoxX = _x;
        _moveBoxY = _y;
        CaptureMouse();
        e.Handled = true;
    }

    private void Title_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_moving) return;
        var point = e.GetPosition(this);
        var scale = Scale();
        var dx = (point.X - _moveOrigin.X) / scale;
        var dy = (point.Y - _moveOrigin.Y) / scale;
        var (snapX, snapY) = BoxLayout.SnapPosition(_moveBoxX + dx, _moveBoxY + dy, _width, _height,
            _boxes.Boxes, _box.Id, WorkArea(), _boxes.Settings);
        _x = snapX; _y = snapY;
        ApplyPosition();
    }

    private void Title_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_moving) return;
        _moving = false;
        ReleaseMouseCapture();
        SaveBounds();
    }

    // 标题上按右键，打开分组自己的菜单。
    private void Title_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        BoxMenu.Show(this, _boxes, _box, Rebuild);
        e.Handled = true;
    }

    // ---- 大小 ----

    private void Grip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_box.Locked) return;
        _resizeDirection = (sender as Border)?.Tag is BoxResizeDirection direction ? direction : null;
        if (_resizeDirection is null) return;
        _resizing = true;
        _resizeOriginX = _x; _resizeOriginY = _y;
        _resizeOriginWidth = _width; _resizeOriginHeight = _height;
        CaptureMouse();
        e.Handled = true;
    }

    private void Grip_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_resizing || _resizeDirection is null) return;
        // 手柄贴在窗口边上，鼠标走过的距离就是窗口要变的那一段。
        var origin = PointToScreen(new Point(0, 0));
        var cursor = PointFromScreen(new Point(WindowsHost.Cursor().X, WindowsHost.Cursor().Y));
        var scale = Scale();
        var dx = (cursor.X - origin.X) / scale - _resizeOriginX;
        var dy = (cursor.Y - origin.Y) / scale - _resizeOriginY;
        var bounds = ResizeFrom(_resizeDirection.Value, dx, dy);
        _x = bounds.X; _y = bounds.Y; _width = bounds.Width; _height = bounds.Height;
        ApplyPosition();
    }

    private void Grip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_resizing) return;
        _resizing = false;
        ReleaseMouseCapture();
        SaveBounds();
    }

    // 窗口从某一条边缩放之后的样子。
    private BoxBounds ResizeFrom(BoxResizeDirection direction, double dx, double dy)
    {
        var x = _resizeOriginX; var y = _resizeOriginY;
        var width = _resizeOriginWidth; var height = _resizeOriginHeight;
        if (direction is BoxResizeDirection.East or BoxResizeDirection.NorthEast or BoxResizeDirection.SouthEast)
            width = Math.Max(BoxWindowSize.MinWidth, _resizeOriginWidth + dx);
        if (direction is BoxResizeDirection.West or BoxResizeDirection.NorthWest or BoxResizeDirection.SouthWest)
        {
            width = Math.Max(BoxWindowSize.MinWidth, _resizeOriginWidth - dx);
            x = _resizeOriginX + (_resizeOriginWidth - width);
        }
        if (direction is BoxResizeDirection.South or BoxResizeDirection.SouthEast or BoxResizeDirection.SouthWest)
            height = Math.Max(BoxWindowSize.MinHeight, _resizeOriginHeight + dy);
        if (direction is BoxResizeDirection.North or BoxResizeDirection.NorthEast or BoxResizeDirection.NorthWest)
        {
            height = Math.Max(BoxWindowSize.MinHeight, _resizeOriginHeight - dy);
            y = _resizeOriginY + (_resizeOriginHeight - height);
        }
        var bounds = new BoxBounds(x, y, width, height);
        // 大小吸附到图标的网格，图标才不会在格子里被切掉一半。
        if (_boxes.Settings.ResizeGridEnabled) bounds = BoxLayout.SnapToGrid(bounds, _boxes.Settings, direction);
        return bounds;
    }

    // 这一块屏幕的工作区，去掉任务栏，分组不会压在任务栏上。
    private BoxWorkArea WorkArea()
    {
        var (_, _, work, _) = WindowsHost.WindowScreen(this);
        return new BoxWorkArea(work.X, work.Y, work.Width, work.Height);
    }

    // 屏幕的缩放：鼠标位移是 DIP，落盘和吸附的尺寸也是 DIP。
    private double Scale()
    {
        var source = PresentationSource.FromVisual(this);
        return source is null ? 1 : VisualTreeHelper.GetDpi(this).DpiScaleX;
    }

    // 光标形状，看得出这条边能拖。
    private static Cursor CursorFor(BoxResizeDirection direction) => direction switch
    {
        BoxResizeDirection.East or BoxResizeDirection.West => Cursors.SizeWE,
        BoxResizeDirection.North or BoxResizeDirection.South => Cursors.SizeNS,
        BoxResizeDirection.NorthEast or BoxResizeDirection.SouthWest => Cursors.SizeNWSE,
        _ => Cursors.SizeNESW
    };

    // ---- 分组里的东西 ----

    // 把分组里的图标重新排一遍。
    public void Rebuild()
    {
        var settings = _boxes.Settings;
        _icons.Children.Clear();
        _icons.Width = Math.Max(0, _width - BoxWindowSize.GridPadding * 2);
        _icons.Margin = new Thickness(BoxWindowSize.GridPadding);

        // 真的文件和系统项在一张顺序表里，所以它们在分组里可以交错着排列。
        foreach (var key in _boxes.ItemKeys(_box.Id)) _icons.Children.Add(ItemView(key));

        _icons.ItemWidth = BoxLayout.ColumnWidth(settings);
        _icons.ItemHeight = BoxLayout.RowHeight(settings);
        _titleText.Text = _box.Title;
        _frame.CornerRadius = new CornerRadius(settings.CornerRadius);
    }

    // 一项的图标和名字。key 是它的路径，或者是 "shell::" 加它的编号。
    private FrameworkElement ItemView(string key)
    {
        var settings = _boxes.Settings;
        var isShell = BoxItemKey.IsShell(key);
        var path = isShell ? "" : key;
        var shellId = isShell ? BoxItemKey.ShellId(key) : "";
        var icon = isShell ? BoxIcons.ForShellItem(shellId) : BoxIcons.ForFile(path);
        var name = isShell ? BoxShellItems.DisplayName(shellId) : BoxIcons.DisplayName(path, settings.NameDisplay);

        var image = new Image
        {
            Source = icon,
            Width = settings.IconSize,
            Height = settings.IconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true
        };

        var iconHost = new Grid { Width = settings.IconSize, Height = settings.IconSize };
        iconHost.Children.Add(image);
        // 快捷方式在左下角带一个小箭头，桌面就是这样标的。
        if (!isShell && settings.ShowShortcutArrow && BoxNaming.IsShortcut(path))
        {
            iconHost.Children.Add(new Path
            {
                Data = Geometry.Parse("M 0,0 L 6,0 L 0,6 Z"),
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 0.5,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false
            });
        }

        var label = new TextBlock
        {
            Text = name,
            FontSize = settings.LabelTextSize,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = Math.Max(14, Math.Ceiling(settings.LabelTextSize * 1.32)) * 2,
            Width = settings.FilenameWidth,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false
        };

        var panel = new StackPanel { Width = settings.FilenameWidth, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(iconHost);
        if (settings.ShowItemLabels) panel.Children.Add(label);

        var host = new Border
        {
            Child = panel,
            Width = BoxLayout.ColumnWidth(settings),
            Height = BoxLayout.RowHeight(settings),
            Background = Brushes.Transparent,
            Tag = key
        };
        host.MouseLeftButtonDown += Icons_MouseLeftButtonDown;
        host.MouseRightButtonUp += Item_MouseRightButtonUp;
        return host;
    }

    // ---- 在分组里拖动 ----

    // 鼠标按下的那一项被拿起来。
    private void Icons_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is null) return;
        _dragOrigin = e.GetPosition(this);
        _dragging = false;
        _dragKey = tag;
    }

    private void Icons_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (_dragKey is null) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragging = true;
        var key = _dragKey;
        _cancelled = false;

        // 被拿起来的那一项跟着鼠标走。它只放在分组自己的标记里，不放在剪贴板格式里：放上去的话，Windows 桌面会当场把它
        // 收走，我们就来不及按「拖出」的规则处理它了。
        var data = new DataObject();
        data.SetData(BoxDrag.Format, key);
        data.SetData(BoxDrag.BoxId, _box.Id);

        // 拖动结束时，鼠标松开的地方告诉了我们它落在哪：还在某个分组里，那边就接住了；落在别的地方，就没有接收者，
        // 我们按「拖出」的规则把它放到桌面上。
        var dropped = DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
        _dragKey = null;
        _dragging = false;
        if (dropped == DragDropEffects.None && !_cancelled) DropOut(key);
    }

    // 拖动时按 Esc：这一项放回原处，什么也不做。
    private void Icons_QueryContinueDrag(object sender, QueryContinueDragEventArgs e)
    {
        // The drag is cancelled with the Escape key; anything else lets it go on.
        if (e.Action == DragAction.Cancel) _cancelled = true;
    }

    private void Icons_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) return;
        // 设置说单击就打开时，这里单击打开。
        if (_boxes.Settings.DoubleClickOpen) return;
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is null) return;
        OpenTag(tag);
    }

    // 双击打开。
    private void Area_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2 || !_boxes.Settings.DoubleClickOpen) return;
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is null) return;
        OpenTag(tag);
    }

    // 打开标记所指的那一项。
    private void OpenTag(string tag)
    {
        if (BoxItemKey.IsShell(tag)) BoxShell.OpenShellItem(BoxItemKey.ShellId(tag));
        else BoxShell.Open(tag);
    }

    // 在一项上按右键，打开它自己的菜单。
    private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is null) return;
        BoxItemMenu.Show(this, _boxes, _box, tag, Rebuild);
        e.Handled = true;
    }

    // 从源头元素往上找带标记的那一层，它就是被指着的那一项。
    private static string? TagOf(FrameworkElement? element)
    {
        while (element is not null && element.Tag is not string) element = element.Parent as FrameworkElement;
        return element?.Tag as string;
    }

    // ---- 拖放 ----

    private void Area_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = Accepts(e) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void Area_DragOver(object sender, DragEventArgs e)
    {
        if (!Accepts(e)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Move;
        // 落点按分组里的网格算：先找指针在哪一行，再在那一行里找它落在谁的前面。
        _insertion = ResolveInsertion(e.GetPosition(_icons));
        ShowDropMark(_insertion);
        e.Handled = true;
    }

    private void Area_DragLeave(object sender, DragEventArgs e) => _dropMark.Visibility = Visibility.Collapsed;

    // 拖进来的东西被收进分组。
    private void Area_Drop(object sender, DragEventArgs e)
    {
        _dropMark.Visibility = Visibility.Collapsed;
        if (!Accepts(e)) return;
        e.Handled = true;

        // 从别的分组里拖过来的：那一项要真的搬过来，搬的方式按「拖进」的规则。
        if (e.Data.GetDataPresent(BoxDrag.Format) && e.Data.GetData(BoxDrag.Format) is string key)
        {
            var sourceBoxId = e.Data.GetData(BoxDrag.BoxId) as string ?? "";
            if (string.Equals(sourceBoxId, _box.Id, StringComparison.Ordinal))
            {
                // 从自己这个分组里拖出来的，放下时只是换个位置，文件不动。
                MoveItem(key, _insertion);
                return;
            }
            MoveBetween(sourceBoxId, [key], _insertion);
            return;
        }

        // 系统项是引用，不是文件，收进来只是记下它的名字。
        if (e.Data.GetDataPresent("ShellId") && e.Data.GetData("ShellId") is string shellId)
        {
            _boxes.AddVirtualItem(_box.Id, shellId);
            Rebuild();
            return;
        }

        var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
        if (paths is not { Length: > 0 }) return;
        try
        {
            var landed = BoxTransferExecutor.Execute(paths, _box.FolderPath, _boxes.Settings.Drop, _boxes.Settings.Conflict);
            // 收进来的文件放在它被丢下的那一格。
            var order = _boxes.ItemKeys(_box.Id).ToList();
            foreach (var path in landed) order.Remove(path);
            var index = IndexFor(_insertion, order);
            order.InsertRange(index, landed);
            _boxes.SaveItemOrder(_box.Id, order);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("文件放入分组失败：", "The files could not be put into the box: ") + ex.Message);
        }
        Rebuild();
    }

    // 拖过来的东西，分组接不接。
    private bool Accepts(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(BoxDrag.Format)) return true;
        if (e.Data.GetDataPresent("ShellId")) return true;
        return e.Data.GetDataPresent(DataFormats.FileDrop);
    }

    // 落点算出来落在第几格。
    private int IndexFor(BoxInsertion insertion, IReadOnlyList<string> order)
    {
        if (insertion.TargetKey is not { } target) return order.Count;
        var index = order.ToList().FindIndex(key => string.Equals(key, target, StringComparison.Ordinal));
        if (index < 0) return order.Count;
        return insertion.After ? index + 1 : index;
    }

    // 把一项挪到落点那里：只改它在分组里的位置，文件不动。
    private void MoveItem(string key, BoxInsertion insertion)
    {
        if (insertion.TargetKey is not { } target) return;
        if (string.Equals(target, key, StringComparison.Ordinal)) return;
        _boxes.MoveItemTo(_box.Id, key, IndexFor(insertion, _boxes.ItemKeys(_box.Id)));
        Rebuild();
    }

    // 把别的那一项搬进这个分组。
    private void MoveBetween(string sourceBoxId, IReadOnlyList<string> keys, BoxInsertion insertion)
    {
        try
        {
            _boxes.MoveBetweenBoxes(sourceBoxId, _box.Id, keys, insertion.TargetKey, insertion.After);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("文件搬动失败：", "The files could not be moved: ") + ex.Message);
        }
        Rebuild();
        BoxWindows.Refresh(sourceBoxId);
    }

    // 拖到了分组外面：按「拖出」的规则把它放到桌面上——移动、复制，或者留一个快捷方式。
    private void DropOut(string key)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            _boxes.DragOut(_box.Id, [key], desktop);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("拖出失败：", "It could not be put on the desktop: ") + ex.Message);
        }
        Rebuild();
    }

    // 落点：分组里的每一项在网格中的位置，指针落在谁的前面或后面。
    internal BoxInsertion ResolveInsertion(Point point)
    {
        var rects = new List<BoxItemRect>();
        foreach (var child in _icons.Children)
        {
            if (child is not FrameworkElement element) continue;
            var tag = element.Tag as string ?? "";
            if (tag.Length == 0) continue;
            var origin = element.TranslatePoint(new Point(0, 0), _icons);
            rects.Add(new BoxItemRect(tag, origin.X, origin.Y, element.ActualWidth, element.ActualHeight));
        }
        var dragged = new HashSet<string>(StringComparer.Ordinal);
        if (_dragKey is not null) dragged.Add(_dragKey);
        return Core.BoxInsertionResolver.Resolve(rects, point.X, point.Y, dragged, _boxes.Settings.IconGapY);
    }

    // 那条指示落点的竖线。
    internal void ShowDropMark(BoxInsertion insertion)
    {
        if (insertion.TargetKey is not { } target) { _dropMark.Visibility = Visibility.Collapsed; return; }
        foreach (var child in _icons.Children)
        {
            if (child is not FrameworkElement element || (element.Tag as string) != target) continue;
            var origin = element.TranslatePoint(new Point(0, 0), _icons);
            var left = origin.X;
            // 落在后面时，竖线画在这一项的右边。
            if (insertion.After) left += element.ActualWidth;
            // 竖线是图标区旁边的那个元素，图标区自己有一圈边距，所以这里要把边距加上，
            // 竖线才真的贴在那一个图标的边上，而不是偏出去一截。
            _dropMark.Margin = new Thickness(left + _icons.Margin.Left - 1, origin.Y + _icons.Margin.Top, 0, 0);
            // 竖线只跟这一项一样高，跟整块图标区一样高就看不出它指的是哪一项。
            _dropMark.Height = element.ActualHeight;
            _dropMark.Visibility = Visibility.Visible;
            return;
        }
        _dropMark.Visibility = Visibility.Collapsed;
    }

    // ---- 空闲淡出 ----

    // 淡出到设定的不透明度。
    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _idleTimer.Stop();
        var opacity = Math.Clamp(_box.TitleOpacity, 0, 100) / 100d;
        if (!Soft.Enabled) { _frame.Opacity = opacity; return; }
        var fade = Soft.Fade(1, opacity, Math.Max(1, _boxes.Settings.IdleOpacityHideMs), Soft.Out);
        fade.FillBehavior = FillBehavior.Stop;
        _frame.BeginAnimation(OpacityProperty, fade);
        if (opacity <= 0) _frame.Visibility = Visibility.Hidden;
    }

    // 鼠标进到窗口上，它又完全显出来。
    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _idleTimer.Stop();
        _frame.Visibility = Visibility.Visible;
        if (Soft.Enabled)
        {
            var fade = Soft.Fade(_frame.Opacity, 1, Math.Max(1, _boxes.Settings.IdleOpacityShowMs), Soft.Out);
            fade.FillBehavior = FillBehavior.Stop;
            _frame.BeginAnimation(OpacityProperty, fade);
        }
        else _frame.Opacity = 1;
        base.OnMouseEnter(e);
    }

    // 鼠标离开窗口一会儿，它淡下去。
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _idleTimer.Stop();
        _idleTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, _boxes.Settings.IdleOpacityHideMs));
        _idleTimer.Start();
        base.OnMouseLeave(e);
    }
}
