using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The window that shows what a picture holds: the code in it, and the text in it. The code is read out of the picture and the
// text is recognized on the machine itself, so nothing leaves this PC.
internal sealed class PictureReaderWindow : ControlDialog
{
    public PictureReaderWindow(SettingsTheme theme, BitmapSource picture, string? code, IReadOnlyList<string> lines, bool recognized)
        : base(theme, Loc.T("图片内容", "What's in the picture"))
    {
        double side = 208;
        if (code is { Length: > 0 })
        {
            var box = new Border { Width = side, Height = side, Background = Brushes.White, CornerRadius = new CornerRadius(8),
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            var image = new Image { Source = QrCodeImage.Encode(code, 360), Stretch = Stretch.Uniform, Margin = new Thickness(8) };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            box.Child = image;
            Body.Children.Add(box);
            Add(code, 14, 12.5, Palette.Primary);
            if (QrCodes.IsLink(code))
            {
                var open = new Button { Content = Loc.T("在浏览器中打开", "Open in browser"), Style = (Style)FindResource("SettingsButton"),
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                open.Click += (_, _) => { OpenFile(code); Close(); };
                Body.Children.Add(open);
            }
        }
        else
        {
            Add(Loc.T("这张图片里没有二维码", "This picture holds no QR code"), 12, 13, Palette.Secondary);
        }

        Add(Loc.T("图片中的文字", "The text in the picture"), 22, 13.5, Palette.Primary, true);
        if (!recognized)
        {
            Add(Loc.T("系统没有可用的文字识别组件，无法读取图片中的文字。", "The system has no text recognizer, so the text in the picture cannot be read."), 8);
            return;
        }
        if (lines.Count == 0)
        {
            Add(Loc.T("没有识别到文字。", "No text was found."), 8);
            return;
        }
        var text = new TextBlock { Text = string.Join("\n", lines), FontSize = 13, Foreground = UI.Brush(Palette.Primary),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 8, 0, 0) };
        Body.Children.Add(text);
        var copy = new Button { Content = Loc.T("复制文字", "Copy the text"), Style = (Style)FindResource("SettingsButton"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        copy.Click += (_, _) => { Clipboard.SetText(string.Join("\n", lines)); Close(); };
        Body.Children.Add(copy);
    }

    private static void OpenFile(string path)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { }
    }
}
