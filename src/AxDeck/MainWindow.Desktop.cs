using System.IO;
using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 「桌面」页：桌面上的东西以图标画在展开的面板里，真实的桌面图标在 AxDeck 运行期间是藏起来的。
//
// 这就是「把桌面搬进 AxDeck」这件事本身：程序在，桌面干净、只剩 AxDeck 这一块；程序退出，桌面原样回去。
// 桌面长什么样（文件、快捷方式、系统项）由 ShellDesktop 问出来，排顺序由 Core.DesktopItems 算，
// 画和操作由 DesktopIconsView 做，这里只负责挂上去、以及接管和交还桌面。
public partial class MainWindow
{
    private DesktopIconsView? _desktopView;
    // 接管前的桌面图标状态：程序退出时照它写回去。
    private Core.DesktopTakeOver.State _desktopState = Core.DesktopTakeOver.State.Untouched;

    private void RenderDesktop()
    {
        bool shown = DesktopContent.Visibility == Visibility.Visible && _desktopView is not null && DesktopContent.Children.Contains(_desktopView);
        RefreshStatus();
        UsageContent.Visibility = Visibility.Collapsed; MusicContent.Visibility = Visibility.Collapsed; ShelfContent.Visibility = Visibility.Collapsed;
        DevicesContent.Visibility = Visibility.Collapsed; MonitorContent.Visibility = Visibility.Collapsed; ServicesContent.Visibility = Visibility.Collapsed;
        DesktopContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = Visibility.Collapsed; ApplicationPages.Visibility = Visibility.Collapsed; ClockLabel.Visibility = Visibility.Visible;
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        DesktopContent.Margin = new Thickness(_header.Side, 0, _header.Side, 10);

        // 桌面上的东西随时会变（别处删了、新存了文件），所以每次回到这一页都重画一遍。
        // 内容没变时 Refresh 自己会跳过，重画不贵。
        var old = shown ? _desktopView : null;
        _desktopView ??= new DesktopIconsView(this);
        if (old is not null) MonitorCrossfade.Swap(DesktopContent, _desktopView, old, Soft.Enabled);
        else { DesktopContent.Children.Clear(); DesktopContent.Children.Add(_desktopView); }
        _desktopView.Refresh();
    }

    // 桌面页开着的时候系统菜单也开着：这期间面板不能收起来，否则菜单跟着一起没了。
    internal bool DesktopMenuOpen => _desktopView?.MenuOpen == true;

    // 打开「桌面整理」这一页（分组那套设置）：从桌面页的右键菜单里进。
    internal void DesktopOpenSettings() => _app.OpenSettings();

    // 打开 AxDeck 的设置窗口，停在「展开外观」：这一页的开关和尺寸都在那里。
    internal void DesktopOpenPageSettings() => _app.OpenSettingsPage(2);

    // ---- 接管桌面 ----

    // 藏图标之前落的记号，交还之后删掉。记号还在，说明上一次运行是被强制结束的（任务管理器、崩溃、断电），
    // 桌面图标还藏着，而"原来是什么样"已经跟着进程没了。
    private string DesktopMarkerPath => Path.Combine(_app.Store.DirectoryPath, "desktop-taken");

    // 启动时调用：把 Windows 自己画的桌面图标藏起来，桌面上的东西此后只在 AxDeck 里看得见。
    // 用户可能本来就关掉了桌面图标，也可能在设置里关了接管；两种情况下都不动它。
    internal void DesktopTake()
    {
        _desktopState = Core.DesktopTakeOver.Take(ShellDesktop.IconsVisible(), Settings.DesktopTakeOver);
        if (!Core.DesktopTakeOver.ShouldHide(_desktopState)) return;
        WriteMarker();
        ShellDesktop.SetIconsVisible(false);
    }

    // 上一次运行没走到交还那一步：把桌面图标放回去。记号只有在真的藏过图标时才存在，
    // 所以放回去就是它原来的样子（能被藏起来，前提本来就是它当时显示着）。
    internal void DesktopRecover()
    {
        try
        {
            if (!Core.DesktopTakeOver.NeedsRecovery(File.Exists(DesktopMarkerPath))) return;
            // 接管是关掉的，但上一次运行把它藏了：那一次用户是开着的，桌面该照上次的样子回去。
            ShellDesktop.SetIconsVisible(true);
            File.Delete(DesktopMarkerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // 退出时调用：把桌面图标放回去，放回的是接管前记下的那个样子。
    internal void DesktopRestore()
    {
        // 记号无论如何都要清：留着它，下次启动会把一张本来好好的桌面又翻动一遍。
        ClearMarker();
        if (!Core.DesktopTakeOver.ShouldRestore(_desktopState)) return;
        ShellDesktop.SetIconsVisible(Core.DesktopTakeOver.RestoreTo(_desktopState));
        _desktopState = Core.DesktopTakeOver.State.Untouched;
    }

    // 落记号。写不出来不算错：没有它，最坏也只是强杀之后要用户自己把桌面图标打开一次。
    private void WriteMarker()
    {
        try
        {
            Directory.CreateDirectory(_app.Store.DirectoryPath);
            File.WriteAllText(DesktopMarkerPath, DateTimeOffset.Now.ToString("O"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void ClearMarker()
    {
        try { if (File.Exists(DesktopMarkerPath)) File.Delete(DesktopMarkerPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    // 接管开关在设置里改了：现在就按新的选择接管或交还，不必重启。
    internal void DesktopTakeOverChanged()
    {
        if (Settings.DesktopTakeOver) DesktopTake();
        else
        {
            DesktopRestore();
            _desktopView?.Refresh(force: true);
        }
    }
}
