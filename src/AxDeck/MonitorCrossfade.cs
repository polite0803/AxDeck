using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;

// The monitor page giving way to a new build of itself (a resize across a layout step, a sensor found or missing, a new main
// gauge): the new page fades in while every page it replaces fades out from where it is, so their opacities always add up to one
// and the page never dims, even when a drag crosses several steps in quick succession. Nothing jumps to its new place in one
// frame; the pages that leave take no input. Without animations the new page simply replaces them.
//
// 换页的淡入淡出，单独放在一个文件里：桌面页（MainWindow.Desktop.cs）也用它换自己的视图，
// 测试也单独考它——上一版桌面页每秒把自己的视图既当新页又当旧页传进来，同一个 Visual
// 被加了两次，框架当场抛异常，桌面页画的图标整个消失（crash.log 里每秒一条）。
internal static class MonitorCrossfade
{
    private const double Seconds = Eased.Seconds * .6;

    public static void Swap(Panel host, FrameworkElement next, FrameworkElement old, bool motion)
    {
        // 同一个元素既是新页又是旧页时没有「换」可言：调用方以为旧页还在动画里没走完，
        // 其实那正是它自己。直接接管，别走进淡入那段——那里会把同一个 Visual 加两次，框架当场抛异常。
        if (ReferenceEquals(next, old)) { host.Children.Clear(); host.Children.Add(next); next.Opacity = 1; return; }
        if (!motion || !host.Children.Contains(old)) { host.Children.Clear(); host.Children.Add(next); return; }
        var leaving = host.Children.OfType<UIElement>().Select(child => (Element: child, From: child.Opacity)).ToList();
        foreach (var (element, _) in leaving) { element.IsHitTestVisible = false; element.BeginAnimation(UIElement.OpacityProperty, null); }
        // The opacities the leaving pages had, after any animation of their own was taken off.
        leaving = [.. leaving.Select(page => (page.Element, page.Element.Opacity))];
        next.Opacity = 0;
        host.Children.Add(next);
        var fade = new Eased(0, Seconds);
        fade.Set(1, true);
        Motion.Start(host, now =>
        {
            double t = Math.Clamp(fade.At(now), 0, 1);
            next.Opacity = t;
            foreach (var (element, from) in leaving) element.Opacity = from * (1 - t);
            if (fade.Moving(now)) return true;
            next.Opacity = 1;
            foreach (var (element, _) in leaving) host.Children.Remove(element);
            return false;
        });
    }
}
