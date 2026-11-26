using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services & tools page's settings: only whether it shows and its size, set with the other pages under 展开外观.
// The page itself (ServicesPanel.cs) reads the personal Cloudflare tunnel infrastructure live while it is open.
public partial class SettingsWindow
{
    // 设置窗口内部的页号，不写进 settings.json，也不占 DeckPage 的编号。19 排在软件更新（18）之后。
    internal const int ServicesActionsPage = 19;

    private ServicesActionsView? _servicesActions;
    internal bool ServicesActionsVisible => IsVisible && WindowState != WindowState.Minimized && _page == ServicesActionsPage;

    // 服务与工具的操作页：状态读数复用面板的 ServicesView（去掉跳转按钮），操作全部交给 MainWindow 的控制器。
    private void ServicesActions()
    {
        _servicesActions = new ServicesActionsView(_app.Deck);
        Target.Children.Add(_servicesActions);
        _app.Deck.ServicesSettingsOpen(_servicesActions);
    }

    internal void ServicesPageLeaving()
    {
        if (_servicesActions is null) return;
        _app.Deck.ServicesSettingsClose();
        _servicesActions = null;
    }

    private void ServicesSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.ServicesWidth, 440, 1200, value => Resize(s => s.ServicesWidth = value, DeckPage.Services)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.ServicesHeight, 140, 400, value => Resize(s => s.ServicesHeight = value, DeckPage.Services)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
