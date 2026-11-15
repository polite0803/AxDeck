using System.Runtime.InteropServices;
using System.Text;

namespace AxDeck.Native;

// The Android emulator's own window, driven through Win32. The emulator draws itself in a Qt top level window
// (the class name starts with "Qt" and the title carries "Android Emulator"), with a small tool window for its
// side buttons. Both are plain windows of another process, so they can only be moved and restyled from the
// outside, which is what this file does.
//
// The rectangle maths is separated from the calls: SmallWindowRect, StableRect, FullscreenRect and MostlyOffscreen
// are pure functions over a monitor rectangle and an aspect ratio, so the geometry can be checked without a
// running emulator. The ported Python app learned the hard way that a window placed outside the screen cannot be
// grabbed by its title bar any more, so every placement is checked against the monitor it belongs to.
internal static class EmulatorWindow
{
    /// <summary>A rectangle in screen pixels.</summary>
    public readonly record struct Rect(int Left, int Top, int Width, int Height)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public override string ToString() => $"{Width}x{Height}@({Left},{Top})";
    }

    // ----------------------------------------------------------------------
    // Window styles and placement flags
    // ----------------------------------------------------------------------

    private const int GwlStyle = -16;
    private const uint WsCaption = 0x00C00000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsSysMenu = 0x00080000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint WsOverlappedWindow = 0x00CF0000;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const int SwRestore = 9;

    // The window that goes in front of the others, and the one that drops out of the topmost band. Both are
    // negative on purpose: they are special values, not handles. They must travel as 64 bit, so they are built
    // as IntPtr and not as int.
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private static readonly IntPtr HwndNotTopmost = new(-2);

    private const uint MonitorDefaultToNearest = 2;

    // The style bits a borderless window drops. Keeping the rest is what makes the window still show a border
    // when it is given the standard style back.
    private const uint BorderlessMask = ~(WsCaption | WsThickFrame | WsSysMenu | WsMinimizeBox | WsMaximizeBox);

    // The emulator's side tool window, a separate top level window with these exact values.
    private const string ToolbarClass = "Qt653QWindowToolSaveBits";
    private const string ToolbarTitle = "Emulator";

    // ----------------------------------------------------------------------
    // Native declarations
    // ----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr param);
    private delegate bool EnumMonitorsProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr param);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumMonitorsProc callback, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLengthW(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLongW(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLongW(IntPtr window, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    // The style of the window before it was made borderless, so the small window can be restored exactly.
    private static readonly Dictionary<IntPtr, (int Style, Rect Rect)> Saved = [];

    // ----------------------------------------------------------------------
    // Finding the emulator window
    // ----------------------------------------------------------------------

    /// <summary>The class name of a window, empty when it has none.</summary>
    public static string WindowClass(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        var text = new StringBuilder(256);
        GetClassNameW(window, text, 256);
        return text.ToString();
    }

    /// <summary>The title of a window, empty when it has none.</summary>
    public static string WindowTitle(IntPtr window)
    {
        if (window == IntPtr.Zero) return "";
        int length = GetWindowTextLengthW(window);
        if (length <= 0) return "";
        var text = new StringBuilder(length + 1);
        GetWindowTextW(window, text, length + 1);
        return text.ToString();
    }

    /// <summary>
    /// True when the window is the emulator's own display window. The manager app itself carried the same AVD
    /// name in its title in the old app, so matching on the title alone would make every window command act on the
    /// manager. The Qt class and the "Android Emulator" title are both required.
    /// </summary>
    public static bool IsEmulatorWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || !IsWindowVisible(window)) return false;
        if (!WindowClass(window).StartsWith("Qt", StringComparison.Ordinal)) return false;
        string title = WindowTitle(window);
        return title.Contains("Android Emulator", StringComparison.Ordinal)
            && (title.Contains("axtv_tv", StringComparison.Ordinal)
                || title.Contains("axtv_portrait", StringComparison.Ordinal)
                || title.Contains("5554", StringComparison.Ordinal));
    }

    /// <summary>The emulator's display window, or zero when the emulator is not running.</summary>
    public static IntPtr FindEmulatorWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (found == IntPtr.Zero && IsEmulatorWindow(window)) found = window;
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The rectangle a window occupies on the screen.</summary>
    public static Rect WindowRect(IntPtr window)
    {
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return new(0, 0, 0, 0);
        return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    /// <summary>True when the window has no title bar, which is how a full screen window is told apart.</summary>
    public static bool IsFullscreen(IntPtr window)
    {
        if (window == IntPtr.Zero) return false;
        return (GetWindowLongW(window, GwlStyle) & (int)WsCaption) == 0;
    }

    // ----------------------------------------------------------------------
    // Monitors
    // ----------------------------------------------------------------------

    /// <summary>Every display of the machine, in the order Windows reports them.</summary>
    public static IReadOnlyList<Rect> AllMonitors()
    {
        var list = new List<Rect>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfoW(monitor, ref info))
                list.Add(new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>The rectangle of the display the window is on, falling back to the primary one.</summary>
    public static Rect ActiveMonitorRect(IntPtr window)
    {
        if (window != IntPtr.Zero && IsWindow(window))
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero && GetMonitorInfoW(monitor, ref info))
                return new(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
        }
        return new(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));
    }

    /// <summary>
    /// The display a window placement is measured against. A portrait AVD was built for a tall 9:16 screen: the
    /// emulator puts its window on the primary display when it starts, and measuring against the window's own
    /// display would then fill the wrong screen. A portrait AVD therefore picks the tall display itself.
    /// </summary>
    public static Rect TargetMonitorRect(IntPtr window, bool portrait)
    {
        if (portrait)
        {
            foreach (var rect in AllMonitors())
                if (rect.Height > rect.Width && Math.Abs((double)rect.Width / rect.Height - 9.0 / 16.0) < 0.05) return rect;
        }
        return ActiveMonitorRect(window);
    }

    // ----------------------------------------------------------------------
    // Rectangle maths (pure)
    // ----------------------------------------------------------------------

    /// <summary>The small window: 70 percent of the display's width, the height follows the aspect ratio.</summary>
    public static Rect SmallWindowRect(Rect monitor, double aspect)
    {
        if (aspect <= 0) aspect = 16.0 / 9.0;
        int width = (int)(monitor.Width * 0.70);
        int height = (int)(width / aspect) + 40;      // the title bar and the border
        if (height > (int)(monitor.Height * 0.85))
        {
            height = (int)(monitor.Height * 0.85);
            width = (int)((height - 40) * aspect);
        }
        return new(monitor.Left + (monitor.Width - width) / 2, monitor.Top + (monitor.Height - height) / 2, width, height);
    }

    /// <summary>A window that fits the display and keeps the aspect ratio, centred.</summary>
    public static Rect StableRect(Rect monitor, double aspect)
    {
        if (aspect <= 0) aspect = 16.0 / 9.0;
        int width = monitor.Width;
        int height = (int)Math.Round(width / aspect);
        if (height > monitor.Height)
        {
            height = monitor.Height;
            width = (int)Math.Round(height * aspect);
        }
        return new(monitor.Left + (monitor.Width - width) / 2, monitor.Top + (monitor.Height - height) / 2, width, height);
    }

    /// <summary>
    /// The full screen rectangle. A portrait AVD on a tall 9:16 display fills the whole display, which is the
    /// only way to get a picture without black bars. Everything else falls back to the stable rectangle, because
    /// the emulator's window keeps the aspect ratio of its physical panel and a request it cannot honour comes
    /// back as a window squeezed into the top of the screen.
    /// </summary>
    public static Rect FullscreenRect(Rect monitor, double aspect, bool portrait)
    {
        if (portrait && monitor.Height > monitor.Width && Math.Abs((double)monitor.Width / monitor.Height - 9.0 / 16.0) < 0.03)
            return new(monitor.Left, monitor.Top, monitor.Width, monitor.Height);
        return StableRect(monitor, aspect);
    }

    /// <summary>
    /// True when the window barely reaches the display it belongs to. The overlap is measured instead of the
    /// top left corner, so a window that is deliberately wider than the screen is not mistaken for one that
    /// has fallen off it.
    /// </summary>
    public static bool MostlyOffscreen(Rect window, Rect monitor)
    {
        int overlapX = Math.Min(window.Right, monitor.Right) - Math.Max(window.Left, monitor.Left);
        int overlapY = Math.Min(window.Bottom, monitor.Bottom) - Math.Max(window.Top, monitor.Top);
        if (overlapX <= 0 || overlapY <= 0) return true;
        return overlapX < Math.Min(window.Width, monitor.Width) * 0.35
            || overlapY < Math.Min(window.Height, monitor.Height) * 0.35
            || window.Width < 300 || window.Height < 200;
    }

    // ----------------------------------------------------------------------
    // Placing the window
    // ----------------------------------------------------------------------

    /// <summary>Moves the window to a rectangle and keeps it there for a moment.</summary>
    public static Rect ApplyWindowRect(IntPtr window, Rect rect, int repeat = 3)
    {
        if (!IsEmulatorWindow(window)) return new(0, 0, 0, 0);
        const uint flags = SwpFrameChanged | SwpShowWindow;
        for (int i = 0; i < repeat; i++)
        {
            SetWindowPos(window, HwndNotTopmost, rect.Left, rect.Top, rect.Width, rect.Height, flags);
            Thread.Sleep(220);
        }
        Thread.Sleep(600);
        // The emulator rearranges its window on its own after the picture changed, and can push it far off the
        // screen, where the title bar cannot be reached any more. The window is pulled back when that happened.
        var current = WindowRect(window);
        if (MostlyOffscreen(current, ActiveMonitorRect(window)))
        {
            SetWindowPos(window, HwndNotTopmost, rect.Left, rect.Top, rect.Width, rect.Height, flags);
            Thread.Sleep(300);
        }
        BringWindowToTop(window);
        return WindowRect(window);
    }

    /// <summary>
    /// Gives the window the borderless style and fills the display. The placement is sent once and then left
    /// alone: sending it again makes the emulator shrink the window another step each time.
    /// </summary>
    public static Rect SetFullscreenGeometry(IntPtr window, double aspect, bool portrait, int settleMilliseconds = 800)
    {
        if (!IsEmulatorWindow(window)) return new(0, 0, 0, 0);
        int style = GetWindowLongW(window, GwlStyle);
        SetWindowLongW(window, GwlStyle, style & unchecked((int)BorderlessMask));
        Rect rect = FullscreenRect(TargetMonitorRect(window, portrait), aspect, portrait);
        SetWindowPos(window, HwndTop, rect.Left, rect.Top, rect.Width, rect.Height, SwpFrameChanged | SwpShowWindow);
        Thread.Sleep(settleMilliseconds);
        var current = WindowRect(window);
        // Only a window the emulator really shrank is corrected, and once.
        if (current.Width < rect.Width * 0.85 || current.Height < rect.Height * 0.85
            || Math.Abs(current.Width - rect.Width) > 120 || Math.Abs(current.Height - rect.Height) > 120)
        {
            Rect stable = StableRect(TargetMonitorRect(window, portrait), aspect);
            SetWindowPos(window, HwndTop, stable.Left, stable.Top, stable.Width, stable.Height, SwpFrameChanged | SwpShowWindow);
            Thread.Sleep(700);
            current = WindowRect(window);
        }
        ParkToolbar(window, portrait);
        return current;
    }

    /// <summary>Switches the window to borderless full screen, remembering how it looked before.</summary>
    public static Rect EnterFullscreen(IntPtr window, double aspect, bool portrait)
    {
        if (!IsEmulatorWindow(window)) return new(0, 0, 0, 0);
        int style = GetWindowLongW(window, GwlStyle);
        Saved[window] = (style, WindowRect(window));
        Rect rect = SetFullscreenGeometry(window, aspect, portrait);
        BringWindowToTop(window);
        SetForegroundWindow(window);
        return rect;
    }

    /// <summary>Restores the window to the size it had before the full screen, or to a small window.</summary>
    public static void ExitFullscreen(IntPtr window, double aspect, bool portrait, bool restoreSaved = true)
    {
        if (!IsEmulatorWindow(window)) return;
        int style;
        Rect rect;
        if (restoreSaved && Saved.Remove(window, out var saved))
        {
            style = saved.Style;
            rect = saved.Rect;
        }
        else
        {
            // AxDeck was restarted in between, so there is nothing to restore: the standard window style goes
            // back and the window takes the small size.
            style = GetWindowLongW(window, GwlStyle) | unchecked((int)WsOverlappedWindow);
            rect = SmallWindowRect(TargetMonitorRect(window, portrait), aspect);
        }
        SetWindowLongW(window, GwlStyle, style);
        ShowWindow(window, SwRestore);
        SetWindowPos(window, HwndNotTopmost, rect.Left, rect.Top, rect.Width, rect.Height, SwpFrameChanged | SwpShowWindow);
        SetForegroundWindow(window);
    }

    /// <summary>Brings the window back to the small centred size, leaving the full screen if it was in one.</summary>
    public static Rect MoveSmall(IntPtr window, double aspect, bool portrait)
    {
        if (!IsEmulatorWindow(window)) return new(0, 0, 0, 0);
        if (IsFullscreen(window)) ExitFullscreen(window, aspect, portrait, restoreSaved: false);
        Rect rect = SmallWindowRect(TargetMonitorRect(window, portrait), aspect);
        SetWindowPos(window, HwndNotTopmost, rect.Left, rect.Top, rect.Width, rect.Height, SwpFrameChanged | SwpShowWindow);
        SetForegroundWindow(window);
        return rect;
    }

    /// <summary>
    /// Pulls the window back on screen when it ended up outside, and lifts it to the front without taking the
    /// focus. The window keeps its shape: the emulator holds the aspect ratio of its physical panel, and asking
    /// for another one is answered with a window squeezed into a corner.
    /// </summary>
    public static Rect EnsureVisible(IntPtr window)
    {
        if (!IsEmulatorWindow(window)) return new(0, 0, 0, 0);
        var current = WindowRect(window);
        if (MostlyOffscreen(current, ActiveMonitorRect(window)))
            ApplyWindowRect(window, SmallWindowRect(ActiveMonitorRect(window), 16.0 / 9.0), repeat: 1);
        SetWindowPos(window, HwndTop, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        return WindowRect(window);
    }

    /// <summary>
    /// Brings the emulator's side button panel back onto the screen. When the window fills the display the panel
    /// is parked at the very right edge, where almost none of it is visible and the buttons cannot be reached.
    /// </summary>
    public static int ParkToolbar(IntPtr window, bool portrait)
    {
        Rect monitor = TargetMonitorRect(window, portrait);
        int moved = 0;
        EnumWindows((candidate, _) =>
        {
            if (WindowClass(candidate) == ToolbarClass && WindowTitle(candidate) == ToolbarTitle && IsWindowVisible(candidate))
            {
                Rect rect = WindowRect(candidate);
                if (rect.Left < monitor.Left || rect.Right > monitor.Right - 4 || rect.Left > monitor.Right - 20)
                {
                    int x = monitor.Right - rect.Width - 8;
                    int y = Math.Max(monitor.Top + 10, Math.Min(rect.Top, monitor.Bottom - rect.Height - 10));
                    SetWindowPos(candidate, HwndTop, x, y, rect.Width, rect.Height, SwpShowWindow | SwpNoActivate);
                    moved++;
                }
            }
            return true;
        }, IntPtr.Zero);
        return moved;
    }

    /// <summary>
    /// Hides the console windows the emulator may have opened. Only console class windows whose title names the
    /// emulator or the virtual machine are touched, so a terminal of the user's own is left alone.
    /// </summary>
    public static IReadOnlyList<string> HideConsoleWindows()
    {
        var hidden = new List<string>();
        foreach (string consoleClass in new[] { "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS" })
        {
            EnumWindows((window, _) =>
            {
                if (WindowClass(window) != consoleClass) return true;
                string title = WindowTitle(window);
                string lower = title.ToLowerInvariant();
                if ((lower.Contains("qemu-system") || lower.EndsWith("emulator.exe", StringComparison.Ordinal)) && IsWindowVisible(window))
                {
                    ShowWindow(window, 0);
                    hidden.Add(title);
                }
                return true;
            }, IntPtr.Zero);
        }
        return hidden;
    }
}
