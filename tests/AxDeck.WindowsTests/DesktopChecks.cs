using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxDeck;

// 「桌面」页的两件事：换页的那一下不再把同一个元素加两次，接管桌面的窗口找得到。
internal static class DesktopChecks
{
    public static void Run(Action<string, bool> check)
    {
        CheckSwapOfTheSameElement(check);
        CheckDesktopWindowFound(check);
    }

    // 换页换到同一个元素上：上一版在这里抛
    // 「指定的 Visual 已经是另一个 Visual 的子级或者已经是 CompositionTarget 的根」，
    // 桌面页画的图标整个消失、也不再响应，crash.log 里每秒一条。这个抛法必须回不来。
    private static void CheckSwapOfTheSameElement(Action<string, bool> check)
    {
        var host = new Grid();
        var view = new Grid { Background = Brushes.Transparent };
        host.Children.Add(view);
        try
        {
            // 没有动画、有动画、同一个元素作为新页和旧页：三种都要活着回来。
            MonitorCrossfade.Swap(host, view, view, motion: false);
            MonitorCrossfade.Swap(host, view, view, motion: true);
            MonitorCrossfade.Swap(host, view, view, motion: true);
            check("换页换到同一个元素上不再抛异常", true);
        }
        catch (Exception ex)
        {
            check("换页换到同一个元素上不再抛异常", false);
            Console.WriteLine($"  异常: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        check("换页后视图还挂在面板上", host.Children.Contains(view) && host.Children.Count == 1);
        check("换页后视图不透明", view.Opacity == 1);
    }

    // 接管桌面要找的那扇窗：桌面的图标视图住在类名 SHELLDLL_DefView 的窗口里。
    // 这条只问找不找得到——发不发命令、桌面动不动，得靠人的眼睛看。
    private static void CheckDesktopWindowFound(Action<string, bool> check)
    {
        var method = typeof(AxDeck.Native.ShellDesktop).GetMethod("FindDesktopWindow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null) { check("找得到桌面自己的窗口", false); return; }
        var found = (IntPtr)(method.Invoke(null, null) ?? IntPtr.Zero);
        // 一台正常跑着 Explorer 的机器上桌面的窗口一定在；找不到多半是查找的类名或层级写错了。
        check("找得到桌面自己的窗口", found != IntPtr.Zero);
    }
}
