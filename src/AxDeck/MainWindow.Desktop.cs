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
        // 真实桌面图标的位置读写只在这里注入：设置窗口之外的测试搭视图都拿不到它，CI 也就碰不到用户的桌面。
        _desktopView ??= new DesktopIconsView(this, new ShellDesktopIconPositions());
        if (old is not null) MonitorCrossfade.Swap(DesktopContent, _desktopView, old, Soft.Enabled);
        else { DesktopContent.Children.Clear(); DesktopContent.Children.Add(_desktopView); }
        _desktopView.Refresh();
        // 这一页在屏幕上了：开始盯着两个桌面文件夹，文件一变就重画。
        _desktopView.BeginWatching();
    }

    // 桌面页上的菜单开着：这期间面板不能收起来，否则菜单跟着一起没了。
    // 包括 Explorer 那份系统菜单，也包括这一页自己的 ContextMenu。
    internal bool DesktopMenuOpen => _desktopView?.MenuOpen == true;

    // 桌面页的菜单关了：它开着的时候指针多半已经不在面板上，收起判断当时是被挡住的。
    // 现在重新走一遍离开流程；不然菜单在面板外面选完，面板会一直展开着。
    internal void MenuClosed()
    {
        if (_expanded && !_pointerInside) PointerChanged(false);
    }

    // Esc 先收这一页的菜单，再轮到收面板。返回真表示菜单收掉了，Esc 这一下就用完了。
    // 系统菜单是阻塞的，Esc 到不了这里（用户在菜单上按 Esc 时菜单自己收）；
    // 要拦的是这一页自己的 ContextMenu——不然 Esc 会把它和面板一起收掉。
    internal bool DesktopEscape()
    {
        if (_desktopView is null || !_desktopView.PageMenuOpen) return false;
        _desktopView.ClosePageMenu();
        return true;
    }

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
        _desktopState = Core.DesktopTakeOver.Take(OriginalIconsVisible(), Settings.DesktopTakeOver);
        if (!Core.DesktopTakeOver.ShouldHide(_desktopState)) return;
        WriteMarker();
        ShellDesktop.SetIconsVisible(false);
    }

    // 接管前桌面图标的样子。算数的来源是 Explorer 现在真的画没画（SetIconsVisible 也认这一个），
    // 注册表只是后备：Explorer 只在切换时写回注册表，崩过或没写完时两边说的不是一回事，
    // 拿注册表当真就会在退出时把用户本来藏着的图标又打开（或者反过来）。
    private static bool OriginalIconsVisible() => ShellDesktop.DrawnIconsVisible() ?? ShellDesktop.IconsVisible();

    // 上一次运行没走到交还那一步：把桌面图标放回去。记号只有在真的藏过图标时才存在，
    // 所以放回去就是它原来的样子（能被藏起来，前提本来就是它当时显示着）。
    internal void DesktopRecover()
    {
        try
        {
            if (!Core.DesktopTakeOver.NeedsRecovery(File.Exists(DesktopMarkerPath))) return;
            // 接管是关掉的，但上一次运行把它藏了：那一次用户是开着的，桌面该照上次的样子回去。
            // 已经是要的样子就不动：这次启动多半马上又要接管（设置里还开着），先显后藏只会让桌面闪一下。
            // SetIconsVisible 自己会核对当前画的样子，跳过多余的切换；记号照样清，恢复义务到此两清。
            if (ShellDesktop.DrawnIconsVisible() != true) ShellDesktop.SetIconsVisible(true);
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
