using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AxDeck.Core;

namespace AxDeck;

// The window that shows the code a text makes, so a phone can read it off the screen. The text itself is shown below it, so
// it is clear what the code carries.
internal sealed class QrCodeWindow : ControlDialog
{
    public QrCodeWindow(SettingsTheme theme, BitmapSource code, string text)
        : base(theme, Loc.T("二维码", "QR code"))
    {
        var box = new Border { Width = 240, Height = 240, Background = Brushes.White, CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var image = new Image { Source = code, Stretch = Stretch.Uniform, Margin = new Thickness(10) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        box.Child = image;
        Body.Children.Add(box);

        var shown = new TextBlock { Text = QrCodes.Describe(text), FontSize = 12.5, Foreground = UI.Brush(Palette.Secondary),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxWidth = 240, Margin = new Thickness(0, 14, 0, 0) };
        Body.Children.Add(shown);
        Add(Loc.T("用手机相机对准这个码即可打开上面的内容。", "Point a phone's camera at the code to open what it carries."), 12);
    }
}
