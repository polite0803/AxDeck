using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// 桌面页自己说的话：一个小窗口，说清楚哪里出了问题，或问一个问题等回答。
// 不用系统的消息框：它盖在透明置顶的面板上时很突兀，也不进本程序的无障碍树。
internal static class BoxReport
{
    // 一句说明：这个做不了的事，为什么做不了。
    public static void Error(string message)
    {
        // 测试里没有用户可看：这里接住消息，免得 ShowDialog 把测试挂在那里。
        if (ErrorOverride is { } over) { over(message); return; }
        Say(null, Loc.T("桌面", "Desktop"), message, Loc.T("知道了", "Got it"), danger: true);
    }

    // 测试缝：接住说明文字，不弹窗。跟 DesktopIconsView 的 OpenOverride 一样是一个函数。
    internal static Action<string>? ErrorOverride { get; set; }

    // 一句说明：做完一件事之后告诉用户结果。
    public static void Notice(string title, string message)
        => Say(null, title, message, Loc.T("知道了", "Got it"));

    // 一个问题：做之前先问一句。回答了才做。
    public static bool Confirm(Window? owner, string title, string message, string action)
    {
        var window = new BoxMessageWindow(title, message, action);
        if (owner is not null) window.Owner = owner;
        return window.ShowDialog() == true;
    }

    // 只有一个按钮的窗口：知道了。
    private static void Say(Window? owner, string title, string message, string action, bool danger = false)
    {
        var window = new BoxMessageWindow(title, message, action, single: true, danger: danger);
        if (owner is not null) window.Owner = owner;
        window.ShowDialog();
    }

    // 一个小窗口：标题、说明，和一个或两个按钮。用设置窗口的样子：中性底色，强调色只留给要按的那个按钮。
    private sealed class BoxMessageWindow : Window
    {
        public BoxMessageWindow(string title, string message, string action, bool single = false, bool danger = false)
        {
            var palette = new SettingsPalette(SettingsPalette.IsDark(SettingsTheme.System));
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/SettingsResources.xaml", UriKind.Relative) });
            Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/NeteaseControlResources.xaml", UriKind.Relative) });
            palette.Apply(Resources);
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = single ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            FontFamily = UI.PanelFont;
            Background = UI.Brush(palette.Surface);
            Foreground = UI.Brush(palette.Primary);
            Native.SettingsChrome.Apply(this);
            SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, palette.Dark);

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            var heading = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = UI.Brush(palette.Primary),
                TextWrapping = TextWrapping.Wrap
            };
            heading.Margin = new Thickness(0, 0, 0, 10);
            panel.Children.Add(heading);

            var body = new TextBlock
            {
                Text = message,
                FontSize = 12.5,
                Foreground = UI.Brush(palette.Secondary),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(body);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
            Button? confirm = null;
            if (!single)
            {
                var cancel = new Button { Content = Loc.T("取消", "Cancel"), Style = (Style)FindResource("SettingsOutlineButton"), IsCancel = true };
                AutomationProperties.SetName(cancel, Loc.T("取消", "Cancel"));
                cancel.Click += (_, _) => { DialogResult = false; };
                buttons.Children.Add(cancel);
            }
            confirm = new Button { Content = action, Style = (Style)FindResource(danger ? "SettingsDangerButton" : "SettingsPrimaryButton") };
            AutomationProperties.SetName(confirm, action);
            confirm.Click += (_, _) => { DialogResult = true; };
            buttons.Children.Add(confirm);
            panel.Children.Add(buttons);

            var frame = new Border { BorderThickness = new Thickness(1), Child = panel };
            frame.SetResourceReference(Border.BorderBrushProperty, "SettingsBorder");
            frame.SetResourceReference(Border.BackgroundProperty, "SettingsSurface");
            Content = frame;
            // 取消按钮在键盘上：光按回车不会就删掉东西。
            Loaded += (_, _) => (single ? confirm : buttons.Children[0])?.Focus();
        }
    }
}
