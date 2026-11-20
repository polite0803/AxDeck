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
    private readonly TextBox _label;
    private readonly ComboBox _project;
    private readonly List<(Guid? Id, string Name, ProviderId Source)> _projects = [];
    private readonly TextBlock _summary;
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

        _label = new TextBox { Style = (Style)FindResource("SettingsField"), Text = app.Focus.Label, Margin = new Thickness(0, 8, 0, 0), MaxLength = 64 };
        AutomationProperties.SetName(_label, Loc.T("工作会话名称", "Work session name"));
        _label.ToolTip = Loc.T("可选。例如：修复登录页、整理发布会材料。", "Optional. For example: fix sign-in, prepare launch notes.");
        body.Children.Add(_label);

        var projectRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        projectRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        projectRow.ColumnDefinitions.Add(new ColumnDefinition());
        var projectLabel = Heading(Loc.T("项目", "Project"), 12.5, palette.Secondary);
        projectLabel.VerticalAlignment = VerticalAlignment.Center;
        projectLabel.Margin = new Thickness(0, 0, 12, 0);
        projectRow.Children.Add(projectLabel);
        _projects.Add((null, Loc.T("不属于项目", "No project"), ProviderId.Codex));
        _projects.AddRange(app.Projects.Projects.Select(project => ((Guid?)project.Id, project.DisplayName, project.WorkSource)));
        _project = new ComboBox
        {
            Style = (Style)FindResource("SettingsCombo"),
            ItemsSource = _projects.Select(project => project.Name).ToList(),
            SelectedIndex = Math.Max(0, _projects.FindIndex(project => project.Id == app.Focus.ProjectId)),
            IsEnabled = _projects.Count > 1,
        };
        AutomationProperties.SetName(_project, Loc.T("工作项目", "Work project"));
        _project.ToolTip = _projects.Count > 1
            ? Loc.T("开始新的专注时，会话会关联到这个项目，并使用它的工作来源。", "A new focus session is linked to this project and uses its work source.")
            : Loc.T("先在设置中登记项目，这里才能选择。", "Register a project in the settings to choose one here.");
        PopupMotion.Attach(_project);
        Grid.SetColumn(_project, 1);
        projectRow.Children.Add(_project);
        body.Children.Add(projectRow);

        var presets = new UniformGrid { Columns = 4, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var minutes in FocusTimer.Presets)
        {
            int value = minutes;
            var button = new Button { Content = $"{value} " + Loc.T("分钟", "min"), Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetName(button, Loc.T($"专注 {value} 分钟", $"Focus for {value} minutes"));
            button.Click += (_, _) =>
            {
                var selected = SelectedProject();
                ProviderId[] sources = selected is { } project ? [project.Source] : [.. _app.Settings.WorkSources];
                _app.FocusStart(value, _label.Text, sources, selected?.Id);
                Update();
            };
            presets.Children.Add(button);
        }
        body.Children.Add(presets);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 0) };
        _pause = new Button { Content = Loc.T("暂停", "Pause"), Style = (Style)FindResource("SettingsButton"), Margin = new Thickness(0, 0, 10, 0) };
        _pause.Click += (_, _) => { if (_app.Focus.Running) _app.FocusPause(); else _app.FocusResume(); Update(); };
        var reset = new Button { Content = Loc.T("重置", "Reset"), Style = (Style)FindResource("SettingsButton") };
        reset.Click += (_, _) => { _app.FocusReset(); Update(); };
        var close = new Button { Content = Loc.T("关闭", "Close"), Style = (Style)FindResource("SettingsPrimaryButton"), Margin = new Thickness(10, 0, 0, 0) };
        close.Click += (_, _) => Close();
        actions.Children.Add(_pause); actions.Children.Add(reset); actions.Children.Add(close);
        body.Children.Add(actions);

        _summary = Heading("", 11.5, palette.Tertiary);
        _summary.Margin = new Thickness(0, 14, 0, 0);
        body.Children.Add(_summary);

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

    private (Guid? Id, string Name, ProviderId Source)? SelectedProject()
        => _project.SelectedIndex > 0 && _project.SelectedIndex < _projects.Count ? _projects[_project.SelectedIndex] : null;

    private void Update()
    {
        var timer = _app.Focus;
        var now = DateTimeOffset.Now;
        _remaining.Text = timer.Running || timer.Shown ? timer.Format(now) : "--:--";
        bool canResume = timer.Running || timer.Shown && timer.EndedAt is null && timer.Remaining(now) > 0;
        _pause.Content = timer.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume");
        _pause.IsEnabled = canResume;
        // The work session is only about local metadata: what is running, when it was last written, and how many samples
        // were kept. The conversation itself is never read or shown.
        var latest = timer.Samples.LastOrDefault();
        var active = latest?.Activity.Where(item => item.IsWorking).Select(item => ProviderCatalog.Name(item.Source)).ToList() ?? [];
        string samples = Loc.T($"{timer.Samples.Count} 个采样", $"{Loc.Count(timer.Samples.Count, "sample", "samples")}");
        _summary.Text = timer.StartedAt is null ? Loc.T("开始后，这里会记录工作来源的活动摘要，不读取对话内容。",
            "Once started, this shows a summary of activity from your work sources; it never reads the conversation.")
            : timer.EndedAt is not null ? Loc.T("这次会话已经结束。选一个时长可以开始新的会话。",
                "This session has ended. Pick a length to start a new one.")
            : Loc.T($"{samples} · 正在工作：{(active.Count == 0 ? "未检测到正在写入" : string.Join("、", active))}",
                $"{samples} · working now: {(active.Count == 0 ? "no recent writes" : string.Join(", ", active))}");
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
