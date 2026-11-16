using System.Collections.Specialized;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 「桌面」页：把桌面上的东西以图标画在展开的面板里。
//
// AxDeck 运行时真实桌面的图标是藏起来的（MainWindow.Desktop.cs 的接管），所以这一页就是桌面本身：
// 双击打开、右键弹的是 Explorer 自己那份菜单（压缩、发送到、用别的程序打开都在里面）、
// 从资源管理器拖文件进来就落到桌面上，从这里拖出去就能交给别的程序。
// 程序退出时桌面图标放回去，跟原来一模一样。
internal sealed class DesktopIconsView : Grid
{
    // 这一页的底色：几乎是透明的黑，不是全透明。
    //
    // AxDeck 的面板是分层窗口，而 Windows 对分层窗口只让画了东西的像素接住点击——完全透明（alpha=0）的
    // 像素它直接放过去，点击落到下面那个窗口上，WPF 连消息都收不到。WPF 自己的命中测试不看这个，所以
    // 全透明的面板在命中测试里好好的，真点下去却毫无反应：点图标图以外的地方（名字上、格子的空处、
    // 两格之间）全部落空，单击选不中、双击打不开。
    // alpha 只留 1 就够了，肉眼看不出来；面板折叠时的悬停条（MainWindow.xaml 的 HoverExtension）也是这么做的。
    private static readonly Brush Surface = Frozen(new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)));

    private static Brush Frozen(Brush brush) { brush.Freeze(); return brush; }

    private readonly MainWindow _owner;
    private readonly BoxIconGrid _icons;
    private readonly Grid _area;
    private readonly Border _dropMark;
    private readonly TextBlock _empty;

    // 现在画着的这些项，按画的顺序。拖进来时算落在哪一格要用它。
    private IReadOnlyList<DesktopItems.Item> _items = [];
    // 上一次画的时候是什么内容：一样就不重画，免得每次展开都把图标重新取一遍。
    private string _key = "";
    // 选中的那几项，按它们的路径（系统项按解析名）。
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    // 鼠标按下的那一项和位置：移动超过系统的拖动阈值，这一下就成了拖出去。
    private string? _pressPath;
    private Point _pressAt;
    private bool _dragging;
    // 系统菜单开着：这期间面板不能收起来，否则菜单跟着一起没了。
    private bool _menuOpen;

    public DesktopIconsView(MainWindow owner)
    {
        _owner = owner;
        // 背景必须有，而且不能是全透明：没有背景的面板不参与命中测试，全透明的像素又会被分层窗口放过去
        // （见 Surface）。两件事同时满足，空白处的点击和拖放才到得了这一页。
        Background = Surface;
        Focusable = true;
        AllowDrop = true;

        _icons = new BoxIconGrid
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Surface
        };

        // 拖进来的文件会落在哪一格，由这条竖线指示。它靠左靠上排：不定对齐的话 Grid 会把它摆在中间。
        _dropMark = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 120, 170, 255)),
            Width = 2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        _area = new Grid { Background = Surface, AllowDrop = true };
        _area.Children.Add(_icons);
        _area.Children.Add(_dropMark);

        Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _area,
            Background = Surface
        });

        // 桌面上什么也没有时写的一句话，盖在图标区上，不接鼠标。
        _empty = UI.Fixed(Loc.T("桌面上什么也没有。把文件拖到这里，它就会落到桌面上。",
            "The desktop is empty. Drop a file here and it lands on the desktop."), 12.5, 20, UI.Tertiary);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.IsHitTestVisible = false;
        _empty.Visibility = Visibility.Collapsed;
        Children.Add(_empty);

        // 鼠标：图标上单击选中、双击打开、按住拖出去；空白处点一下清掉选区；右键弹菜单。
        _icons.PreviewMouseLeftButtonDown += Icons_MouseLeftButtonDown;
        _icons.PreviewMouseMove += Icons_MouseMove;
        _icons.MouseLeftButtonUp += Icons_MouseLeftButtonUp;
        _icons.MouseRightButtonUp += Item_MouseRightButtonUp;
        _area.MouseLeftButtonDown += Area_MouseLeftButtonDown;
        _area.MouseRightButtonUp += Area_MouseRightButtonUp;

        // 拖放：从资源管理器拖文件进来，落到桌面上。
        _area.DragEnter += Area_DragOver;
        _area.DragOver += Area_DragOver;
        _area.DragLeave += (_, _) => _dropMark.Visibility = Visibility.Collapsed;
        _area.Drop += Area_Drop;

        // 键盘挂在页面本身上：Grid 默认不接焦点，上面设了 Focusable，点一下就把焦点收过来。
        KeyDown += View_KeyDown;
    }

    // 系统菜单开着的时候，面板不能收起来。
    public bool MenuOpen => _menuOpen;

    // ---- 画 ----

    // 重画一遍。force 为真时不管内容有没有变都重画（改了外观、改了名字显示方式）。
    public void Refresh(bool force = false)
    {
        var items = ShellDesktop.Items();
        var look = Look;
        var key = string.Join("\n", items.Select(item => item.Path + "|" + item.Name))
            + "\n" + look.IconSize + "|" + look.LabelTextSize + "|" + look.IconGapX + "|" + look.IconGapY
            + "|" + look.ShowItemLabels + "|" + look.ShowShortcutArrow + "|" + look.NameDisplayMode
            + "|" + look.CornerRadius + "|" + PanelTheme.Generation;
        if (!force && key == _key && _icons.Children.Count > 0) return;
        _key = key;
        _items = items;

        _icons.CellWidth = BoxLayout.ColumnWidth(look);
        _icons.CellHeight = BoxLayout.RowHeight(look);
        _icons.ColumnGap = BoxLayout.ColumnGap(look);
        _icons.RowGap = BoxLayout.RowGap(look);

        _icons.Children.Clear();
        foreach (var item in items) _icons.Children.Add(ItemView(item, look));
        // 选区里那些已经不在了的项去掉：文件被删掉之后不该还留着选中状态。
        _selected.IntersectWith(items.Select(item => item.Path));
        PaintSelection();
        _empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 图标的大小、间距、名字怎么写：在设置窗口的「桌面页」里调，这一页照着画。
    private DesktopLookSettings Look => _owner.Boxes.Settings;

    // 一项的图标和名字。
    private FrameworkElement ItemView(DesktopItems.Item item, DesktopLookSettings look)
    {
        bool shell = item.Kind == DesktopItems.Kind.Shell;
        var icon = shell ? BoxIcons.ForShellItem(ShellIdOf(item.Path)) : BoxIcons.ForFile(item.Path);
        var name = shell ? item.Name : BoxIcons.DisplayName(item.Path, look.NameDisplay);

        var iconHost = new Grid { Width = look.IconSize, Height = look.IconSize, HorizontalAlignment = HorizontalAlignment.Center };
        iconHost.Children.Add(new Image
        {
            Source = icon,
            Width = look.IconSize,
            Height = look.IconSize,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            SnapsToDevicePixels = true
        });
        // 快捷方式在左下角带一个小箭头，桌面就是这样标的。
        if (!shell && look.ShowShortcutArrow && item.Kind == DesktopItems.Kind.Shortcut)
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

        var panel = new StackPanel { Width = look.FilenameWidth, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(iconHost);
        if (look.ShowItemLabels)
        {
            panel.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = look.LabelTextSize,
                Foreground = UI.Brush(UI.Primary),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = Math.Max(14, Math.Ceiling(look.LabelTextSize * 1.32)) * 2,
                Width = look.FilenameWidth,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, DesktopIconView.LabelGap, 0, 0),
                IsHitTestVisible = false
            });
        }

        var host = new Border
        {
            Child = panel,
            Width = BoxLayout.ColumnWidth(look),
            Height = BoxLayout.RowHeight(look),
            Background = Surface,
            CornerRadius = new CornerRadius(Math.Clamp(look.CornerRadius, 0, 12)),
            Tag = item.Path,
            ToolTip = item.Path
        };
        AutomationProperties.SetName(host, name);
        return host;
    }

    // 系统项的路径或解析名换回它的编号，取图标要用编号。用户文件夹给的是它自己的 Profile 路径，
// 认它得比一次路径，所以走 FromPath 而不是只按解析名查。
    private static string ShellIdOf(string pathOrParsingName)
        => BoxShellItems.FromPath(pathOrParsingName) ?? "";

    // ---- 选区 ----

    private void PaintSelection()
    {
        foreach (var child in _icons.Children.OfType<Border>())
        {
            bool chosen = child.Tag is string path && _selected.Contains(path);
            // 没选中时用 Surface 而不是全透明：全透明的那一块会被分层窗口让点击穿过去，
            // 图标图以外的地方（名字上、格子上下留的空）就点不着了。
            child.Background = chosen ? new SolidColorBrush(Color.FromArgb(60, 120, 170, 255)) : Surface;
            child.BorderBrush = chosen ? new SolidColorBrush(Color.FromArgb(150, 120, 170, 255)) : null;
            child.BorderThickness = new Thickness(chosen ? 1 : 0);
        }
    }

    private static string? TagOf(FrameworkElement? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current) as FrameworkElement)
            if (current.Tag is string path) return path;
        return null;
    }

    private DesktopItems.Item? ItemAt(string path)
    {
        foreach (var item in _items)
            if (string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)) return item;
        return null;
    }

    private int IndexOf(string path)
    {
        for (int i = 0; i < _items.Count; i++)
            if (string.Equals(_items[i].Path, path, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // ---- 鼠标 ----

    private void Icons_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (TagOf(e.OriginalSource as FrameworkElement) is not { } path) return;
        // Ctrl 加选、Shift 连选，跟资源管理器一样；不按修饰键时点谁选谁。
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (!_selected.Remove(path)) _selected.Add(path);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selected.Count > 0)
        {
            int from = IndexOf(_selected.First()), to = IndexOf(path);
            if (from >= 0 && to >= 0)
            {
                _selected.Clear();
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _selected.Add(_items[i].Path);
            }
        }
        else if (!_selected.Contains(path)) { _selected.Clear(); _selected.Add(path); }
        PaintSelection();

        _pressAt = e.GetPosition(this);
        _pressPath = path;
        _dragging = false;
    }

    // 按住往外拖：移动超过系统认的那点距离，就把选中的文件交给系统去拖，可以拖进资源管理器、别的程序里。
    private void Icons_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressPath is not { } path) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // 系统项（此电脑、回收站）没有自己的文件，拖不出去。
        var files = _selected.Where(p => ItemAt(p) is { Kind: not DesktopItems.Kind.Shell }).ToArray();
        _pressPath = null;
        if (files.Length == 0) return;

        _dragging = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, files);
            var collection = new StringCollection();
            collection.AddRange(files);
            data.SetFileDropList(collection);
            // 只给复制：文件还在桌面上，这一页只是把它交出去，不是从这里搬走。
            DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally { _dragging = false; }
    }

    private void Icons_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressPath = null;
        if (_dragging) return;
        if (TagOf(e.OriginalSource as FrameworkElement) is not { } path) return;
        if (ItemAt(path) is not { } item) return;
        // 双击打开。桌面就是这个规矩：点一下选中，点两下打开。
        if (e.ClickCount < 2) return;
        Open(item);
        e.Handled = true;
    }

    private void Area_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        // 按在图标上：这一下属于图标（选中、双击、往外面拖）。这里只是冒泡到图标区的父级，
        // 不能把图标刚记下的「按在哪一项上」清掉——清掉了，拖动就永远起不来。
        if (TagOf(e.OriginalSource as FrameworkElement) is not null) return;
        _pressPath = null;
        _selected.Clear();
        PaintSelection();
    }

    // 在一项上按右键：弹 Explorer 自己那份菜单。
    private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TagOf(e.OriginalSource as FrameworkElement) is not { } path) return;
        if (!_selected.Contains(path)) { _selected.Clear(); _selected.Add(path); PaintSelection(); }
        ShowItemMenu(path, e.GetPosition(this));
        e.Handled = true;
    }

    // 在空白处按右键：这一页自己的菜单。
    private void Area_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (TagOf(e.OriginalSource as FrameworkElement) is not null) return;
        var menu = BoxMenu.Build();
        menu.Items.Add(BoxMenu.Item(Loc.T("刷新", "Refresh"), () => Refresh(force: true), BoxMenu.MenuIcons.Rollup));
        menu.Items.Add(BoxMenu.Item(Loc.T("打开桌面文件夹", "Open the desktop folder"), () =>
        {
            var folder = ShellDesktop.FolderPath();
            if (folder.Length == 0) return;
            try { BoxShell.Open(folder); }
            catch (Exception ex) { BoxReport.Error(Loc.T("无法打开桌面文件夹：", "The desktop folder could not be opened: ") + ex.Message); }
        }, BoxMenu.MenuIcons.Folder));
        menu.Items.Add(BoxMenu.Separator());
        menu.Items.Add(BoxMenu.Item(Loc.T("桌面页设置", "Desktop page settings"), () => _owner.BoxesOpenPage(), BoxMenu.MenuIcons.Desktop));
        BoxMenu.Open(_owner, menu);
        e.Handled = true;
    }

    // 一项的菜单：整段都是 Explorer 自己那份，压缩、发送到、用别的程序打开都在里面。
    private void ShowItemMenu(string path, Point position)
    {
        if (ItemAt(path) is not { } item) return;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(_owner).Handle;
        if (hwnd == IntPtr.Zero) return;
        var screen = _owner.PointToScreen(position);
        _menuOpen = true;
        try
        {
            // 菜单是阻塞的：它开在桌面上，直到用户选一项或把它关掉。这期间面板不能收起来。
            var ran = ShellContextMenu.Show(hwnd, item.Path, (int)screen.X, (int)screen.Y);
            // 菜单里做的事可能改了文件（改名、删除），所以重画一遍。
            if (ran) Refresh(force: true);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("系统菜单打不开：", "The system menu could not be opened: ") + ex.Message);
        }
        finally { _menuOpen = false; }
    }

    // ---- 键盘 ----

    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when _selected.Count > 0:
                foreach (var path in _selected.ToList())
                    if (ItemAt(path) is { } item) Open(item);
                e.Handled = true;
                break;
            case Key.Delete when _selected.Count > 0:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.F5:
                Refresh(force: true);
                e.Handled = true;
                break;
            case Key.A when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                _selected.Clear();
                foreach (var item in _items) _selected.Add(item.Path);
                PaintSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                _selected.Clear();
                PaintSelection();
                e.Handled = true;
                break;
        }
    }

    // 删掉选中的那几项：进回收站，跟桌面上按 Delete 一样。系统项没有文件，删不了。
    private void DeleteSelected()
    {
        var files = _selected.Where(path => ItemAt(path) is { Kind: not DesktopItems.Kind.Shell }).ToList();
        if (files.Count == 0) return;
        var message = files.Count == 1
            ? Loc.T($"确定把“{DesktopItems.NameOf(files[0])}”放进回收站？", $"Send \"{DesktopItems.NameOf(files[0])}\" to the recycle bin?")
            : Loc.T($"确定把这 {files.Count} 项放进回收站？", $"Send these {files.Count} items to the recycle bin?");
        if (!BoxReport.Confirm(_owner, Loc.T("删除", "Delete"), message, Loc.T("删除", "Delete"))) return;
        try
        {
            ShellFileOperations.Recycle(files);
            _selected.Clear();
            Refresh(force: true);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("删除失败：", "The delete failed: ") + ex.Message);
        }
    }

    // ---- 打开 ----

    private static void Open(DesktopItems.Item item)
    {
        try
        {
            if (item.Kind == DesktopItems.Kind.Shell && ShellIdOf(item.Path) is { Length: > 0 } id) BoxShell.OpenShellItem(id);
            else BoxShell.Open(item.Path);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("打不开这一项：", "This item could not be opened: ") + ex.Message);
        }
    }

    // ---- 拖进来 ----

    private void Area_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        e.Effects = DragDropEffects.Copy;
        // 落点那条竖线：贴在被指着的那一格左边；指在最后面时贴在最后一格右边。
        var look = Look;
        int index = CellAt(e.GetPosition(_icons), look);
        int columns = Columns(look);
        int row = index / columns, column = index % columns;
        _dropMark.Height = BoxLayout.RowHeight(look);
        _dropMark.Margin = new Thickness(column * BoxLayout.ColumnStride(look) - BoxLayout.ColumnGap(look) / 2,
            row * BoxLayout.RowStride(look), 0, 0);
        _dropMark.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    // 一行摆得下几格。图标区按可用宽度撑开，所以用它的实际宽度算，跟 BoxIconGrid 摆图标用的是同一个式子。
    private int Columns(DesktopLookSettings look)
    {
        var usable = _icons.ActualWidth > 0 ? _icons.ActualWidth : ActualWidth;
        if (!(usable > 0)) return Math.Max(1, _items.Count);
        return Math.Max(1, (int)Math.Floor((usable + BoxLayout.ColumnGap(look)) / BoxLayout.ColumnStride(look)));
    }

    // 鼠标落在第几格。
    private int CellAt(Point at, DesktopLookSettings look)
    {
        int columns = Columns(look);
        int column = Math.Clamp((int)Math.Floor((at.X + BoxLayout.ColumnGap(look) / 2) / BoxLayout.ColumnStride(look)), 0, columns);
        int row = Math.Max(0, (int)Math.Floor(at.Y / BoxLayout.RowStride(look)));
        return Math.Clamp(row * columns + column, 0, Math.Max(0, _items.Count));
    }

    private void Area_Drop(object sender, DragEventArgs e)
    {
        _dropMark.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var folder = ShellDesktop.FolderPath();
        if (folder.Length == 0)
        {
            BoxReport.Error(Loc.T("找不到桌面文件夹。", "The desktop folder could not be found."));
            return;
        }
        // 拖进来的文件复制到桌面上。用复制而不是移动：从别的地方拖过来的东西，原处那份不该没了。
        try
        {
            BoxTransferExecutor.Execute(paths, folder, BoxDropAction.Copy, Look.Conflict);
            Refresh(force: true);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("放到桌面上失败：", "Putting the files on the desktop failed: ") + ex.Message);
        }
        e.Handled = true;
    }
}