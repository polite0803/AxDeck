using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The window that shows a service's log, read from the file the service writes. The log stays on this machine: the window
// only reads the tail of it.
internal sealed class ServiceLogWindow : ControlDialog
{
    public ServiceLogWindow(SettingsTheme theme, string title, IReadOnlyList<string> lines, string path)
        : base(theme, title)
    {
        Add(Loc.T("日志文件：", "Log file: ") + path, 10, 11.5);
        if (lines.Count == 0)
        {
            Add(ServiceLog.Empty, 14);
            return;
        }
        // The log is read in a fixed-width face, so the columns of a stack trace still line up.
        var text = new TextBlock { Text = ServiceLog.Text(lines, ServiceLog.DefaultLines), FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 12, Foreground = UI.Brush(Palette.Primary), TextWrapping = TextWrapping.NoWrap,
            LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 12, 0, 0) };
        var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 380, Content = text };
        Body.Children.Add(scroller);
        // The newest line is the one the reader came for, so the view starts at the bottom.
        Loaded += (_, _) => scroller.ScrollToEnd();
    }
}
