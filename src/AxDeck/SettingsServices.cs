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
    private void ServicesSizeControls()
    {
        var dimensions = new Grid { Margin = new Thickness(0, 8, 0, 0) }; dimensions.ColumnDefinitions.Add(new()); dimensions.ColumnDefinitions.Add(new());
        var width = Dimension(Loc.T("宽度", "Width"), S.ServicesWidth, 440, 1200, value => Resize(s => s.ServicesWidth = value, DeckPage.Services)); width.Margin = new Thickness(0, 0, 20, 0); dimensions.Children.Add(width);
        var height = Dimension(Loc.T("高度", "Height"), S.ServicesHeight, 140, 400, value => Resize(s => s.ServicesHeight = value, DeckPage.Services)); height.Margin = new Thickness(20, 0, 0, 0); Grid.SetColumn(height, 1); dimensions.Children.Add(height);
        Target.Children.Add(dimensions);
    }
}
