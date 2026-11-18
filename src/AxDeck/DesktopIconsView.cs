using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
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
    // Shift 连选和方向键的锚点：最后一次单选或加选的那一项。资源管理器的 Shift 连选是从锚点到
    // 新位置的一整段，不是「从选区里随便挑的一项」——锚点就是它，方向键再把它挪走。
    private string? _anchorPath;
    // 正在改名的那一项，和它上面的输入框。F2 打开，Enter 确认，Esc 或失焦放弃。
    private string? _renamingPath;
    private TextBox? _renameBox;
    // 鼠标按下的那一项和位置：移动超过系统的拖动阈值，这一下就成了拖出去。
    private string? _pressPath;
    private Point _pressAt;
    private bool _dragging;
    // 自动排列那一条提示说过没有：拖动时指针每动一下就进一次 MouseMove，不记着就会反复弹窗。
    private bool _autoArrangeWarned;
    // 拖到本页图标上时算出来的落点，松手或离开时清掉。拖动期间靠它画竖线。
    private DesktopIconInsertion _insertion = DesktopIconInsertion.None;
    // 这一下是连着点的第几下。
    //
    // 双击的数只有「按下」那一下带：WPF 在 MouseLeftButtonDown 里给 ClickCount=2，而随后的
    // MouseLeftButtonUp 永远是 1——抬起本身没有双击的概念。所以打开一项的判断必须看按下时记下的这个数，
    // 在抬起时再问 e.ClickCount 是问不出来的（问出来永远是 1，双击就永远不打开）。
    private int _clickCount;
    // 菜单开着：这期间面板不能收起来，否则菜单跟着一起没了。
    // 系统菜单是阻塞的，页面自己的菜单是 WPF ContextMenu（非阻塞）；两种都得算，少算一种它就被收掉。
    private bool _shellMenuOpen;
    private bool _pageMenuOpen;
    // 页面自己那份右键菜单开着时的引用：收掉它（Esc）和挂关闭回调都要用它。
    private ContextMenu? _pageMenu;

    // 桌面上图标的位置：手动排好的顺序就存在那里，和资源管理器画的是同一份。
    // 不注入时用 NullDesktopIconPositions：它一个 Win32 都不碰，测试搭多少份视图都动不到真实桌面。
    private readonly IDesktopIconPositions _positions;
    // 上一次读到的位置快照。拖动提交时拿它分配槽位，免得写之前又读一遍。
    private DesktopIconSnapshot _snapshot = DesktopIconSnapshot.Unavailable();

    public DesktopIconsView(MainWindow owner, IDesktopIconPositions? positions = null)
    {
        _owner = owner;
        _dispatcher = owner.Dispatcher;
        _positions = positions ?? new NullDesktopIconPositions();
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

    // 桌面上的文件随时会变（别的程序存了文件、装程序写了快捷方式、自己在这里删了东西）。
    // 盯着两个桌面文件夹，变了就在一小段安静之后重画一遍。系统项的显隐不在文件系统里，
    // 还是靠回到这一页和设置改动后的刷新兜底。
    //
    // 观察器不在构造时建：测试会搭很多份视图，每份都挂着两个真观察器，盯的就是用户真实的桌面。
    // 这一页真正显示出来时才盯，页面离开时放掉，收着的面板一个字节也不花。
    private readonly List<System.IO.FileSystemWatcher> _watchers = [];
    // 观察器事件从线程池线程进来，动定时器和查询状态都要封送回 UI 线程：留一个 Dispatcher 的引用。
    private readonly Dispatcher _dispatcher;
    private PromptTimer? _settle;
    private bool _dirty;

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(600);

    // 这一页显示出来时把观察器建好；离开时 Dispose。
    public void BeginWatching()
    {
        if (_settle is not null) return;
        _settle = new PromptTimer(Dispatcher, SettledRefresh) { Interval = SettleDelay };
        WatchFolder(ShellDesktop.FolderPath());
        WatchFolder(ShellDesktop.SharedFolderPath());
    }

    // 盯住一个桌面文件夹。OneDrive 接管桌面时路径会变，问不出来就盯不了，刷新退回原来的节奏。
    private void WatchFolder(string folder)
    {
        if (folder.Length == 0) return;
        try
        {
            var watcher = new System.IO.FileSystemWatcher(folder)
            {
                IncludeSubdirectories = false,
                NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.DirectoryName
                    | System.IO.NotifyFilters.Size | System.IO.NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            watcher.Created += OnDesktopChanged;
            watcher.Deleted += OnDesktopChanged;
            watcher.Renamed += OnDesktopChanged;
            watcher.Changed += OnDesktopChanged;
            watcher.Error += (_, _) => StopWatching(watcher);
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is System.IO.IOException or ArgumentException or UnauthorizedAccessException)
        {
            // 这个文件夹盯不了：刷新退回「回到这一页重画」的节奏，不算错误。
        }
    }

    private void OnDesktopChanged(object? sender, System.IO.FileSystemEventArgs e) => ScheduleSettledRefresh();

    // 观察器自己坏了（内部缓冲溢出、目录被删）：停掉它，避免每个事件都报错。
    private void StopWatching(System.IO.FileSystemWatcher watcher)
    {
        try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* 已经坏了，放掉就好 */ }
        _watchers.Remove(watcher);
    }

    private void ScheduleSettledRefresh()
    {
        // 观察器的事件在线程池线程上进来，而 PromptTimer 不是线程安全的：两个事件同时到，
        // 各自 Stop/Start 一遍会建出两个 Timer，只留下一个的引用——另一个成了每 600ms 空跑的孤儿；
        // 与 Dispose 里的 _timer.Dispose() 撞上还会抛 ObjectDisposedException。都封送到 UI 线程排队，
        // 一次只改一个，竞争就没了；重复的封送代价远小于一次重画。
        _dispatcher.BeginInvoke(() =>
        {
            if (_settle is null) return;
            _dirty = true;
            _settle.Stop();
            _settle.Interval = SettleDelay;
            _settle.Start();
        });
    }

    private void SettledRefresh()
    {
        _settle?.Stop();
        if (!_dirty) return;
        _dirty = false;
        // 收起来的面板不重画：下一次展开本来就会重画一遍。
        if (!IsVisible) return;
        Refresh(force: false);
    }

    // 页面不再显示时把观察器放掉：面板收着的时候一个字节也不该花。
    // _settle 也要清掉：BeginWatching 拿它当「已经盯着了」的标志，不清的话第一次离开这一页之后
    // 它永远不是 null，观察器就再也不会重建，桌面的文件变化从此收不到。
    public void Dispose()
    {
        _settle?.Stop();
        _settle = null;
        _dirty = false;
        foreach (var watcher in _watchers) { try { watcher.EnableRaisingEvents = false; watcher.Dispose(); } catch { /* 放掉就好 */ } }
        _watchers.Clear();
    }

    // 有菜单开着的时候，面板不能收起来。
    public bool MenuOpen => _shellMenuOpen || _pageMenuOpen;
    // 这一项只看页面自己的菜单：Esc 收它用的。
    public bool PageMenuOpen => _pageMenuOpen;

    // 把页面自己的菜单收掉（Esc 那条路用）。
    public void ClosePageMenu()
    {
        if (_pageMenu is { IsOpen: true }) _pageMenu.IsOpen = false;
    }

    // ---- 画 ----

    // 重画一遍。force 为真时不管内容有没有变都重画（改了外观、改了名字显示方式）。
    public void Refresh(bool force = false)
    {
        // 测试塞确定的一组项；正常运行去问真实桌面。
        var items = ItemsOverride?.Invoke() ?? ShellDesktop.Items();
        // 桌面上图标摆在哪儿是手动排好的顺序的唯一真相：Explorer 给出的位置比名字序更该信。
        // 读不到（Explorer 没在跑、开了自动排列）就退回名字序，不编一个看着像的顺序出来。
        _snapshot = _positions.Read();
        if (_snapshot.Status == DesktopIconPositionStatus.Ok)
        {
            // 在“要画的项”里按位置排；快照里可能还有这一页不画的项，它们不参与。
            items = DesktopIconOrdering.OrderByPositions(items, item => item.Path, _snapshot);
        }
        var look = Look;
        var key = string.Join("\n", items.Select(item => item.Path + "|" + item.Name))
            + "\n" + look.IconSize + "|" + look.LabelTextSize + "|" + look.IconGapX + "|" + look.IconGapY
            + "|" + look.ShowItemLabels + "|" + look.ShowShortcutArrow + "|" + look.NameDisplayMode
            + "|" + look.CornerRadius + "|" + PanelTheme.Generation
            // 顺序也算进重画的依据：拖动提交后内容没变、只是先后变了，不带上它就跳过重画，页面上还是旧排法。
            + "|" + string.Join(",", items.Select(item => item.Path));
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

    // 测试缝：塞一组确定的项，免得测试去读真实桌面。跟 OpenOverride 一样是一个函数，不是接口。
    internal static Func<IReadOnlyList<DesktopItems.Item>>? ItemsOverride { get; set; }

    // 一项的图标和名字。
    private FrameworkElement ItemView(DesktopItems.Item item, DesktopLookSettings look)
    {
        bool shell = item.Kind == DesktopItems.Kind.Shell;
        // 图标的键带上文件的修改时间：文件换了图标（换关联程序、换 .ico），重画时就不会再拿旧图。
        var stamp = FileStamp(item.Path);
        var icon = shell ? BoxIcons.ForShellItem(ShellIdOf(item.Path)) : BoxIcons.ForFile(item.Path, stamp);
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
            iconHost.Children.Add(new System.Windows.Shapes.Path
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

    // 一项的文件的修改时间，取不到（刚被删、正在写）就空着——缓存键里少这一段，跟没有它时一样。
    private static DateTime? FileStamp(string path)
    {
        try { return File.GetLastWriteTime(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

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
        // 双击的数只在这一下里，先记下来；下面的分支可能会早退，记晚了一步这个数就丢了。
        _clickCount = e.ClickCount;
        if (TagOf(e.OriginalSource as FrameworkElement) is not { } path) return;
        // Ctrl 加选、Shift 连选，跟资源管理器一样；不按修饰键时点谁选谁。
        // 连选的起点是「焦点项」（_anchorPath），不是选区里碰巧排第一的那一个：点选、Ctrl 加选、
        // 方向键移动都会把焦点挪到那一项上，之后的 Shift 连选就从那里展开。
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (!_selected.Remove(path)) _selected.Add(path);
            _anchorPath = path;
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            // 没有焦点项时退回老办法：拿选区的第一项当起点，至少连选还能用。
            var anchor = _anchorPath ?? _selected.FirstOrDefault();
            int from = anchor is null ? -1 : IndexOf(anchor);
            int to = IndexOf(path);
            if (from >= 0 && to >= 0)
            {
                _selected.Clear();
                for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _selected.Add(_items[i].Path);
                _anchorPath = path;
            }
        }
        else
        {
            _selected.Clear();
            _selected.Add(path);
            _anchorPath = path;
        }
        PaintSelection();

        _pressAt = e.GetPosition(this);
        _pressPath = path;
        _dragging = false;
    }

    // 按住往拖：移动超过系统认的那点距离，就把选中的这些项交给系统去拖。
    //
    // 一次拖动兼两用：落在这一页的图标上是排序（写回真实桌面的图标位置），落到页面外面是交给别的程序。
    // 数据的两种格式与落点怎么区分，见 DesktopDrag。
    private void Icons_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressPath is not { } path) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        // 按下的那一项若不在选区里（比如按住图标后先松开修饰键），这一下拖动的是它自己。
        var keys = _selected.Contains(path)
            ? _items.Where(item => _selected.Contains(item.Path)).Select(item => item.Path).ToList()
            : new List<string> { path };
        // 系统项（此电脑、回收站）没有自己的文件，拖不出去；但它们在桌面上有位置，照样能参与排序。
        var files = keys.Where(p => ItemAt(p) is { Kind: not DesktopItems.Kind.Shell }).ToArray();
        // 位置在「现在」重读一次，不用页面刷新时留下的那份：那份可能是几分钟前的，
        // 也可能因为当时 Explorer 还没就绪而是空的。排序只认此刻读得到的位置。
        _snapshot = _positions.Read();
        bool sortableNow = _snapshot.Status == DesktopIconPositionStatus.Ok;
        if (!sortableNow)
        {
            // 自动排列会把写进去的位置当场改回去，排序在这里没有意义：得说出来，否则拖完毫无反应，
            // 看着就像功能坏了。位只提示一次，不然指针每动一下都会再进来、反复弹窗。
            if (_snapshot.Status == DesktopIconPositionStatus.AutoArrange && !_autoArrangeWarned)
            {
                _autoArrangeWarned = true;
                BoxReport.Error(Loc.T("资源管理器开着「自动排列图标」，桌面图标的顺序改不了。在桌面空白处右键 → 查看 → 取消「自动排列图标」之后就能拖了。",
                    "Explorer has Auto arrange icons on, so the icons cannot be reordered. Turn it off under View on the desktop's right-click menu, and dragging will work."));
            }
            // 没有文件可拖出去、位置又读不到：这一下什么也做不成。清掉记号，免得反复进来。
            if (files.Length == 0) { _pressPath = null; return; }
        }

        _pressPath = null;
        _dragging = true;
        _insertion = DesktopIconInsertion.None;
        var dragged = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        // 能按位置排时，选中的这些项都带进拖动数据里：落回本页就是给它们换位置。
        var sortable = sortableNow ? keys : new List<string>();
        // 在 DoDragDrop 之前标记：它带自己的消息循环，Shelf 的 DragEnter 会在调用返回之前就到。
        _owner.SetDraggingOut(true);
        var result = DragDropEffects.None;
        // Esc 取消一次拖动：拖动中的落点与竖线一起清掉，DoDragDrop 返回 None，顺序自然不动。
        void Query(object origin, QueryContinueDragEventArgs cancel)
        {
            if (cancel.EscapePressed) { _insertion = DesktopIconInsertion.None; _dropMark.Visibility = Visibility.Collapsed; }
        }
        // QueryContinueDrag 是附加事件，UIElement 上没有同名的 CLR 事件，只能这样挂。
        DragDrop.AddQueryContinueDragHandler(this, Query);
        try
        {
            var data = DesktopDrag.Build(sortable, files);
            // 有文件时给复制：文件还在桌面上，这一页只是把它交出去，不是从这里搬走。
            // 能排序时再把移动打开：落在本页图标上会以 Move 交回来（见 Area_Drop）。
            var allowed = sortable.Count > 0 && files.Length > 0
                ? DragDropEffects.Copy | DragDropEffects.Move
                : files.Length > 0 ? DragDropEffects.Copy : DragDropEffects.Move;
            result = DragDrop.DoDragDrop(this, data, allowed);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            DragDrop.RemoveQueryContinueDragHandler(this, Query);
            _dragging = false;
            _dropMark.Visibility = Visibility.Collapsed;
            // 告诉面板拖动结束：从桌面页往外拖时，面板的剪贴板页会收到 DragEnter，
            // 不标出这是面板自己的拖动，它就会在拖动途中切页，把两页的内容叠在一起。
            _owner.SetDraggingOut(false);
            // 拖出去的这一阵指针多半在面板外面（DoDragDrop 是阻塞的，期间收起被挡住），告诉面板再查一次离开。
            _owner.MenuClosed();
        }
        // 落在本页图标上：只在这里提交排序，拖出去（Copy）和取消（None）都不动顺序。
        // 提交放在 DoDragDrop 返回之后：拖动期间改 Children 会把正在拖的那个元素从树上摘掉。
        if (result == DragDropEffects.Move)
            CommitOrder(_insertion, dragged);
        _insertion = DesktopIconInsertion.None;
    }

    // ---- 排序 ----

    // 拖动提交：按落点算出新顺序，把它写成真实桌面上图标的位置，再按真实位置重画。
    // 写不成功就重读一遍位置、照着原样画回去并说明原因——绝不让页面上留一个其实没存下来的顺序。
    private void CommitOrder(DesktopIconInsertion insertion, IReadOnlySet<string> dragged)
    {
        if (!insertion.IsValid || dragged.Count == 0) return;
        // 分配槽位用的是「现在」的位置，不是上一次重画时读的那份：拖动这几秒里，别处可能刚动过桌面。
        // 读不到就什么都不写，页面上什么都不改——绝不能拿一份不知道对不对的顺序去覆盖真实桌面。
        _snapshot = _positions.Read();
        if (_snapshot.Status == DesktopIconPositionStatus.AutoArrange)
        {
            BoxReport.Error(Loc.T("资源管理器开着「自动排列图标」，桌面图标的顺序改不了。",
                "Explorer has Auto arrange icons on, so the icons cannot be reordered."));
            return;
        }
        if (_snapshot.Status != DesktopIconPositionStatus.Ok)
        {
            // 读不到图标位置：写不了，也不能假装写成了。说清为什么，让用户知道这不是「拖了没用」。
            BoxReport.Error(Loc.T("读不到桌面图标的位置，顺序没能保存。",
                "The desktop icons' positions could not be read, so the order was not saved."));
            return;
        }

        var order = DesktopIconOrdering.ApplyInsertion([.. _items.Select(item => item.Path)], dragged, insertion);
        var placements = DesktopIconOrdering.PlanPlacements(order, _snapshot);
        if (placements.Count == 0)
        {
            // 要挪的项在真实桌面上一个都认不出来：写不出去，同样要说明。
            BoxReport.Error(Loc.T("桌面图标的位置没能对应上，顺序没有保存。",
                "The page's icons could not be matched to the desktop's, so the order was not saved."));
            return;
        }

        var result = _positions.Write(placements);
        // 不管写成没写成，都按真实位置重读一遍重画：写成时页面照着新位置排（间距、对齐也一致），
        // 没写成时回到原来的顺序——页面上不该留一个没存下来的排法。
        _key = "";
        Refresh(force: true);
        if (!result.Ok)
            BoxReport.Error(Loc.T("桌面图标的顺序没能保存。", "The icons' order could not be saved."));
    }

    private void Icons_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _pressPath = null;
        // 这一下是第几下，一进门就取走：下面哪条路提前返回，这个数都不会留到下一次去。
        var count = _clickCount;
        _clickCount = 0;
        if (_dragging) return;
        if (TagOf(e.OriginalSource as FrameworkElement) is not { } path) return;
        if (ItemAt(path) is not { } item) return;
        // 双击打开。桌面就是这个规矩：点一下选中，点两下打开。
        // 数看的是按下时记下的那个：抬起事件自己的 ClickCount 永远是 1（见 _clickCount）。
        if (count < 2) return;
        Open(item);
        e.Handled = true;
    }

    private void Area_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        // 按在空白处也是连着点的第一下或第二下：这个数照样记，抬起时才不会拿上一次的旧值。
        _clickCount = e.ClickCount;
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
        if (!_selected.Contains(path))
        {
            _selected.Clear();
            _selected.Add(path);
            _anchorPath = path;
            PaintSelection();
        }
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
        _pageMenu = menu;
        menu.Closed += (_, _) =>
        {
            _pageMenuOpen = false;
            // 菜单在鼠标外面的页面上开着，收起来以后指针已经不在面板里，得重新走一遍离开流程。
            Dispatcher.BeginInvoke(() => _owner.MenuClosed());
        };
        _pageMenuOpen = true;
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
        _shellMenuOpen = true;
        try
        {
            // 菜单是阻塞的：它开在桌面上，直到用户选一项或把它关掉。这期间面板不能收起来。
            // 系统项（此电脑、回收站…）没有文件路径，它们的菜单得按解析名去要；普通项按路径。
            bool ran = item.Kind == DesktopItems.Kind.Shell && ShellIdOf(item.Path) is { Length: > 0 } id
                && Core.BoxShellItems.ParsingName(id) is { Length: > 0 } parsing
                ? ShellContextMenu.ShowForParsingName(hwnd, parsing, (int)screen.X, (int)screen.Y)
                : ShellContextMenu.Show(hwnd, item.Path, (int)screen.X, (int)screen.Y);
            // 菜单里做的事可能改了文件（改名、删除），所以重画一遍。
            if (ran) Refresh(force: true);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("系统菜单打不开：", "The system menu could not be opened: ") + ex.Message);
        }
        finally
        {
            _shellMenuOpen = false;
            // 同上：菜单结束时指针多半已经不在面板上了，告诉面板再查一次离开。
            Dispatcher.BeginInvoke(_owner.MenuClosed);
        }
    }

    // ---- 键盘 ----

    // 跟桌面一样的那套键盘：回车打开、Delete 删除、F2 改名、Ctrl+C/X/V 走系统剪贴板、
    // 方向键挪焦点（Shift 一起按就连选）、菜单键弹系统菜单、Alt+Enter 看属性、Ctrl+A 全选、F5 刷新。
    private void View_KeyDown(object sender, KeyEventArgs e)
    {
        // 改名的输入框开着时，这一页不再接键：输入框自己处理 Enter 和 Esc（见 RenameBoxOnKey）。
        if (_renameBox is not null) return;
        var modifiers = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Enter when (modifiers & ModifierKeys.Alt) != 0 && _selected.Count > 0:
                ShowProperties(_selected.First());
                e.Handled = true;
                break;
            case Key.Enter when _selected.Count > 0:
                foreach (var path in _selected.ToList())
                    if (ItemAt(path) is { } item) Open(item);
                e.Handled = true;
                break;
            case Key.Delete when _selected.Count > 0:
                DeleteSelected();
                e.Handled = true;
                break;
            case Key.F2 when _selected.Count > 0:
                BeginRename(_selected.First());
                e.Handled = true;
                break;
            case Key.C when modifiers == ModifierKeys.Control && _selected.Count > 0:
                CopySelected();
                e.Handled = true;
                break;
            case Key.X when modifiers == ModifierKeys.Control && _selected.Count > 0:
                CutSelected();
                e.Handled = true;
                break;
            case Key.V when modifiers == ModifierKeys.Control:
                PasteFromClipboard();
                e.Handled = true;
                break;
            case Key.F5:
                Refresh(force: true);
                e.Handled = true;
                break;
            case Key.A when modifiers == ModifierKeys.Control:
                _selected.Clear();
                foreach (var item in _items) _selected.Add(item.Path);
                PaintSelection();
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down:
                MoveFocus(e.Key, (modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                break;
            case Key.Home or Key.End:
                MoveToEdge(e.Key == Key.Home, (modifiers & ModifierKeys.Shift) != 0);
                e.Handled = true;
                break;
            // 菜单键（App 菜单键，或 Shift+F10）：选中的那几项弹系统菜单，跟在图标上按右键一样。
            case Key.Apps or Key.F10 when e.Key == Key.Apps || ((modifiers & ModifierKeys.Shift) != 0 && e.Key == Key.F10):
                if (_selected.Count > 0 && ItemAt(_selected.First()) is { } chosen)
                {
                    if (FindBorder(chosen.Path) is { } host) ShowItemMenu(chosen.Path, host.TranslatePoint(new Point(host.ActualWidth / 2, host.ActualHeight / 2), this));
                }
                e.Handled = true;
                break;
            case Key.Escape:
                _selected.Clear();
                _anchorPath = null;
                PaintSelection();
                e.Handled = true;
                break;
        }
    }

    // ---- 选区导航 ----

    // 方向键挪焦点。Shift 一起按，就是从锚点到新焦点的一整段连选；不按，就是单选新焦点。
    private void MoveFocus(Key key, bool extend)
    {
        if (_items.Count == 0) return;
        int columns = Columns(Look);
        int from = _anchorPath is { } anchor ? IndexOf(anchor) : -1;
        // 没有焦点：从选区的头一个算起，再没有就从第一格开始。
        if (from < 0) from = _selected.Count > 0 ? Math.Max(0, IndexOf(_selected.First())) : 0;
        int to = key switch
        {
            Key.Left => Math.Max(0, from - 1),
            Key.Right => Math.Min(_items.Count - 1, from + 1),
            Key.Up => Math.Max(0, from - columns),
            _ => Math.Min(_items.Count - 1, from + columns)
        };
        SelectRange(from, to, extend);
        EnsureVisible(to);
    }

    // Home/End：第一格或最后一格。
    private void MoveToEdge(bool home, bool extend)
    {
        if (_items.Count == 0) return;
        int from = _anchorPath is { } anchor ? Math.Max(0, IndexOf(anchor)) : 0;
        int to = home ? 0 : _items.Count - 1;
        SelectRange(from, to, extend);
        EnsureVisible(to);
    }

    // 一段选区：从锚点 to 到新焦点 to。extend 为假时重新起头（锚点跟着挪）。
    private void SelectRange(int from, int to, bool extend)
    {
        int anchor = to;
        if (extend)
        {
            anchor = Math.Max(0, from);
        }
        if (!extend) _selected.Clear();
        for (int i = Math.Min(from, to); i <= Math.Max(from, to); i++) _selected.Add(_items[i].Path);
        _anchorPath = _items[anchor].Path;
        PaintSelection();
    }

    // 把第 index 格滚进视野：方向键走到窗口外面时，列表跟着滚。
    private void EnsureVisible(int index)
    {
        if (FindBorder(_items[index].Path) is not { } host) return;
        host.BringIntoView();
    }

    // 一项的格子：按路径在画出来的图标里找。
    private FrameworkElement? FindBorder(string path)
    {
        foreach (var child in _icons.Children.OfType<Border>())
            if (child.Tag is string tag && string.Equals(tag, path, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    // ---- 剪贴板 ----

    // 复制选中的文件到系统剪贴板：Explorer 的那一份（CF_HDROP + Preferred DropEffect），
    // 从这里复制出去，资源管理器和别的程序都接得住。
    private void CopySelected()
    {
        var files = SelectedFiles();
        if (files.Count == 0) return;
        ShellClipboard.Write(files, ShellClipboard.Operation.Copy);
    }

    // 剪切：也是写系统剪贴板，意图是移动。桌面页自己不粘贴到自己的文件夹（那样等于什么都没变），
    // 粘贴到桌面上时（Ctrl+V）剪切过的文件就搬过来。
    private void CutSelected()
    {
        var files = SelectedFiles();
        if (files.Count == 0) return;
        ShellClipboard.Write(files, ShellClipboard.Operation.Move);
    }

    // 粘贴：剪贴板上有文件就落到桌面上。复制来的复制过来，剪切来的搬过来（走 shell，Explorer 看得见）。
    private void PasteFromClipboard()
    {
        var (paths, operation) = ShellClipboard.Read();
        if (paths.Count == 0) return;
        var folder = ShellDesktop.FolderPath();
        if (folder.Length == 0)
        {
            BoxReport.Error(Loc.T("找不到桌面文件夹。", "The desktop folder could not be found."));
            return;
        }
        var conflict = Look.Conflict;
        var ui = _dispatcher;
        Task.Run(() =>
        {
            try
            {
                BoxTransferExecutor.Execute([.. paths], folder,
                    operation == ShellClipboard.Operation.Move ? BoxDropAction.Move : BoxDropAction.Copy, conflict);
                ui.BeginInvoke(() => Refresh(force: true));
            }
            catch (Exception ex)
            {
                ui.BeginInvoke(() => BoxReport.Error(Loc.T("粘贴到桌面失败：",
                    "Pasting onto the desktop failed: ") + ex.Message));
            }
        });
    }

    // 选中的那几项里的真实文件：系统项没有文件，参加不了复制剪切。
    private List<string> SelectedFiles()
        => _selected.Where(path => ItemAt(path) is { Kind: not DesktopItems.Kind.Shell }).ToList();

    // ---- 属性 ----

    // 一项的属性对话框，跟资源管理器右键「属性」是同一个。系统项也认：按解析名交给 shell。
    private static void ShowProperties(string path)
    {
        if (path.Length == 0) return;
        // ShellExecuteEx 的动词 properties 只认文件路径；系统项的解析名它不认，交给 SHObjectProperties。
        try
        {
            if (path.StartsWith("::", StringComparison.Ordinal))
            {
                Native.ShellContextMenu.ShowPropertiesDialog(path);
                return;
            }
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "properties" });
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("打不开属性：", "The properties could not be shown: ") + ex.Message);
        }
    }

    // ---- 改名 ----

    // F2：把这一项的名字变成一个输入框。改的是文件自己，改完桌面和资源管理器看到的是同一个名字。
    private void BeginRename(string path)
    {
        if (_renameBox is not null || ItemAt(path) is not { } item) return;
        // 系统项没有文件：它的名字是 Windows 定的，这里改不了（资源管理器也不让改）。
        if (item.Kind == DesktopItems.Kind.Shell) return;
        var host = FindBorder(path);
        if (host is not Border border || border.Child is not StackPanel panel) return;
        var label = panel.Children.OfType<TextBlock>().FirstOrDefault();
        if (label is null) return;

        var box = new TextBox
        {
            Text = BoxNaming.IsShortcut(path) ? System.IO.Path.GetFileNameWithoutExtension(path) : item.Name,
            FontSize = label.FontSize,
            Background = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            Foreground = label.Foreground,
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 120, 170, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2, 0, 2, 0),
            Width = panel.Width,
            MaxLength = 255,
            SelectionBrush = new SolidColorBrush(Color.FromArgb(160, 120, 170, 255))
        };
        // 默认选中主名（不带扩展名），跟桌面按 F2 一样。
        box.Loaded += (_, _) =>
        {
            box.Focus();
            var stem = BoxNaming.IsShortcut(path) ? box.Text.Length : System.IO.Path.GetFileNameWithoutExtension(item.Name).Length;
            box.Select(0, Math.Max(0, Math.Min(stem, box.Text.Length)));
        };
        box.KeyDown += (_, key) =>
        {
            if (key.Key == Key.Enter) { FinishRename(path, box.Text); key.Handled = true; }
            else if (key.Key == Key.Escape) { CancelRename(); key.Handled = true; }
        };
        box.LostFocus += (_, _) => CancelRename();
        _renamingPath = path;
        _renameBox = box;
        int at = panel.Children.IndexOf(label);
        panel.Children.RemoveAt(at);
        panel.Children.Insert(at, box);
        box.Focus();
    }

    // 改完了：名字合法就改名，不然把输入框放回去并说一声为什么。
    private void FinishRename(string path, string name)
    {
        CancelRename();
        var clean = BoxNaming.SanitizeUserFileName(name);
        if (clean is null)
        {
            BoxReport.Error(Loc.T("这个名字 Windows 不接受。", "Windows does not take that name."));
            return;
        }
        // 名字没变就不动文件：改一个字又改回去不该算一次改名。
        if (string.Equals(clean, DesktopItems.NameOf(path), StringComparison.OrdinalIgnoreCase))
        {
            Refresh(force: true);
            return;
        }
        try
        {
            var item = ItemAt(path);
            if (item is null) return;
            var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path) ?? "", clean);
            if (Directory.Exists(path)) Directory.Move(path, target);
            else File.Move(path, target);
            Refresh(force: true);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("改名失败：", "The rename failed: ") + ex.Message);
        }
    }

    // 不改了：把输入框放回去，名字照旧。
    private void CancelRename()
    {
        if (_renameBox is null) return;
        var box = _renameBox;
        var path = _renamingPath;
        _renameBox = null;
        _renamingPath = null;
        // LostFocus 也会走到这里（换成名字这一下自己就触发一次），先把字段清掉再动树，第二次进来直接返回。
        if (path is { } live && ItemAt(live) is { } item && FindBorder(live) is Border border && border.Child is StackPanel panel
            && panel.Children.OfType<TextBox>().FirstOrDefault() is { } current && ReferenceEquals(current, box))
        {
            int at = panel.Children.IndexOf(current);
            panel.Children.RemoveAt(at);
            panel.Children.Insert(at, new TextBlock
            {
                Text = item.Name,
                FontSize = Look.LabelTextSize,
                Foreground = UI.Brush(UI.Primary),
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = Math.Max(14, Math.Ceiling(Look.LabelTextSize * 1.32)) * 2,
                Width = panel.Width,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, DesktopIconView.LabelGap, 0, 0),
                IsHitTestVisible = false
            });
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
        // When set, opening an item is handed over instead of starting the real program. Tests assign a single function,
        // so that a double click can be checked without launching whatever happens to be on the desktop.
        if (OpenOverride is { } over) { over(item); return; }
        // 真正的打开交给这两个函数。哪个分支接哪种项也是行为的一部分：系统项走解析名，普通项走路径。
        // 测试把这两个缝换掉，就能验分支而不真把桌面上的程序拉起来。
        try
        {
            if (item.Kind == DesktopItems.Kind.Shell && ShellIdOf(item.Path) is { Length: > 0 } id) OpenShellBranch(id);
            else OpenFileBranch(item.Path);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("打不开这一项：", "This item could not be opened: ") + ex.Message);
        }
    }

    // 系统项走这里：按解析名交给 shell。
    private static void OpenShellBranch(string id)
    {
        if (OpenShellOverride is { } over) { over(id); return; }
        BoxShell.OpenShellItem(id);
    }

    // 普通项走这里：路径交给系统，文件用它自己的程序开，文件夹进资源管理器。
    private static void OpenFileBranch(string path)
    {
        if (OpenFileOverride is { } over) { over(path); return; }
        BoxShell.Open(path);
    }

    // 打开了什么，交给这个函数。测试用它接住「双击打开」，免得真把桌面上的程序拉起来。
    internal static Action<DesktopItems.Item>? OpenOverride { get; set; }
    // 系统项分支的缝：接到的是解析名对应的编号。
    internal static Action<string>? OpenShellOverride { get; set; }
    // 普通项分支的缝：接到的是路径。
    internal static Action<string>? OpenFileOverride { get; set; }

    // ---- 拖进来 ----

    private void Area_DragOver(object sender, DragEventArgs e)
    {
        // 本页自己起的拖动：落在页内就是排序。
        if (DesktopDrag.IsInternal(e.Data))
        {
            var keys = DesktopDrag.Keys(e.Data);
            if (keys.Length > 0 && _snapshot.Status == DesktopIconPositionStatus.Ok)
            {
                _insertion = ResolveDrop(e.GetPosition(_icons), keys);
                e.Effects = _insertion.IsValid ? DragDropEffects.Move : DragDropEffects.None;
                ShowDropMark(_insertion);
                e.Handled = true;
                return;
            }
            // 排不了（开了自动排列、读不到位置）：不要因此拒绝这一整次拖动，否则页面上哪里都放不下，
            // 看着就是「图标拖不动」。有文件就照原来的规矩走下面那条复制出去的路。
            _insertion = DesktopIconInsertion.None;
            _dropMark.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // 只拖着系统项（此电脑这类没有文件的）：本来也交不出去，收下这个拖动只会白忙。
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }
        }
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

    // 拖动中的落点：把画着的每个图标换算成矩形交给 Core，由它按上下分行、行内按中线定前后。
    // 被拖的那几项不参与：它们跟着指针走，留在原处只会让落点跳来跳去。
    private DesktopIconInsertion ResolveDrop(Point at, IEnumerable<string> dragged)
    {
        List<DesktopIconOrdering.Rect> rects = [];
        foreach (var child in _icons.Children.OfType<Border>())
        {
            if (child.Tag is not string key) continue;
            var corner = child.TransformToAncestor(_icons).Transform(new Point(0, 0));
            rects.Add(new DesktopIconOrdering.Rect(key, corner.X, corner.Y, child.ActualWidth, child.ActualHeight));
        }
        return DesktopIconOrdering.ResolveInsertion(rects, at.X, at.Y,
            new HashSet<string>(dragged, StringComparer.OrdinalIgnoreCase), BoxLayout.RowGap(Look));
    }

    // 落点那条竖线：画在被指着的那一项的左边缘（落在它之后时画右边缘）。
    private void ShowDropMark(DesktopIconInsertion insertion)
    {
        if (!insertion.IsValid || FindBorder(insertion.TargetKey!) is not { } host)
        {
            _dropMark.Visibility = Visibility.Collapsed;
            return;
        }
        var corner = host.TransformToAncestor(_icons).Transform(new Point(0, 0));
        double x = insertion.After ? corner.X + host.ActualWidth : corner.X;
        _dropMark.Height = host.ActualHeight;
        _dropMark.Margin = new Thickness(x, corner.Y, 0, 0);
        _dropMark.Visibility = Visibility.Visible;
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
        // 本页自己起的拖动落在页内：这是排序，不落盘。交出 Move，DoDragDrop 返回后按落点提交（见 Icons_MouseMove）。
        if (DesktopDrag.IsInternal(e.Data))
        {
            var keys = DesktopDrag.Keys(e.Data);
            _insertion = keys.Length > 0 && _snapshot.Status == DesktopIconPositionStatus.Ok ? ResolveDrop(e.GetPosition(_icons), keys) : DesktopIconInsertion.None;
            e.Effects = _insertion.IsValid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
            return;
        }
        // Explorer 拖进来的是 string[]；面板自己发起的拖动（SetFileDropList）是 StringCollection。
        // 两种都得认，否则从这一页拖出去再拖回来，文件一声不响就不落盘。
        string[] paths = e.Data.GetData(DataFormats.FileDrop) switch
        {
            string[] array => array,
            StringCollection list => [.. list.Cast<string>()],
            _ => []
        };
        if (paths.Length == 0) return;
        var folder = ShellDesktop.FolderPath();
        if (folder.Length == 0)
        {
            BoxReport.Error(Loc.T("找不到桌面文件夹。", "The desktop folder could not be found."));
            return;
        }
        // 拖进来的文件复制到桌面上。用复制而不是移动：从别的地方拖过来的东西，原处那份不该没了。
        // 落盘在后台做：大文件夹复制几秒到几十秒，UI 线程等不得；完成后回 UI 线程重画。
        // 失败在后台也回不了 UI 弹窗——BoxReport 自己会切线程，交给它。
        try
        {
            var conflict = Look.Conflict;
            var ui = Dispatcher;
            Task.Run(() =>
            {
                try
                {
                    BoxTransferExecutor.Execute(paths, folder, BoxDropAction.Copy, conflict);
                    ui.BeginInvoke(() => Refresh(force: true));
                }
                catch (Exception ex)
                {
                    ui.BeginInvoke(() => BoxReport.Error(Loc.T("放到桌面上失败：",
                        "Putting the files on the desktop failed: ") + ex.Message));
                }
            });
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("放到桌面上失败：", "Putting the files on the desktop failed: ") + ex.Message);
        }
        e.Handled = true;
    }
}