using System.Windows;
using System.Windows.Controls;

namespace AxDeck;

// 展开的面板同一时刻只显示一个页面：从服务页切到设备页时，服务页的按钮曾经还留在设备页上。
// 这里验证统一收口的那一步——要显示的那个可见，其余全部折叠，无论调用顺序如何。
internal static class PageContentShowingTests
{
    public static void Run(Action<string, bool> check)
    {
        var usage = new Grid(); var music = new Grid(); var monitor = new Grid();
        var shelf = new Grid(); var devices = new Grid(); var services = new Grid(); var desktop = new Grid();
        var pages = new FrameworkElement[] { usage, music, monitor, shelf, devices, services, desktop };
        // 起始状态：全部可见，模拟上一个页面把内容留在了屏幕上。
        foreach (var page in pages) page.Visibility = Visibility.Visible;

        PageContentShowing.ShowOnly(services, pages);
        check("showing the services page hides every other page",
            services.Visibility == Visibility.Visible && pages.Where(page => page != services).All(page => page.Visibility == Visibility.Collapsed));

        // 切到设备页：服务页必须随之折叠，这正是截图上那些残留按钮的来源。
        PageContentShowing.ShowOnly(devices, pages);
        check("moving from the services page to the devices page leaves no services content behind",
            devices.Visibility == Visibility.Visible && services.Visibility == Visibility.Collapsed
            && pages.Count(page => page.Visibility == Visibility.Visible) == 1);

        // 切回已经折叠的页面同样生效。
        PageContentShowing.ShowOnly(usage, pages);
        check("a page switched back into view is shown alone again",
            usage.Visibility == Visibility.Visible && pages.Count(page => page.Visibility == Visibility.Visible) == 1);

        // 已经在对的页面上再显示一次，结果不变（每次刷新都会走这一步）。
        PageContentShowing.ShowOnly(usage, pages);
        check("showing the page already on screen changes nothing",
            usage.Visibility == Visibility.Visible && pages.Count(page => page.Visibility == Visibility.Visible) == 1);
    }
}