using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// 「桌面」页的设置：尺寸，以及 AxDeck 运行期间要不要接管真实桌面（把 Windows 自己画的图标藏起来）。
// 跟别的页面一样放在「展开外观」里的一行下面。
public partial class SettingsWindow
{
    private void DesktopSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.DesktopWidth, 440, 1200, value => Resize(s => s.DesktopWidth = value, DeckPage.Desktop)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.DesktopHeight, 140, 400, value => Resize(s => s.DesktopHeight = value, DeckPage.Desktop)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);

        // 接管桌面：AxDeck 在的时候把 Windows 的桌面图标藏起来，退出时照原样放回去。
        var takeOver = Toggle(Loc.T("运行时接管桌面", "Take over the desktop while running"), S.DesktopTakeOver,
            value => { Change(s => s.DesktopTakeOver = value); _app.Deck.DesktopTakeOverChanged(); },
            help: Loc.T("打开后，AxDeck 运行期间 Windows 自己的桌面图标是藏起来的，桌面上的东西只在 AxDeck 的这一页里看得见；AxDeck 退出时图标照原样放回去。关掉它，真实桌面照旧由 Windows 画，这一页只是多一份镜像。",
                "When on, Windows' own desktop icons are hidden while AxDeck runs, so the desktop's contents are only visible on this page; they come back as they were when AxDeck exits. With it off, Windows draws its desktop as usual and this page is a second copy."));
        takeOver.Margin = new Thickness(0, 10, 0, 0);
        Target.Children.Add(takeOver);
    }
}
