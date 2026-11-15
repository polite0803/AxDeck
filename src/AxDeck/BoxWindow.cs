using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;
// 这个文件里也有 System.Windows.Shapes.Path（画图形用的），它会把 System.IO.Path 挡住，
// 所以文件路径这块用别名单独写清楚。
using IOFile = System.IO.File;
using IODirectory = System.IO.Directory;
using IOPath = System.IO.Path;

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

    // 选中的那几项：真的文件是它的路径，系统项是 "shell::" 加它的编号。
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    // 框选的那块地方：按下鼠标的地方，和此刻鼠标所在的地方。
    private Point? _selectionStart;
    private Point _selectionCurrent;
    // 框选的那块地方画在哪：它盖在图标区上面，跟着鼠标走。
    private readonly Border _selectionRect;
    // 正在改名字的那一项，以及输入框里的草稿。
    private string? _editingKey;
    private TextBox? _renameInput;
    // 待定的改名：单击已选中的那一项时先等一等，看用户是不是要双击打开。
    private DispatcherTimer? _renameTimer;
    // 键盘焦点要落在图标区上，F2、Delete、Ctrl+C 这些键才收得到。
    private Grid? _fileArea;

    // 单击已选中的那一项之后，等这么久再进改名（原版是 520 毫秒）。
    private const int RenameClickDelayMs = 520;

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
        // 分组是浮在桌面上的，不置顶就会被桌面的图标和别的窗口压住，根本点不到。
        Topmost = true;
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

        // 图标区：接收拖放的那一块。
        // 它的背景必须是透明而不是没有背景：WPF 里没有背景的面板不参与命中测试，鼠标在空白处落下时命中的是它上面的
        // 那层边框，而那块边框不在图标区的祖先链里，拖放事件就永远到不了这里——空的分组因此什么都接不住。
        // Focusable：键盘要落在这一块上，Delete、F2、Ctrl+C 这些键才收得到（原版也是把焦点放在文件网格上）。
        var area = new Grid { AllowDrop = true, Background = Brushes.Transparent, Focusable = true };
        _fileArea = area;
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
        // 空白处按下鼠标是框选，和图标上的点击、标题上的拖动、边缘上的缩放各走各的。
        area.PreviewMouseLeftButtonDown += Area_PreviewMouseLeftButtonDown;
        area.MouseMove += Area_MouseMove;
        area.MouseLeftButtonUp += Area_SelectionMouseLeftButtonUp;
        // 空白处按右键：把选中项挪到桌面、或者从分组里移出去。
        // 这两个动作是分组自己才有的，系统的原生菜单里没有，所以放在这里。
        area.MouseRightButtonUp += Area_MouseRightButtonUp;
        // 键盘：F2 改名、Delete 删除、Enter 打开、Ctrl+C/X/V 走 Windows 的文件剪贴板。
        // 挂在窗口上而不是图标区上：键盘事件从焦点元素往上冒泡，挂在窗口上一准收得到，
        // 不管焦点此刻在不在图标区上（原版也是挂在 window 上，useBoxWindowLifecycle.ts）。
        KeyDown += Area_KeyDown;
        content.Children.Add(area);

        // 框选画出来的那块地方：跟着鼠标走，盖在图标上面，但不接鼠标（它只是让人看见选了哪一块）。
        _selectionRect = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 60, 120, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(40, 60, 120, 255)),
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        area.Children.Add(_selectionRect);

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
        // 标题的位置可能刚被菜单改过，重排的时候一起摆好。
        ApplyTitlePosition();
    }

    // 标题在上还是在下。菜单里能改这一项，所以这里每次重排都按它摆一次。
    private void ApplyTitlePosition()
    {
        var area = _fileArea;
        if (area is null) return;

        // 标题在下面时，图标区挪到上面那行去，标题占下面那行。
        var titleRow = _box.TitlePosition == BoxTitlePosition.Bottom ? 1 : 0;
        Grid.SetRow(_title, titleRow);
        Grid.SetRow(area, titleRow == 0 ? 1 : 0);
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
        host.MouseLeftButtonUp += Icons_MouseLeftButtonUp;
        host.MouseRightButtonUp += Item_MouseRightButtonUp;
        return host;
    }

    // ---- 选区 ----

    // 图标区里按下鼠标：点在图标上的是选中或者拖动，点在空白处的是框选。
    private void Area_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 改名的时候不打断用户。
        if (_editingKey is not null) return;
        // 键盘要落在这一块上，Delete、F2、Ctrl+C 这些键才收得到（原版也是这样：点一下就把焦点给文件网格）。
        FocusFileArea();
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is not null)
        {
            // 按在图标上：普通点击只留这一项，按住 Ctrl 则增删这一项。
            // 双击的第二次点击不再改选区（原版：detail > 1 就直接返回）。
            if (e.ClickCount > 1) return;
            var next = Core.BoxSelection.AfterClick([.. _selected], tag, (Keyboard.Modifiers & ModifierKeys.Control) != 0);            _selected.Clear();
            foreach (var key in next) _selected.Add(key);
            PaintSelection();

            // 单击的是已经选中的那一项，而且设置说双击才打开：这时延迟一会儿再进改名，
            // 因为这一下很可能是双击的前一下。Explorer 也是这样：单击已选中的名字会变成可编辑。
            if (Core.BoxSelection.ShouldRenameOnClick(_selected, tag, _boxes.Settings.DoubleClickOpen))
                ScheduleRename(tag);
            return;
        }

        // 按在空白处：开始框选。按住 Ctrl 时是把框到的加进原来的选区。
        var point = e.GetPosition(_icons);
        _selectionStart = point;
        _selectionCurrent = point;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) ClearSelection();
        _icons.CaptureMouse();
        e.Handled = true;
    }

    // 延迟一会儿再进改名：单击已选中的那一项时用的。
    // 这段时间里如果用户又点了别的、或者按了 F2、Delete、Escape，这次改名就取消。
    private void ScheduleRename(string key)
    {
        CancelScheduledRename();
        _renameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenameClickDelayMs) };
        _renameTimer.Tick += (_, _) =>
        {
            CancelScheduledRename();
            // 这段时间里选区变了，或者已经有别的改名在进行，就不改名了。
            if (_editingKey is not null) return;
            if (_selected.Count != 1 || !_selected.Contains(key)) return;
            StartRename(key);
        };
        _renameTimer.Start();
    }

    // 取消那个待定的改名。
    private void CancelScheduledRename()
    {
        if (_renameTimer is null) return;
        _renameTimer.Stop();
        _renameTimer = null;
    }

    // 把键盘焦点放到图标区上，键盘快捷键才收得到。
    private void FocusFileArea()
    {
        if (_fileArea is null) return;
        if (!_fileArea.IsFocused) _fileArea.Focus();
    }

    // 框选的时候跟着鼠标走，把框到的图标都选上。
    private void Area_MouseMove(object sender, MouseEventArgs e)
    {
        if (_selectionStart is not { } start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { StopSelection(); return; }
        _selectionCurrent = e.GetPosition(_icons);
        ApplySelection();
    }

    // 松开鼠标：框选结束。
    private void Area_SelectionMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectionStart is null) return;
        ApplySelection();
        StopSelection();
    }

    // 框选结束：那块地方收起来，鼠标的捕获也放开。
    private void StopSelection()
    {
        _selectionStart = null;
        _selectionRect.Visibility = Visibility.Collapsed;
        if (_icons.IsMouseCaptured) _icons.ReleaseMouseCapture();
    }

    // 把框到的图标选上，并把那块地方画出来。
    private void ApplySelection()
    {
        if (_selectionStart is not { } start) return;
        var rect = Core.BoxSelection.Normalize(new Core.BoxSelection.Point(start.X, start.Y),
            new Core.BoxSelection.Point(_selectionCurrent.X, _selectionCurrent.Y));
        var touched = TouchedItems(rect);
        foreach (var key in touched) _selected.Add(key);
        PaintSelection();

        // 框选用的是图标区自己的坐标，而那块画出来的地方是挂在图标区外面那一层上的，
        // 两者差着图标区的那圈边距，所以画之前先把坐标换算过去，不然框画得偏出去一截。
        var host = _selectionRect.Parent as UIElement;
        var offset = host is null ? new Point(0, 0) : _icons.TranslatePoint(new Point(0, 0), host);
        _selectionRect.Margin = new Thickness(rect.Left + offset.X, rect.Top + offset.Y, 0, 0);
        _selectionRect.Width = rect.Width;
        _selectionRect.Height = rect.Height;
        _selectionRect.Visibility = Visibility.Visible;
    }

    // 框到的那些图标。
    private IReadOnlyList<string> TouchedItems(Core.BoxSelection.Rect rect)
    {
        var items = new List<(string Key, Core.BoxSelection.Rect Rect)>();
        foreach (var child in _icons.Children)
        {
            if (child is not FrameworkElement element) continue;
            var tag = element.Tag as string ?? "";
            if (tag.Length == 0) continue;
            var origin = element.TranslatePoint(new Point(0, 0), _icons);
            items.Add((tag, new Core.BoxSelection.Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight)));
        }
        return Core.BoxSelection.ItemsIn(rect, items);
    }

    // 清空选区。
    private void ClearSelection()
    {
        _selected.Clear();
        CancelScheduledRename();
        PaintSelection();
    }

    // 按选中与否把图标画出来。
    private void PaintSelection()
    {
        foreach (var child in _icons.Children)
        {
            if (child is not FrameworkElement element) continue;
            var tag = element.Tag as string ?? "";
            var chosen = _selected.Contains(tag);
            if (element is Border border)
            {
                // 选中的那一项底下有一层蓝，鼠标在它上面时看得出选的是哪几项。
                border.Background = chosen ? new SolidColorBrush(Color.FromArgb(70, 60, 120, 255)) : Brushes.Transparent;
                border.BorderBrush = chosen ? new SolidColorBrush(Color.FromArgb(200, 80, 140, 255)) : Brushes.Transparent;
                border.BorderThickness = new Thickness(chosen ? 1 : 0);
            }
        }
    }

    // ---- 键盘 ----

    // 文件区的键盘：跟 Explorer 一样的那几个键。
    private void Area_KeyDown(object sender, KeyEventArgs e)
    {
        // 改名的时候不打断用户。
        if (_editingKey is not null) return;
        // 焦点在改名输入框里时，那几个键是输入框自己的，别抢。
        if (e.OriginalSource is TextBox) return;
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var selected = SelectedKeys();

        // 原版在每一个快捷键之前都先取消那个待定的改名：用户改了主意，就不该再改名了。
        CancelScheduledRename();

        // 这个键在这一块里是什么意思：跟原版的分支一一对应（原版 useBoxFileSelection.ts 的 handleFileViewKeydown）。
        var command = Core.BoxKeyboard.For(e.Key.ToString(), control);
        switch (command)
        {
            case Core.BoxKeyboard.Command.SelectAll:
                _selected.Clear();
                foreach (var key in _boxes.ItemKeys(_box.Id)) _selected.Add(key);
                PaintSelection();
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Copy:
                CopySelected(ShellClipboard.Operation.Copy);
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Cut:
                CopySelected(ShellClipboard.Operation.Move);
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Paste:
                PasteClipboard();
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Rename:
                StartRename(FirstRenameable());
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Delete:
                DeleteSelected(selected);
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Open when selected.Count > 0:
                OpenTag(selected[0]);
                e.Handled = true;
                return;
            case Core.BoxKeyboard.Command.Clear:
                ClearSelection();
                e.Handled = true;
                return;
        }
    }

    // 选中的那几项，按分组里的顺序排。
    private IReadOnlyList<string> SelectedKeys()
    {
        var order = _boxes.ItemKeys(_box.Id);
        return [.. order.Where(key => _selected.Contains(key))];
    }

    // 要改名的是哪一项：选中的第一项里第一个能改名的。
    // 系统项（这台电脑、回收站这些）没有自己的文件名，改不了名，所以要跳过它们——
    // 原版也是这么挑的（useBoxFileActions.ts 的 startSelectedItemRename）。
    private string? FirstRenameable()
        => SelectedKeys().FirstOrDefault(key => !BoxItemKey.IsShell(key));

    // 把选中的那几项写到 Windows 的文件剪贴板上。
    private void CopySelected(ShellClipboard.Operation operation)
    {
        var paths = SelectedKeys().Where(key => !BoxItemKey.IsShell(key)).ToList();
        if (paths.Count == 0) return;
        if (ShellClipboard.Write(paths, operation))
        {
            // 剪切的项在贴走之前画成虚的，让人看得出它们等着被搬走。
            if (operation == ShellClipboard.Operation.Move) PaintSelection();
        }
        else BoxReport.Error(Loc.T("剪贴板写不进去。", "The clipboard could not be written to."));
    }

    // 把剪贴板上的文件贴进这个分组。剪过的是搬过来，复制过的是复制过来。
    private void PasteClipboard()
    {
        var (paths, operation) = ShellClipboard.Read();
        if (paths.Count == 0) return;
        try
        {
            var action = operation == ShellClipboard.Operation.Move ? BoxDropAction.Move : BoxDropAction.Copy;
            var landed = BoxTransferExecutor.Execute(paths, _box.FolderPath, action, _boxes.Settings.Conflict);
            var order = _boxes.ItemKeys(_box.Id).ToList();
            foreach (var path in landed) order.Remove(path);
            order.InsertRange(order.Count, landed);
            _boxes.SaveItemOrder(_box.Id, order);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("文件粘贴失败：", "The files could not be pasted: ") + ex.Message);
        }
        Rebuild();
    }

    // 删掉选中的那几项：真文件进回收站，系统项只是把它从分组里拿掉。
    private void DeleteSelected(IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (BoxItemKey.IsShell(key)) _boxes.RemoveVirtualItem(_box.Id, BoxItemKey.ShellId(key));
            else _boxes.RemoveItem(_box.Id, key);
        }
        ClearSelection();
        Rebuild();
    }

    // ---- 改名 ----

    // 给某一项改名：它的名字变成一个可以编辑的框。
    private void StartRename(string? key)
    {
        if (key is null) return;
        if (BoxItemKey.IsShell(key)) return;
        if (!IOFile.Exists(key) && !IODirectory.Exists(key)) return;        // 真的开始改了，那个待定的改名就不算了。
        CancelScheduledRename();

        _editingKey = key;
        var host = FindItem(key);
        if (host is not Border border) { _editingKey = null; return; }
        var panel = border.Child as StackPanel;
        if (panel is null) { _editingKey = null; return; }

        // 名字那一行换成输入框，光标落在里面，原来的名字被选中：Explorer 就是这样改名的。
        var label = panel.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is null) { _editingKey = null; return; }
        var index = panel.Children.IndexOf(label);
        var input = new TextBox
        {
            Text = IOPath.GetFileNameWithoutExtension(key),
            FontSize = _boxes.Settings.LabelTextSize,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(170, 20, 20, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(200, 90, 150, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
            Height = Math.Max(18, _boxes.Settings.LabelTextSize * 1.5),
            TextAlignment = TextAlignment.Center
        };
        panel.Children.Remove(label);
        panel.Children.Insert(index, input);
        _renameInput = input;
        input.SelectAll();
        input.Focus();
        input.KeyDown += Rename_KeyDown;
        input.LostKeyboardFocus += (_, _) => CommitRename();
    }

    // 改名框里按回车是确认，按 Esc 是取消。
    private void Rename_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        else if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    // 确认改名：文件真的改名字，分组里的顺序也跟着换。
    private void CommitRename()
    {
        var key = _editingKey;
        var input = _renameInput;
        if (key is null || input is null) return;
        var name = input.Text.Trim();
        // 名字里没有扩展名时，把原来的扩展名加回去。
        var extension = IOPath.GetExtension(key);
        if (name.Length > 0 && extension.Length > 0 && !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name += extension;
        ClearRename();
        if (name.Length == 0) { Rebuild(); return; }
        if (!_boxes.Rename(_box.Id, key, name))
        {
            BoxReport.Error(Loc.T("这个名字用不了：Windows 不收这个名字，或者已经有同名的东西了。",
                "This name cannot be used: Windows does not take it, or something of that name is already there."));
        }
        Rebuild();
    }

    // 取消改名：名字不动，只把输入框收起来。
    private void CancelRename()
    {
        ClearRename();
        Rebuild();
    }

    // 把改名框收起来，换回普通的名字。
    private void ClearRename()
    {
        _editingKey = null;
        _renameInput = null;
    }

    // 找图标区里带这个标记的那一项。
    private Border? FindItem(string key)
        => _icons.Children.OfType<Border>().FirstOrDefault(child => child.Tag as string == key);

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

    // 鼠标在图标上松开：单击打开（设置说双击打开时不开），或者双击打开。
    private void Icons_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging) return;
        // 改名的时候不打开。
        if (_editingKey is not null) return;
        var tag = TagOf(e.OriginalSource as FrameworkElement);
        if (tag is null) return;

        if (_boxes.Settings.DoubleClickOpen)
        {
            // 双击打开。第二下点下去时，那个待定的改名就不该再发生了。
            if (e.ClickCount < 2) return;
            CancelScheduledRename();
        }
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

        // 右键先修正选区：不在选区里的那一项成为唯一选中的一项，跟 Explorer 一样。
        if (!_selected.Contains(tag)) { _selected.Clear(); _selected.Add(tag); PaintSelection(); }

        BoxItemMenu.Show(this, _boxes, _box, tag, Rebuild, e.GetPosition(this));
        e.Handled = true;
    }

    // 在空白处按右键：把选中的那几项挪到桌面上，或者从分组里移出去。
    // 系统给的那份菜单里没有这两样——它们是分组自己才有的动作，所以单独放在这里。
    private void Area_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 按在图标上的那一条，交给图标自己的菜单。
        if (TagOf(e.OriginalSource as FrameworkElement) is not null) return;

        var selected = SelectedKeys();
        if (selected.Count == 0) return;

        var menu = BoxMenu.Build();
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

        // 真文件可以挪到桌面上；系统项没有自己的文件，挪不了。
        var files = selected.Where(key => !BoxItemKey.IsShell(key)).ToList();
        if (files.Count > 0)
        {
            menu.Items.Add(BoxMenu.Item(Loc.T("拖出到桌面", "Put on the desktop"), () =>
            {
                try
                {
                    _boxes.DragOut(_box.Id, files, desktop);
                    ClearSelection();
                    Rebuild();
                }
                catch (Exception ex)
                {
                    BoxReport.Error(Loc.T("拖出失败：", "It could not be put on the desktop: ") + ex.Message);
                }
            }, BoxMenu.MenuIcons.Desktop));
        }

        // 从分组里移出去：真文件是从分组里拿掉，系统项是把它的桌面图标放回去。
        menu.Items.Add(BoxMenu.Item(Loc.T("从分组移出", "Take out of the box"), () =>
        {
            foreach (var key in selected)
            {
                if (BoxItemKey.IsShell(key)) _boxes.RemoveVirtualItem(_box.Id, BoxItemKey.ShellId(key));
                else _boxes.RemoveItem(_box.Id, key);
            }
            ClearSelection();
            Rebuild();
        }, BoxMenu.MenuIcons.Remove));

        BoxMenu.Open(this, menu);
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
        // 淡出只改透明度，不改可见性。隐藏的元素不参与命中测试，整个窗口在桌面上就「不存在」了：
        // Windows 的拖放靠命中测试找接收者，找不到就永远不会发 DragEnter，鼠标也永远进不来，
        // 分组既显不出来也接不住东西。Dasktop 原版用的是 CSS opacity，pointer-events 不受影响。
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
