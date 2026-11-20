using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using AxDeck.Core;

namespace AxDeck;

// The development work view's parts that can be built and laid out without starting the app. MainWindow passes itself as the
// host in production; the WPF tests pass a small double. Updating the view only changes text or button actions, so a refresh
// never rebuilds the visual tree under the pointer.
internal interface IWorkViewHost
{
    IReadOnlyList<ProjectServiceOption> WorkServices { get; }
    ProjectEntry? WorkProject(Guid? id);
    void WorkOpenProject(Guid id);
    void WorkRestoreProject(Guid id);
    void WorkFocusStart(Guid? projectId = null);
    void WorkFocusReset();
    string? WorkServiceCommand(Guid id);
    void WorkPasteServiceCommand(string command);
    void WorkPasteClip(Guid id);
    void WorkPasteGroup(Guid id);
    void WorkOpenSettings();
    void FocusOpen();
}

internal sealed class WorkView : Grid
{
    private static readonly string OkColor = "#3FB950", WarnColor = "#D29922", IdleColor = "#8B949E";
    private readonly IWorkViewHost _owner;
    private readonly StackPanel _focusPanel = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly StackPanel _projectsPanel = new();
    private readonly StackPanel _activityPanel = new();
    private readonly StackPanel _clipsPanel = new();
    private readonly StackPanel _groupsPanel = new();
    private readonly TextBlock _focusTime = UI.Fixed("", 30, 36, UI.Primary, FontWeights.SemiBold);
    private readonly TextBlock _focusState = UI.Text("", 12, UI.Secondary);
    private readonly Button _focusToggle = Button("", 11.5, 22, () => { });
    private readonly List<WorkRow> _projectRows = [], _activityRows = [], _clipRows = [], _groupRows = [];

    public WorkView(IWorkViewHost owner)
    {
        _owner = owner;
        Margin = new Thickness(2, 2, 2, 2);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel { Margin = new Thickness(0, 4, 6, 4) };
        content.Children.Add(BuildFocus());
        content.Children.Add(Section(Loc.T("今日工作态势", "Today's work")));
        content.Children.Add(_overviewPanel);
        content.Children.Add(Section(Loc.T("最近项目", "Recent projects")));
        content.Children.Add(_projectsPanel);
        content.Children.Add(Section(Loc.T("AI 工作活动", "AI work activity")));
        content.Children.Add(_activityPanel);
        content.Children.Add(Section(Loc.T("最近文本", "Recent text")));
        content.Children.Add(_clipsPanel);
        content.Children.Add(Section(Loc.T("文件组", "File groups")));
        content.Children.Add(_groupsPanel);
        scroll.Content = content;
        Children.Add(scroll);

        var foot = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        foot.Children.Add(new Border { Height = 1, Background = UI.Fade(UI.Hairline, vertical: false, tails: .08), Margin = new Thickness(0, 0, 0, 6) });
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 0, -4, 0) };
        bar.Children.Add(Button(Loc.T("添加项目", "Add project"), 11.5, 22, _owner.WorkOpenSettings));
        bar.Children.Add(Button(Loc.T("专注设置", "Focus timer"), 11.5, 22, _owner.FocusOpen));
        foot.Children.Add(bar);
        Grid.SetRow(foot, 1);
        Children.Add(foot);
    }

    private FrameworkElement BuildFocus()
    {
        var panel = new Border
        {
            Background = UI.Brush(UI.Fill), CornerRadius = new CornerRadius(PanelTheme.Corner(8)),
            Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 12),
        };
        _focusTime.HorizontalAlignment = HorizontalAlignment.Left;
        _focusState.TextWrapping = TextWrapping.Wrap;
        _focusState.Margin = new Thickness(0, 2, 0, 0);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, 6, -4, 0) };
        actions.Children.Add(Button(Loc.T("开始 25 分钟", "Start 25 minutes"), 11.5, 22, () => _owner.WorkFocusStart(_focusProject)));
        actions.Children.Add(_focusToggle);
        actions.Children.Add(Button(Loc.T("重置", "Reset"), 11.5, 22, _owner.WorkFocusReset));
        var stack = new StackPanel();
        stack.Children.Add(_focusTime);
        stack.Children.Add(_focusState);
        stack.Children.Add(actions);
        panel.Child = stack;
        return panel;
    }

    private Guid? _focusProject;
    private readonly TextBlock _overviewText = UI.Text("", 12, UI.Secondary);
    private readonly StackPanel _overviewPanel = new();
    private TextBlock _emptyHint = UI.Text("", 11.5, UI.Secondary);

    public void Update(FocusTimer focus, WorkOverview overview, IReadOnlyList<ProjectEntry> projects, IReadOnlyList<SessionActivitySnapshot> activity,
        IReadOnlyList<ClipEntry> clips, IReadOnlyList<ShelfFileGroup> groups)
    {
        if (!_overviewPanel.Children.Contains(_overviewText)) _overviewPanel.Children.Add(_overviewText);
        _overviewText.TextWrapping = TextWrapping.Wrap;
        string sources = overview.ActiveSources.Count == 0
            ? Loc.T("暂无会话写入采样", "no session-write samples yet")
            : string.Join("、", overview.ActiveSources.Select(ProviderCatalog.Name));
        string top = overview.Projects.Count == 0
            ? Loc.T("还没有带项目的专注记录", "no focus session has named a project yet")
            : string.Join("、", overview.Projects.Take(3).Select(row => Loc.T($"{row.Project.DisplayName} {row.FocusMinutes} 分钟", $"{row.Project.DisplayName}: {row.FocusMinutes} min")));
        _overviewText.Text = Loc.T($"本机记录：{overview.FocusCount} 次专注 · 目标 {overview.FocusMinutes} 分钟 · 工作来源：{sources}。{top}。",
            $"Local records: {overview.FocusCount} focus sessions · {overview.FocusMinutes} target minutes · sources: {sources}. {top}.");
        if (!_projectsPanel.Children.Contains(_emptyHint))
        {
            _emptyHint.TextWrapping = TextWrapping.Wrap;
            _projectsPanel.Children.Add(_emptyHint);
        }
        _focusProject = focus.ProjectId;
        _focusTime.Text = focus.Shown ? focus.Format(DateTimeOffset.Now) : FocusTimer.Format(FocusTimer.Presets[1] * 60);
        string label = focus.Label.Length > 0 ? focus.Label : Loc.T("专注", "Focus");
        var project = _owner.WorkProject(focus.ProjectId);
        string detail = focus.Running ? Loc.T("进行中", "running")
            : focus.Shown && !focus.Finished(DateTimeOffset.Now) ? Loc.T("已暂停", "paused")
            : focus.Shown && focus.Finished(DateTimeOffset.Now) ? Loc.T("已完成", "finished")
            : Loc.T("尚未开始", "not started");
        if (project is not null) detail += " · " + project.DisplayName;
        if (focus.WorkSources.Count > 0) detail += " · " + string.Join("、", focus.WorkSources.Select(ProviderCatalog.Name));
        _focusState.Text = label + " · " + detail;
        _focusToggle.Content = UI.Fixed(focus.Running ? Loc.T("暂停", "Pause") : Loc.T("继续", "Resume"), 11.5, 18, UI.Secondary);
        _focusToggle.IsEnabled = focus.Shown && !focus.Finished(DateTimeOffset.Now);

        Fill(_projectsPanel, _projectRows, projects.Count, () => new WorkRow(
            (Loc.T("打开", "Open"), () => { }), (Loc.T("恢复", "Restore"), () => { }), (Loc.T("复制站点命令", "Copy site command"), () => { })));
        for (int i = 0; i < _projectRows.Count; i++)
        {
            var row = _projectRows[i];
            if (i >= projects.Count) { row.Grid.Visibility = Visibility.Collapsed; continue; }
            var entry = projects[i];
            row.Grid.Visibility = Visibility.Visible;
            row.SetPrimary(entry.DisplayName, entry.RootPath);
            var parts = new List<string> { ProviderCatalog.Name(entry.WorkSource) };
            var service = AxDeck.Core.ProjectServices.Find(_owner.WorkServices, entry.ServiceHost);
            if (service is not null) parts.Add(service.Label);
            parts.Add(entry.LastOpenedAt is { } opened ? Shelf.When(opened, DateTimeOffset.Now) : Loc.T("未从面板打开", "not opened from the panel"));
            row.SetMeta(string.Join(" · ", parts));
            row.SetAction(0, Loc.T($"打开项目：{entry.DisplayName}", $"Open project: {entry.DisplayName}"), () => _owner.WorkOpenProject(entry.Id));
            row.SetAction(1, Loc.T($"恢复工作：{entry.DisplayName}", $"Restore work: {entry.DisplayName}"), () => _owner.WorkRestoreProject(entry.Id));
            if (service is not null)
                row.SetAction(2, Loc.T($"复制 {service.Label} 的命令", $"Copy command for {service.Label}"), () =>
                {
                    if (_owner.WorkServiceCommand(entry.Id) is { } command) _owner.WorkPasteServiceCommand(command);
                });
        }
        Fill(_projectRows, projects.Count);

        Fill(_activityPanel, _activityRows, activity.Count, () => new WorkRow());
        for (int i = 0; i < _activityRows.Count; i++)
        {
            var row = _activityRows[i];
            if (i >= activity.Count) { row.Grid.Visibility = Visibility.Collapsed; continue; }
            var item = activity[i];
            string state = item.State switch
            {
                SessionActivityState.Active => Loc.T("正在写本机会话记录", "Writing local session records"),
                SessionActivityState.Quiet => Loc.T("近期无写入", "No recent writes"),
                _ => Loc.T("未找到本地工作记录", "No local work records"),
            };
            row.SetPrimary(ProviderCatalog.Name(item.Source), state, item.IsWorking ? OkColor : item.State == SessionActivityState.Unavailable ? IdleColor : WarnColor);
            string when = item.LastActivity is { } last ? Shelf.When(last, DateTimeOffset.Now) : Loc.T("未知", "unknown");
            row.SetMeta(Loc.T($"最近写入 {when} · 共 {item.RecordCount} 个本地记录，{item.ActiveRecordCount} 个正在写入",
                $"Last write {when} · {Loc.Count(item.RecordCount, "local record", "local records")}, {item.ActiveRecordCount} being written"));
        }

        Fill(_clipsPanel, _clipRows, clips.Count, () => new WorkRow(("粘贴", () => { })));
        for (int i = 0; i < _clipRows.Count; i++)
        {
            var row = _clipRows[i];
            if (i >= clips.Count) { row.Grid.Visibility = Visibility.Collapsed; continue; }
            var clip = clips[i];
            var (first, more) = Shelf.FirstLine(clip.Text);
            row.Grid.Visibility = Visibility.Visible;
            row.SetPrimary(first.Length > 0 ? first : Loc.T("空白内容", "Empty text"), Shelf.When(clip.Copied, DateTimeOffset.Now));
            row.SetMeta(more > 0 ? Loc.T($"+{more} 行", $"+{more} lines") : Loc.T("最近文本", "Recent text"));
            row.SetAction(0, Loc.T("粘贴", "Paste"), () => _owner.WorkPasteClip(clip.Id));
        }

        Fill(_groupsPanel, _groupRows, groups.Count, () => new WorkRow(("粘贴路径", () => { })));
        for (int i = 0; i < _groupRows.Count; i++)
        {
            var row = _groupRows[i];
            if (i >= groups.Count) { row.Grid.Visibility = Visibility.Collapsed; continue; }
            var group = groups[i];
            row.Grid.Visibility = Visibility.Visible;
            row.SetPrimary(group.ShownName, Loc.T($"{group.Paths.Count} 个路径", $"{Loc.Count(group.Paths.Count, "path", "paths")}"));
            row.SetMeta(Loc.T("只保存路径，不复制文件", "Stores paths only; copies no files"));
            row.SetAction(0, Loc.T("粘贴路径", "Paste paths"), () => _owner.WorkPasteGroup(group.Id));
        }

        if (projects.Count == 0 && activity.Count == 0 && clips.Count == 0 && groups.Count == 0)
        {
            _emptyHint.Text = Loc.T(
                "这里只显示你本机已有的工作记录：在设置中登记项目、选择 AI 工作来源，或从中转站复制内容，就会汇总到这里。",
                "This page only shows work records already on this PC: register a project or choose an AI work source in Settings, or copy something from the Shelf, and it appears here.");
            _emptyHint.Visibility = Visibility.Visible;
        }
        else _emptyHint.Visibility = Visibility.Collapsed;
    }

    private static void Fill(StackPanel panel, List<WorkRow> rows, int count, Func<WorkRow> build)
    {
        while (rows.Count < Math.Min(count, 6))
        {
            var row = build();
            rows.Add(row);
            panel.Children.Add(row.Grid);
        }
    }

    private static void Fill(List<WorkRow> rows, int count)
    {
        foreach (var row in rows.Skip(Math.Min(count, 6))) row.Grid.Visibility = Visibility.Collapsed;
    }

    private static TextBlock Section(string text)
    {
        var label = UI.Fixed(text, 11, 16, UI.Tertiary, FontWeights.SemiBold);
        label.Margin = new Thickness(0, 0, 0, 4);
        return label;
    }

    private static Button Button(string text, double size, double height, Action onClick)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            Content = UI.Fixed(text, size, 18, UI.Secondary),
            Margin = new Thickness(4, 4, 4, 0), Padding = new Thickness(8, 0, 8, 0), Height = height,
            MinWidth = 0,
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private sealed class WorkRow
    {
        private readonly TextBlock _primary = UI.Text("", 12.5, UI.Primary);
        private readonly TextBlock _meta = UI.Text("", 11, UI.Tertiary);
        private readonly Button[] _actions;
        private readonly Action?[] _clicks;

        public Grid Grid { get; } = new() { Margin = new Thickness(0, 1, 0, 1) };

        public WorkRow(params (string Label, Action Click)[] actions)
        {
            Grid.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
            _primary.TextTrimming = TextTrimming.CharacterEllipsis;
            _meta.TextTrimming = TextTrimming.CharacterEllipsis;
            _meta.Margin = new Thickness(0, 1, 0, 0);
            text.Children.Add(_primary);
            text.Children.Add(_meta);
            Grid.Children.Add(text);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _actions = new Button[actions.Length];
            _clicks = new Action?[actions.Length];
            for (int i = 0; i < actions.Length; i++)
            {
                _clicks[i] = actions[i].Click;
                int index = i;
                _actions[i] = Button(actions[i].Label, 11, 20, () => _clicks[index]?.Invoke());
                _actions[i].Margin = new Thickness(2, 0, 2, 0);
                _actions[i].Visibility = Visibility.Collapsed;
                bar.Children.Add(_actions[i]);
            }
            Grid.SetColumn(bar, 1);
            Grid.Children.Add(bar);
        }

        public void SetPrimary(string primary, string meta, string? color = null)
        {
            _primary.Text = primary;
            _primary.Foreground = UI.Brush(color ?? UI.Primary);
            _meta.Text = meta;
            Grid.ToolTip = primary + "\n" + meta;
            AutomationProperties.SetName(Grid, Grid.ToolTip as string ?? primary);
        }

        public void SetMeta(string meta) => SetPrimary(_primary.Text, meta, _primary.Foreground is SolidColorBrush brush ? brush.Color.ToString() : null);

        public void SetAction(int index, string label, Action onClick)
        {
            if (index < 0 || index >= _actions.Length) return;
            var button = _actions[index];
            _clicks[index] = onClick;
            button.Visibility = Visibility.Visible;
            button.Content = UI.Fixed(label, 11, 18, UI.Secondary);
            AutomationProperties.SetName(button, label);
        }
    }
}
