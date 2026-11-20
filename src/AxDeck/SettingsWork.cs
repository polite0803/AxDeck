using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// The development work page's settings: only whether it shows and its size, with the other pages under 展开外观.
// The page itself reads the focus timer, recent projects, AI activity and the clipboard shelf live while it is open.
public partial class SettingsWindow
{
    private void WorkSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        dimensions.ColumnDefinitions.Add(new());
        dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.WorkWidth, DeckPages.MinWidth, DeckPages.MaxWidth, value => Resize(s => s.WorkWidth = value, DeckPage.Work));
        width.Margin = new Thickness(0, 0, 20, 0);
        dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.WorkHeight, DeckPages.MinHeight, DeckPages.MaxHeight, value => Resize(s => s.WorkHeight = value, DeckPage.Work));
        height.Margin = new Thickness(20, 0, 0, 0);
        Grid.SetColumn(height, 1);
        dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
