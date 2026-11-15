using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 「桌面整理」页面的控制器：分组、规则、原生桌面图标，都在这里改。
// 数据存在 settings.json 里（DeckSettings.Boxes），所以这一页不需要另存一份。
public partial class MainWindow
{
    private BoxService? _boxesService;
    private BoxesView? _boxesView;
    private string _boxesKey = "";

    // 整理器本身：所有面板共用一份，改动大家都看得见。
    internal BoxService Boxes => _boxesService ??= new BoxService();

    // 程序启动时把保存的数据读进来：分组里收着的系统图标，在收进分组的那一刻就被藏起来了，
    // 所以数据必须比退出早一步到位，退出时才放得回来。
    internal void BoxesLoad()
    {
        if (_boxesService is not null) return;
        _boxesService = new BoxService();
        _boxesService.Load(Settings.Boxes ?? new DasktopData());
        // 整理器里的东西变了：保存一次，页面自己重画。
        Settings.Boxes = _boxesService.Snapshot();
    }
    // 页面第一次打开时把保存的数据读进来。
    private void RenderBoxes()
    {
        bool shown = BoxesContent.Visibility == Visibility.Visible && _boxesView is not null && BoxesContent.Children.Contains(_boxesView);
        RefreshStatus();
        UsageContent.Visibility = Visibility.Collapsed; MusicContent.Visibility = Visibility.Collapsed; ShelfContent.Visibility = Visibility.Collapsed;
        DevicesContent.Visibility = Visibility.Collapsed; MonitorContent.Visibility = Visibility.Collapsed; ServicesContent.Visibility = Visibility.Collapsed;
        BoxesContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = Visibility.Collapsed; ApplicationPages.Visibility = Visibility.Collapsed; ClockLabel.Visibility = Visibility.Visible;
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        BoxesContent.Margin = new Thickness(_header.Side, 0, _header.Side, 10);

        // 数据在程序启动时就读进来了：它跟着保存走，不必每次开页面都重读。
        var key = "boxes";
        if (_boxesView is not null && key == _boxesKey && BoxesContent.Children.Contains(_boxesView)) return;
        _boxesKey = key;
        var old = shown ? _boxesView : null;
        _boxesView = new BoxesView(this, Boxes);
        if (old is not null) MonitorCrossfade.Swap(BoxesContent, _boxesView, old, Soft.Enabled);
        else
        {
            BoxesContent.Children.Clear(); BoxesContent.Children.Add(_boxesView);
        }
        _boxesView.Refresh();
    }

    // 整理器里的东西变了：保存一次，再让页面重画。
    internal void BoxesChanged()
    {
        if (_boxesService is null) return;
        Settings.Boxes = _boxesService.Snapshot();
        _app.QueueSave();
        _boxesView?.Refresh();
    }
}
