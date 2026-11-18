using System.Windows;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The focus timer's countdown in the collapsed row: the time that is left, in the row's own face. The width is reserved for
// the longest figure the countdown can show, so the row does not move as the seconds pass.
internal sealed class FocusBadge : Grid
{
    private readonly TextBlock _text;

    public FocusBadge(double width, double height, double fontSize, string color)
    {
        Width = width; Height = height;
        Background = Brushes.Transparent;
        _text = new TextBlock
        {
            FontFamily = UI.PanelFont, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Foreground = UI.Brush(color),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        };
        Typography.SetNumeralAlignment(_text, FontNumeralAlignment.Tabular);
        Children.Add(_text);
    }

    // The countdown as it is shown.
    public void Set(string text) => _text.Text = text;

    // The width the countdown takes: the longest figure it can show, so the row keeps its width from second to second.
    public static double ReservedWidth(double fontSize)
    {
        var probe = new TextBlock { FontFamily = UI.PanelFont, FontSize = fontSize, FontWeight = FontWeights.SemiBold, Text = "00:00:00" };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Ceiling(probe.DesiredSize.Width) + 1;
    }
}
