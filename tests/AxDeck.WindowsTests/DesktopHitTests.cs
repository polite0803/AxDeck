using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

// 桌面页的图标为什么收不到鼠标：这里不猜，把真实的 DesktopIconsView 搭起来，
// 用真实布局后的命中测试问一句「点在图标上，命中的到底是谁」。
internal static class DesktopHitTests
{
    public static void Run(Action<string, bool> check)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // 桌面页的图标不用到应用的资源，但保险起见把它的样式表挂上：没有样式的控件在测试里也画得出来。
        _ = app;
        // 真实的 AxDeck 面板是分层窗口（AllowsTransparency）。分层窗口只让不透明的像素接住点击，
        // 全透明的那些直接穿过去——桌面页的图标正是「透明底 + 不透明图」这种画法，所以这件事必须按真实条件问。
        var window = new MainWindow { Width = 640, Height = 300, Left = -20000, Top = -20000,
            ShowActivated = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent };
        var view = new DesktopIconsView(window) { Width = 640, Height = 300 };
        window.Content = view;
        try
        {
            window.Show();
            Pump();
            view.Refresh(force: true);
            Pump();

            var grid = FindGrid(view);
            check("桌面页画出了图标区", grid is not null);
            if (grid is null) return;

            var icons = grid.Children.OfType<Border>().ToList();
            check($"桌面页画出了图标（{icons.Count} 个）", icons.Count > 0);
            if (icons.Count == 0) return;

            // 图标格里，图标本身的那个 Border：它的 Tag 是这一项的路径。
            var icon = icons[0];
            check("图标带得着自己的路径", icon.Tag is string);

            // 图标自己的大小：0 或极小的话，它就画不出来、也点不到。
            check($"图标有自己的大小（{icon.ActualWidth:0.#}x{icon.ActualHeight:0.#}）",
                icon.ActualWidth > 8 && icon.ActualHeight > 8);

            // 关键一问：点在图标中心，命中测试找到的是谁。
            var centre = new Point(icon.ActualWidth / 2, icon.ActualHeight / 2);
            var hit = VisualTreeHelper.HitTest(icon, centre)?.VisualHit;
            check($"点在图标的中心命中的是图标本身或它的内容（命中 {hit?.GetType().Name ?? "什么都没有"}）",
                hit is not null && (ReferenceEquals(hit, icon) || AncestorOf(icon, hit)));

            // 点在图标上面一点（图标图与名字之间的空处）：还是这一项。
            var upper = new Point(icon.ActualWidth / 2, 3);
            var hitUpper = VisualTreeHelper.HitTest(icon, upper)?.VisualHit;
            check($"点在图标的上沿也命中这一项（命中 {hitUpper?.GetType().Name ?? "什么都没有"}）",
                hitUpper is not null && (ReferenceEquals(hitUpper, icon) || AncestorOf(icon, hitUpper)));

            // 整个视图：点在图标所在的位置，从整页问一次——这才是鼠标真正走的路径，
            // 中间任何一层盖住它、或不吃命中，都会在这里现形。
            var inView = icon.TranslatePoint(centre, view);
            var fromView = VisualTreeHelper.HitTest(view, inView)?.VisualHit;
            var reachesIcon = fromView is not null && (ReferenceEquals(fromView, icon) || AncestorOf(icon, fromView));
            check($"从整页问：点图标命中的是这一项（命中 {fromView?.GetType().Name ?? "什么都没有"}）", reachesIcon);

            // 上面那些都是 WPF 自己的命中测试，而它不看像素透不透明。可面板是分层窗口，
            // Windows 只让画了东西的像素接住点击，全透明的像素它直接放过去——点击落到下面那个
            // 窗口上，这一页连消息都收不到。用户点的位置只要在图标图以外（名字上、格子空处、
            // 两格之间）就会全部落空：单击选不中、双击打不开，而 WPF 这边一切正常。
            // 所以这里得问真正画出来的那个像素，这是这一条唯一查得出来的地方。
            foreach (var (where, at) in new[]
            {
                ("图标名字上", new Point(icon.ActualWidth / 2, icon.ActualHeight - 4)),
                ("两格之间", new Point(icon.ActualWidth + 4, icon.ActualHeight / 2))
            })
            {
                string alpha = AlphaAt(view, icon.TranslatePoint(at, view));
                check($"点在这一页的「{where}」时画出来的不是全透明（alpha {alpha}）", alpha != "0" && alpha != "窗口没有面积");
            }

            CheckClicks(view, icon, hit as FrameworkElement ?? icon, check);
        }
        finally { window.Close(); }
    }

    // 鼠标真的按下去、抬起来，这一页是不是当回事：单击要选中。
    private static void CheckClicks(DesktopIconsView view, Border icon, FrameworkElement target, Action<string, bool> check)
    {
        if (icon.Tag is not string path) { check("图标带得着自己的路径", false); return; }
        var grid = FindGrid(view)!;

        // 单击：按下再抬起。
        PressDown(target, 1);
        Pump();
        var selected = SelectedPaths(view);
        check($"单击一下，这一项就被选中了（选中 {selected.Count} 项）", selected.Contains(path));
        // 按下的那一项得记着，不然拖动永远起不来：按在图标上时，事件冒泡到图标区的父级，
        // 那里不能把刚记下的这一项当成「点在空白处」清掉。
        check("按在图标上时，记着的就是这一项", PressPath(view) == path);

        var painted = icon.Background is SolidColorBrush brush && brush.Color.A > 0;
        check("选中的图标看得出来是选中的（底色变了）", painted);

        Release(target, 1);
        Pump();
        check("单击抬起之后它还是选中的", SelectedPaths(view).Contains(path));

        // 空白处按一下：选区清掉，跟资源管理器一样。
        PressDown(grid, 1);
        Pump();
        check("点空白处清掉选区", SelectedPaths(view).Count == 0);
    }

    // 一次完整的按下：预览那一路从根隧穿下来，冒泡那一路再从图标冒上去，跟真实输入一样。
    // 生产代码挂在 PreviewMouseLeftButtonDown / MouseLeftButtonDown 上——这两个都是 Direct 路由，
    // 由 MouseDown 在路径上经过每个元素时逐个引发，所以要发的正是 MouseDown 本身。
    private static void PressDown(FrameworkElement target, int clickCount)
    {
        Raise(target, UIElement.PreviewMouseDownEvent, clickCount);
        Raise(target, UIElement.MouseDownEvent, clickCount);
    }

    private static void Release(FrameworkElement target, int clickCount)
        => Raise(target, UIElement.MouseUpEvent, clickCount);

    // 手造一次鼠标事件。OriginalSource 是鼠标真正落在的那个元素——生产代码正是从它往上找
    // 「按在哪一项上」的；手造的事件没有它，就得自己放进去。
    private static void Raise(FrameworkElement target, RoutedEvent routed, int clickCount)
    {
        var args = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = routed,
            Source = target
        };
        // 点击次数由 WPF 的输入系统在心里记着，测试里得自己放进去。
        typeof(System.Windows.Input.MouseButtonEventArgs)
            .GetField("_clickCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(args, clickCount);
        SetOriginalSource(args, target);
        target.RaiseEvent(args);
    }

    private static void SetOriginalSource(RoutedEventArgs args, object source)
        => typeof(RoutedEventArgs)
            .GetField("_originalSource", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(args, source);

    // 视图在这一点上真正画出来的那个像素有多不透明。0 就是全透明，Windows 会让点击穿过去。
    // inView 是视图自己的坐标。
    private static string AlphaAt(FrameworkElement view, Point inView)
    {
        if (view.ActualWidth < 1 || view.ActualHeight < 1) return "窗口没有面积";
        var dpi = VisualTreeHelper.GetDpi(view);
        int width = (int)Math.Ceiling(view.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(view.ActualHeight * dpi.DpiScaleY);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(view);
        int x = Math.Clamp((int)Math.Round(inView.X * dpi.DpiScaleX), 0, width - 1);
        int y = Math.Clamp((int)Math.Round(inView.Y * dpi.DpiScaleY), 0, height - 1);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return $"{pixel[3]}";
    }

    // 这一页记着的「按在哪一项上」。
    private static string? PressPath(DesktopIconsView view)
        => typeof(DesktopIconsView)
            .GetField("_pressPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(view) as string;

    // 这一页现在选着哪些项。
    private static HashSet<string> SelectedPaths(DesktopIconsView view)
    {
        var field = typeof(DesktopIconsView).GetField("_selected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(view) as HashSet<string> ?? [];
    }

    // 是不是祖先与后代的关系。
    private static bool AncestorOf(DependencyObject ancestor, DependencyObject? element)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    // 图标区：BoxIconGrid，它是视图里唯一画着图标的东西。
    private static BoxIconGrid? FindGrid(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is BoxIconGrid grid) return grid;
            if (FindGrid(child) is { } found) return found;
        }
        return null;
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}

public partial class MainWindow
{
    // 桌面页只用到外观设置（图标大小、间距）和这一页的菜单去向，测试里给最小的桩。
    private BoxService? _desktopProbeBoxes;
    internal BoxService Boxes => _desktopProbeBoxes ??= new BoxService();
    internal void BoxesOpenPage() { }
}