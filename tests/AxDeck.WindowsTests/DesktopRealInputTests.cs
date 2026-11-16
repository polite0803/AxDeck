using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 桌面页的图标收不收得到鼠标——按真实条件问：一扇和产品一模一样的窗口（分层、不抢焦点、
// MA_NOACTIVATE），用 SendInput 送真正的鼠标消息进去，再看窗口层到底收到了什么、这一页当不当回事。
//
// 前面那套用 RaiseEvent 造的测试证明不了这件事：它绕开了系统的输入层，而产品的问题恰恰可能就在那一层。
internal static class DesktopRealInputTests
{
    public static void Run(Action<string, bool> check)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _ = app;

        // 真的要用光标去点，窗口就得在屏幕上真的看得见：SetCursorPos 会被夹回屏幕范围，
        // 窗口摆在屏幕外的话，那一下点根本落不到它身上。置顶，保证点在它上面而不是别的东西上面。
        var window = new MainWindow { Width = 520, Height = 240, Left = 40, Top = 40, Topmost = true,
            ShowActivated = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent };
        var view = new DesktopIconsView(window) { Width = 640, Height = 300 };
        window.Content = view;
        // 窗口收到的鼠标消息：系统这一层到底送没送进来。
        var messages = new List<int>();
        IntPtr handle = IntPtr.Zero;
        try
        {
            window.Show();
            Pump();
            handle = new WindowInteropHelper(window).Handle;
            // 和产品一样的窗口样式：分层、不抢焦点、把 WM_MOUSEACTIVATE 挡回去。
            WindowsHost.ConfigureOverlay(window, capturable: false);
            HwndSource.FromHwnd(handle)?.AddHook((IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (message is 0x0201 or 0x0202 or 0x0204 or 0x0205) messages.Add(message);
                return IntPtr.Zero;
            });
            Pump();
            check("真实窗口建起来了", handle != IntPtr.Zero);
            // 真点屏幕上的某一点，那一点上最上面的窗口就必须是本窗口；别的窗口开着的时候
            // WPF 的 Topmost 不一定真的把它抬上去，所以这里直接向窗口管理器要一次。
            SetWindowPos(handle, -1 /* HWND_TOPMOST */, 0, 0, 0, 0, 0x0001 | 0x0002); // NOSIZE | NOMOVE
            Pump();
            GetWindowRect(handle, out var rect);
            Console.WriteLine($"  窗口矩形 {rect.Left},{rect.Top}-{rect.Right},{rect.Bottom}，可见 {IsWindowVisible(handle)}，" +
                $"置顶 {(GetWindowLongPtr(handle, -20).ToInt64() & 0x00000008L) != 0}");

            view.Refresh(force: true);
            Pump();
            var grid = FindGrid(view);
            check("桌面页画出了图标区", grid is not null);
            if (grid is null) return;
            var icons = grid.Children.OfType<Border>().ToList();
            check($"桌面页画出了图标（{icons.Count} 个）", icons.Count > 0);
            if (icons.Count == 0) return;

            var icon = icons[0];
            var centre = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight / 2), window);
            var screen = window.PointToScreen(centre);

            // 系统层在这个点上会不会把点击交给本窗口。分层窗口只让画了东西的像素接住点击，
// 完全透明（alpha=0）的像素直接穿过去——这跟 WPF 自己的命中测试是两套机制。
            bool Hits(Point inWindow)
            {
                var p = window.PointToScreen(inWindow);
                SetCursorPos((int)Math.Round(p.X), (int)Math.Round(p.Y));
                Pump();
                GetCursorPos(out var at);
                return WindowFromPoint(at) == handle;
            }

            var iconImage = FindImage(icon);
            var imageAt = iconImage is null ? default : iconImage.TranslatePoint(new Point(iconImage.ActualWidth / 2, iconImage.ActualHeight / 2), window);
            if (iconImage is not null) Console.WriteLine($"  图标图 {iconImage.ActualWidth:0}x{iconImage.ActualHeight:0}，在窗口里的 {imageAt.X:0},{imageAt.Y:0}");
            Console.WriteLine($"  图标图上那个像素的透明度：{AlphaAt(window, imageAt)}（0 就是全透明，Windows 会让点击穿过去）");
            Console.WriteLine($"  图标间空隙那个像素的透明度：{AlphaAt(window, new Point(400, 200))}");
            check($"点在图标图上：系统把点击交给本窗口", iconImage is not null && Hits(imageAt));
            check("点在图标之间的空隙上：系统把点击交给本窗口", Hits(new Point(400, 200)));

            // 真的把光标挪过去、真的按一下、真的松开。
            bool moved = SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
            Pump();
            GetCursorPos(out var at);
            var under = WindowFromPoint(at);
            Console.WriteLine($"  图标中心在 {screen.X:0},{screen.Y:0}，光标现在在 {at.X},{at.Y}，挪动{(moved ? "成功" : "失败")}；" +
                $"那个点上最上面的窗口是 {under}（本窗口 {handle}）");
            // 别的窗口挡在上面时这一下点不到本窗口，那不是被测代码的问题；这条测试也就无从谈起。
            check("光标所在的位置上是本窗口，没有被别人挡住", under == handle);
            if (under != handle) return;

            Click(screen, clickCount: 1);
            Pump();

            check($"系统把按下的鼠标消息送进了窗口（收到 {Describe(messages)}）",
                messages.Contains(0x0201));
            var selected = SelectedPaths(view);
            check($"桌面页收到了这次单击（选中 {selected.Count} 项）", selected.Count == 1);

            // 点在图标图以外的地方也是这一页的：名字上、格子上下留的空。先前这些地方全透明，
            // 点击被 Windows 放了过去，这一页一点反应也没有——用户看到的就是「单击没反应」。
            var name = icon.TranslatePoint(new Point(icon.ActualWidth / 2, icon.ActualHeight - 4), window);
            SetCursorPos((int)Math.Round(window.PointToScreen(name).X), (int)Math.Round(window.PointToScreen(name).Y));
            Pump();
            GetCursorPos(out var atName);
            check("点在图标名字上也在本窗口里", WindowFromPoint(atName) == handle);

            // 右键：挂在同一批图标上，它一直是好的——拿它当对照，说明窗口和事件路由本来就通。
            messages.Clear();
            Click(screen, flags: (0x0008, 0x0010)); // MOUSEEVENTF_RIGHTDOWN / RIGHTUP
            Pump();
            check($"系统把右键消息也送进了窗口（收到 {Describe(messages)}）", messages.Contains(0x0204));
        }
        finally { window.Close(); }
    }

    // 窗口在这一点上真正画出来的那个像素有多不透明。0 是全透明。
    private static string AlphaAt(Window window, Point inWindow)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        int width = (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY);
        if (width < 1 || height < 1) return "窗口没有面积";
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        int x = Math.Clamp((int)Math.Round(inWindow.X * dpi.DpiScaleX), 0, width - 1);
        int y = Math.Clamp((int)Math.Round(inWindow.Y * dpi.DpiScaleY), 0, height - 1);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return $"{pixel[3]}";
    }

    // 光标挪到屏幕上的这一点。
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELPOINT { public int X, Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out PIXELPOINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(PIXELPOINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, int after, int x, int y, int width, int height, uint flags);

    // 在屏幕的这一点上点一下：真实的鼠标按下和松开，走系统的输入队列。
    private static void Click(Point screen, int clickCount = 1, (uint Down, uint Up)? flags = null)
    {
        var (down, up) = flags ?? (0x0002u, 0x0004u); // 默认是左键
        for (int i = 0; i < Math.Max(1, clickCount); i++)
        {
            Mouse(down);
            Pump();
            Mouse(up);
            Pump();
        }
    }

    // 一次鼠标动作。位置不写：光标已经在 SetCursorPos 放好了。
    private static void Mouse(uint flags)
    {
        var input = default(Input);
        input.Type = 0; // INPUT_MOUSE
        input.Data.Mouse = new MouseInputData { Flags = flags };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
        if (sent != 1) throw new InvalidOperationException($"SendInput 没有送出去（错误码 {Marshal.GetLastWin32Error()}）");
    }

    private static string Describe(List<int> messages)
        => messages.Count == 0 ? "什么也没有"
            : string.Join(" ", messages.Select(code => code switch { 0x0201 => "左键按下", 0x0202 => "左键抬起", 0x0204 => "右键按下", 0x0205 => "右键抬起", _ => code.ToString("X4") }));

    // 这一页现在选着哪些项。
    private static HashSet<string> SelectedPaths(DesktopIconsView view)
    {
        var field = typeof(DesktopIconsView).GetField("_selected",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return field?.GetValue(view) as HashSet<string> ?? [];
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

    // 图标里那张图：真正画出不透明像素的就是它。
    private static System.Windows.Controls.Image? FindImage(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.Controls.Image image) return image;
            if (FindImage(child) is { } found) return found;
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

    // 和 Win32 的 INPUT 一样大（x64 上 40 字节），否则 SendInput 会一声不响地失败。
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
}