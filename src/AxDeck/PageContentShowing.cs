using System.Windows;

namespace AxDeck;

// 展开的面板同一时刻只显示一个页面。以前每个 Render* 各自手写一长串 Visibility = Collapsed，新增页面时只要有
// 一处漏掉，旧页面的内容就留在了新页面上（从服务页切到设备页时，「探测站点」「SSH 登录测试」这些按钮还叠在
// 「没有已连接的无线设备」上面）。现在调用方只说明哪个页面要显示，折叠其余所有页面由这里统一做。
internal static class PageContentShowing
{
    // 单独成文件、不依赖 MainWindow：Windows 测试直接链接它，验证「除了要显示的那个，其余全部折叠」。
    public static void ShowOnly(FrameworkElement page, IReadOnlyList<FrameworkElement> pages)
    {
        foreach (var other in pages)
            if (!ReferenceEquals(other, page)) other.Visibility = Visibility.Collapsed;
        page.Visibility = Visibility.Visible;
    }
}