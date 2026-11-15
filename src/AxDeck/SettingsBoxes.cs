using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// 「桌面整理」页面的设置：只有是否显示和它的尺寸，跟别的页面一样放在「展开外观」里。
// 这一页本身（BoxesPanel.cs）管着分组、规则和原生桌面图标。
public partial class SettingsWindow
{
    private void BoxesSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.BoxesWidth, 440, 1200, value => Resize(s => s.BoxesWidth = value, DeckPage.Boxes)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.BoxesHeight, 140, 400, value => Resize(s => s.BoxesHeight = value, DeckPage.Boxes)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
