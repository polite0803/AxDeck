using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxDeck;

public partial class MainWindow
{
    // The clock is not one page's to show: every page asks for it, and the toolbar's middle column decides where it goes. A page
    // that wants it (ShowClock(true)) and a page that hides it for its own reasons (the usage details, the clipboard's full-content
    // view) set the wish; whether it is drawn is the wish and the room the page switch and the right-hand group leave between them.
    // A narrow page would otherwise clip the clock against its neighbours, which reads as a broken toolbar rather than as a
    // crowded one.
    private bool _clockWanted = true;
    // The air the clock keeps on either side of it, so two items never meet.
    private const double ClockRoom = 4;

    private void ShowClock(bool wanted) { _clockWanted = wanted; ApplyClock(); }
    private void Toolbar_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyClock();

    private void ApplyClock()
    {
        // The column is laid out before this runs on the first pass; until it has a width nothing is decided, and the page's wish
        // stands, so the clock is never stripped from a panel that has simply not been measured yet.
        double column = ClockColumn.ActualWidth;
        bool fits = column <= 0 || _clockWanted && Fits(column);
        ClockLabel.Visibility = _clockWanted && fits ? Visibility.Visible : Visibility.Collapsed;

        bool Fits(double room)
        {
            string text = ClockLabel.Text;
            if (text.Length == 0) return true;
            // Measured afresh: a localized time, a larger user font or a different theme change what the clock needs.
            double width = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(UI.PanelFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), ClockLabel.FontSize,
                Brushes.White, 1).WidthIncludingTrailingWhitespace;
            return width + ClockRoom * 2 <= room;
        }
    }
}