using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The window the focus timer is set in: the lengths it offers, the time that is left, and the three things that can be done
// to it — start, pause and reset. The countdown keeps running while the window is closed, and while AxDeck itself is not
// open; the state is in the timer's own file, on this machine only.
internal sealed class FocusTimerWindow : Window
{
    private readonly App _app;
    private readonly TextBlock _remaining;
    private readonly Button _pause;
    private PromptTimer? _tick;

    public FocusTimerWindow(App app)
    {
        _app = app;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AxDeck;component/SettingsResources.xaml", UriKind.Relative) });
        var palette = new SettingsPalette(SettingsPalette.IsDark(app.Settings.Theme));
        palette.Apply(Resources);
        Style = (Style)FindResource(typeof(Window));
        Title = Loc.T("专注计时", "Focus timer");
        Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; ShowInTaskbar = false;
        FontFamily = UI.PanelFont; Background = UI.Brush(palette.Surface); Foreground = UI.Brush(palette.Primary);
        Native.SettingsChrome.Apply(this);
        SourceInitialized += (_, _) => Native.SettingsChrome.SetTheme(this, palette.Dark);

        var body = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        body.Children.Add(Heading(Loc.T("专注计时", "Focus timer"), 19, palette.Primary, true));
        body.Children.Add(Heading(Loc.T("选一个时长开始，计时会在面板收起时继续走。", "Pick a length to start; the countdown keeps running while the panel is closed."), 12.5, palette.Secondary));

        _remaining = Heading("", 34, palette.Primary, true);
        _remaining.HorizontalAlignment = HorizontalAlignment.Center;
        _remaining.Margin = new Thickness(0, 18, 0, 4);
        Typography.SetNumeralAlignment(_remaining, FontNumeralAlignment.Tabular);
        body.Children.Add(_remaining);

        var presets = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var minutes in FocusTimer.Presets)
        {
            int value = minutes;
            var button = new Button { Content = $"{value} " + Loc.T("分钟", "min"), Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(button, Loc.T($"专注 {value} 分钟", $"Focus for {value} minutes"));
            button.Click += (_, _) => { _app.FocusStart(value); Update(); };
            presets.Children.Add(button);
        }
        body.Children.Add(presets);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        _pause = new Button { Content = Loc.T("暂停", "Pause"), Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(0, 0, 10, 0) };
        _pause.Click += (_, _) => { if (_app.Focus.Running) _app.FocusPause(); else _app.FocusStart(Math.Max(1, _app.Focus.TotalSeconds / 60)); Update(); };
        var reset = new Button { Content = Loc.T("重置", "Reset"), Style = (Style)FindResource("SettingsButton") };
        reset.Click += (_, _) => { _app.FocusReset(); Update(); };
        var close = new Button { Content = Loc.T("关闭", "Close"), Style = (Style)FindResource("SettingsPrimaryButton"), Margin = new Thickness(10, 0, 0, 0) };
        close.Click += (_, _) => Close();
        actions.Children.Add(_pause); actions.Children.Add(reset); actions.Children.Add(close);
        body.Children.Add(actions);

        body.Children.Add(Heading(Loc.T("计时只记在这台电脑上，不上传任何地方。", "The countdown is kept on this computer only; nothing is sent anywhere."), 11.5, palette.Tertiary));
        ((TextBlock)body.Children[^1]).Margin = new Thickness(0, 16, 0, 0);

        Content = new Border { Background = UI.Brush(palette.Surface), Child = body };
        Update();
        // The countdown is read once a second while this window is open; a prompt timer keeps the figure right even while
        // the mouse moves.
        _tick = new PromptTimer(Dispatcher, Update) { Interval = TimeSpan.FromSeconds(1) };
        _tick.Start();
        Closed += (_, _) => { _tick?.Stop(); _tick = null; };
    }

    private void Update()
    {
        var timer = _app.Focus;
        _remaining.Text = timer.Running || timer.Shown ? timer.Format(DateTimeOffset.Now) : "--:--";
        _pause.Content = timer.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume");
    }

    private static TextBlock Heading(string text, double size, string color, bool bold = false)
    {
        var block = UI.Line(text, size, color);
        if (bold) block.FontWeight = FontWeights.SemiBold;
        block.TextWrapping = TextWrapping.Wrap;
        block.LineHeight = size * 1.5;
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        return block;
    }
}
