using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AxDeck;

// The pop-up's leaving picture must cover exactly the list that is leaving: its pixels, and where it lies. A picture sized
// for the wrong window's DPI shows the list twice, the larger copy shifted (a drop-down read as two lists, one scaled).
internal static class PopupMotionTests
{
    public static void Run(Action<string, bool> check)
    {
        var host = new Window { Width = 420, Height = 400, Left = 40, Top = 40, ShowActivated = false, WindowStyle = WindowStyle.None };
        var list = new ListBox { Width = 208, Height = 242 };
        foreach (var entry in new[] { "Claude", "Codex", "Antigravity", "Cursor", "Grok Bot", "ZCode", "DeepSeek Harness", "Kimi Code", "OpenCode", "Local app data" })
            list.Items.Add(new ListBoxItem { Content = entry });
        var anchor = new Border { Width = 208, Height = 34, Background = Brushes.Transparent };
        var column = new StackPanel();
        column.Children.Add(anchor);
        host.Content = column;
        try
        {
            host.Show();
            host.UpdateLayout();
            var real = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, Child = list };
            PopupMotion.Attach(real);
            real.IsOpen = true;
            try
            {
                Drain();
                check("the drop-down list is laid out", list.ActualWidth > 100 && list.ActualHeight > 100);
                var (ghost, picture) = Placed(real) ?? throw new InvalidOperationException("the pop-up's picture was not prepared");
                try
                {
                    LayOut(ghost);
                    // The picture as PopupMotion takes it; called directly so the check does not wait for a timer tick.
                    Take(list, picture);
                    LayOut(ghost);
                    var from = VisualTreeHelper.GetDpi(list);
                    var to = VisualTreeHelper.GetDpi(picture);
                    var bitmap = picture.Source as BitmapSource;
                    check("the leaving picture covers the list one to one when both windows have the same DPI",
                        Math.Abs(picture.ActualWidth * to.DpiScaleX - list.ActualWidth * from.DpiScaleX) < 1
                        && Math.Abs(picture.ActualHeight * to.DpiScaleY - list.ActualHeight * from.DpiScaleY) < 1);
                    check("the leaving picture has the list's pixels",
                        bitmap is not null
                        && bitmap.PixelWidth == (int)Math.Ceiling(list.ActualWidth * from.DpiScaleX)
                        && bitmap.PixelHeight == (int)Math.Ceiling(list.ActualHeight * from.DpiScaleY));
                    var (pictureLeft, pictureTop) = ScreenBounds(picture);
                    var (listLeft, listTop) = TopLeft(list);
                    check("the leaving picture starts at the list's screen position",
                        bitmap is not null && pictureLeft == listLeft && pictureTop == listTop);
                }
                finally { ghost.IsOpen = false; }
            }
            finally
            {
                real.IsOpen = false;
                Drain();
                check("closing the pop-up clears its leaving picture", Placed(real) is null);
            }
        }
        finally { host.Close(); }
    }

    private static void Take(FrameworkElement content, Image image) =>
        typeof(PopupMotion).GetMethod("Take", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { content, image });

    private static (Popup Ghost, Image Picture)? Placed(Popup popup)
    {
        var places = typeof(PopupMotion).GetField("Places", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var arguments = new object?[] { popup, null };
        if (places.GetType().GetMethod("TryGetValue")!.Invoke(places, arguments) is not true) return null;
        var placed = arguments[1]!;
        var ghost = (Popup?)placed.GetType().GetField("Ghost", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .GetValue(placed);
        var picture = (Image?)placed.GetType().GetField("Picture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)!
            .GetValue(placed);
        return ghost is null || picture is null ? null : (ghost, picture);
    }

    private static void LayOut(Popup popup)
    {
        popup.UpdateLayout();
        Drain();
    }

    private static (int X, int Y) TopLeft(FrameworkElement element)
    {
        var source = PresentationSource.FromVisual(element) as HwndSource;
        return source is null || !GetWindowRect(source.Handle, out var rect)
            ? (0, 0)
            : (rect.Left, rect.Top);
    }

    private static (int Left, int Top) ScreenBounds(FrameworkElement element) => TopLeft(element);

    // Drains the dispatcher, so a pop-up created above is laid out and has a DPI of its own.
    private static void Drain()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);
}
