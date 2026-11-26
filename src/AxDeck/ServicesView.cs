using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services & tools page in the panel: readings only. The view is built once and filled by Update from the live status
// the controller reads off the UI thread. All actions live in the settings page's ServicesActionsView; this page shows
// the same readings plus one link to get there. No network is touched here.
internal sealed class ServicesView : Grid, IServicesDisplay
{
    internal static readonly string OkColor = "#3FB950", BadColor = "#F85149", WarnColor = "#D29922", IdleColor = "#8B949E";

    private readonly MainWindow _owner;
    private readonly StackPanel _envPanel = new() { Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel _sitesPanel = new();
    private readonly StackPanel _bgePanel = new();
    private readonly StackPanel _axtvPanel = new();
    private readonly StackPanel _diagnosticsPanel = new();
    private readonly TextBlock _status = UI.Text("", 12, UI.Tertiary);

    // The emulator's rows. They are built once and filled in as the state is read.
    private Row _axtvState = null!, _axtvAvd = null!, _axtvWindow = null!, _axtvOrientation = null!, _axtvVersion = null!, _axtvShotDir = null!;

    // The actions page in settings embeds this view with its link hidden, so both places show the same readings from one
    // implementation. The panel itself keeps the link as its only way into the settings page.
    public ServicesView(MainWindow owner, bool showSettingsLink = true)
    {
        _owner = owner;
        _status.Margin = new Thickness(0, 0, 0, 8);
        _status.TextWrapping = TextWrapping.Wrap;
        Margin = new Thickness(2, 2, 2, 2);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel { Margin = new Thickness(0, 4, 6, 4) };
        content.Children.Add(Section(Loc.T("环境", "Environment")));
        content.Children.Add(_envPanel);
        content.Children.Add(Section(Loc.T("站点入口", "Site entries")));
        content.Children.Add(_sitesPanel);
        content.Children.Add(Section(Loc.T("BGE-M3 向量服务", "BGE-M3 embedding service")));
        content.Children.Add(_bgePanel);
        content.Children.Add(Section(Loc.T("AxTV 模拟器", "AxTV emulator")));
        content.Children.Add(_axtvPanel);
        BuildAxTv();
        content.Children.Add(Section(Loc.T("运行开销与能力", "Runtime cost and capabilities")));
        content.Children.Add(_diagnosticsPanel);
        BuildDiagnostics();
        scroll.Content = content;
        Children.Add(scroll);

        // 面板底部是状态行与唯一的入口：一条两端淡出的细线把它同可滚动的读数分开，否则滚动区最后一行贴着状态文字，
        // 看上去像被裁掉了半截。设置页内嵌这份读数时状态已经在操作区上方显示，不再重复一遍。
        if (showSettingsLink)
        {
            var foot = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            foot.Children.Add(new Border { Height = 1, Background = UI.Fade(UI.Hairline, vertical: false, tails: .08), Margin = new Thickness(0, 0, 0, 6) });
            foot.Children.Add(_status);
            var open = ActionButton(Loc.T("在设置中操作", "Manage in settings"), () => _owner.ServicesOpenSettings());
            open.Margin = new Thickness(4, 2, 4, 0);
            foot.Children.Add(open);
            Grid.SetRow(foot, 1);
            Children.Add(foot);
        }
    }

    private Row _diagnosticCost = null!, _diagnosticTraffic = null!, _diagnosticMonitor = null!;
    private readonly List<Row> _diagnosticFacts = [];
    private readonly StackPanel _diagnosticStops = new();
    private string _diagnosticStopsKey = "";

    // Built once and filled by UpdateDiagnostics. The cost is a one-off process probe; the capability facts reuse the monitor snapshot
    // the app already has, so opening this page does not start a second sampler.
    private void BuildDiagnostics()
    {
        _diagnosticCost = new Row(Loc.T("应用开销", "App cost"));
        _diagnosticTraffic = new Row(Loc.T("本机流量", "Local traffic"));
        _diagnosticMonitor = new Row(Loc.T("自动停用读数", "Stopped readings"));
        _diagnosticsPanel.Children.Add(_diagnosticCost.Grid);
        _diagnosticsPanel.Children.Add(_diagnosticTraffic.Grid);
        for (int i = 0; i < 11; i++)
        {
            var row = new Row("");
            row.Grid.Tag = "DiagnosticFact";
            _diagnosticFacts.Add(row);
            _diagnosticsPanel.Children.Add(row.Grid);
        }
        _diagnosticsPanel.Children.Add(_diagnosticMonitor.Grid);
        _diagnosticsPanel.Children.Add(_diagnosticStops);
    }

    public void UpdateDiagnostics(AppDiagnostics diagnostics)
    {
        _diagnosticCost.Set(diagnostics.ResourceDetail, DiagnosticColor(diagnostics.ResourceState));
        _diagnosticCost.Grid.ToolTip = diagnostics.ResourceDetail;
        _diagnosticTraffic.Set(diagnostics.TrafficDetail, DiagnosticColor(diagnostics.TrafficState));
        _diagnosticTraffic.Grid.ToolTip = diagnostics.TrafficDetail;
        for (int i = 0; i < _diagnosticFacts.Count; i++)
        {
            var row = _diagnosticFacts[i];
            if (i >= diagnostics.Facts.Count)
            {
                row.Set("", IdleColor);
                row.Grid.Visibility = Visibility.Collapsed;
                continue;
            }
            var fact = diagnostics.Facts[i];
            row.Grid.Visibility = Visibility.Visible;
            row.SetLabel(AppDiagnostics.SubjectName(fact.Subject));
            row.Set($"{AppDiagnostics.StateName(fact.State)} · {fact.Detail}", DiagnosticColor(fact.State));
            row.Grid.ToolTip = fact.Detail;
        }
        _diagnosticMonitor.Set(
            Loc.T($"{diagnostics.Stops.Count} 项自动停用的读数", $"{Loc.Count(diagnostics.Stops.Count, "reading", "readings")} automatically stopped"),
            diagnostics.Stops.Count > 0 ? WarnColor : IdleColor);
        UpdateStops(diagnostics.Stops);
    }

    // Stops change only when SensorGuard stops or resumes a reading. Rebuilding then keeps the rows current, while ordinary
    // refreshes leave even these rows in place. The resume action itself lives in the settings page.
    private void UpdateStops(IReadOnlyList<SensorStop> stops)
    {
        string key = string.Join("|", stops.Select(stop => $"{stop.Reading}:{stop.Device}:{stop.Method}:{stop.Name}"));
        if (key == _diagnosticStopsKey) return;
        _diagnosticStopsKey = key;
        _diagnosticStops.Children.Clear();
        foreach (var stop in stops)
        {
            var row = new Row(Loc.T("需要恢复", "Needs resume"));
            row.Set(SensorStops.Notice(stop), WarnColor);
            row.Grid.ToolTip = SensorStops.Notice(stop);
            _diagnosticStops.Children.Add(row.Grid);
        }
    }

    internal static string DiagnosticActionLabel(DiagnosticAction action) => action switch
    {
        DiagnosticAction.OpenSensorSettings => Loc.T("传感器设置", "Sensor settings"),
        DiagnosticAction.AllowFrames => Loc.T("授权", "Allow"),
        _ => Loc.T("监控设置", "Monitor settings"),
    };

    internal static string DiagnosticActionName(DiagnosticAction action, DiagnosticFact fact) => Loc.T(
        $"{DiagnosticActionLabel(action)}：{AppDiagnostics.SubjectName(fact.Subject)}，{AppDiagnostics.StateName(fact.State)} · {fact.Detail}",
        $"{DiagnosticActionLabel(action)}: {AppDiagnostics.SubjectName(fact.Subject)}, {AppDiagnostics.StateName(fact.State)} · {fact.Detail}");

    internal static string DiagnosticColor(DiagnosticState state) => state switch
    {
        DiagnosticState.Available => OkColor,
        DiagnosticState.Estimated => WarnColor,
        DiagnosticState.Unavailable => BadColor,
        _ => IdleColor,
    };

    public void SetStatus(string text) => SetStatus(text, false);

    // A status that is a warning rather than a note.
    public void SetStatus(string text, bool warning)
    {
        _status.Text = text;
        _status.Foreground = UI.Brush(warning ? "#D29922" : UI.Tertiary);
    }

    // Filled by the controller from a fresh status snapshot; never blocks (it is called on the UI thread after a background read).
    public void Update(CloudflareTunnel.StatusSnapshot status, IReadOnlyList<CloudflareTunnel.SiteProbe>? probes)
    {
        _envPanel.Children.Clear();
        bool coreOk = CloudflareTunnel.CoreOk(status);
        _envPanel.Children.Add(CoreRow(coreOk));
        _envPanel.Children.Add(EnvRow(Loc.T("cloudflared", "cloudflared"),
            status.CloudflaredPath is { } path ? $"{status.CloudflaredVersion}  ({path})" : Loc.T("未找到 cloudflared.exe", "cloudflared.exe not found"),
            status.CloudflaredPath is null ? BadColor : OkColor));
        _envPanel.Children.Add(EnvRow(Loc.T("本地进程数", "Local processes"), status.LocalProcesses.ToString(), IdleColor));
        _envPanel.Children.Add(EnvRow(Loc.T("公网 443", "Public 443"), status.PublicTcp443 ? Loc.T("可达", "reachable") : Loc.T("不可达", "unreachable"), status.PublicTcp443 ? OkColor : BadColor));
        _envPanel.Children.Add(EnvRow(Loc.T("边缘状态", "Edge status"), status.Edge.Detail, status.Edge.Online ? OkColor : BadColor));
        _envPanel.Children.Add(EnvRow(Loc.T("SSH 配置", "SSH config"), status.SshConfigExists ? status.SshConfig : Loc.T("缺失", "missing"), status.SshConfigExists ? OkColor : BadColor));
        _envPanel.Children.Add(EnvRow(Loc.T("代理脚本", "Proxy bat"), status.ProxyBatExists ? status.ProxyBat : Loc.T("缺失", "missing"), status.ProxyBatExists ? OkColor : BadColor));
        _envPanel.Children.Add(EnvRow(Loc.T("ProxyCommand", "ProxyCommand"), status.ProxyCommandOk ? Loc.T("正常", "ok") : Loc.T("异常", "bad"), status.ProxyCommandOk ? OkColor : BadColor));

        _sitesPanel.Children.Clear();
        var probeByHost = probes?.ToDictionary(p => p.Host);
        var sites = CloudflareTunnel.Sites;
        if (sites.Count == 0)
        {
            // Nothing to list: the entries are the user's own, read from services.json, and this build ships none.
            var hint = UI.Text(Loc.T(
                "还没有配置站点。隧道入口来自本机的 services.json（与 settings.json 同一目录），仓库和安装包里不带任何主机名。点下面「打开配置」可以生成一份模板，改完保存即生效。",
                "No sites configured yet. The tunnel entries come from services.json on this PC (the folder that holds settings.json); the repository and the installer carry no host names. Use Open config to write a template, then edit and save it."), 11.5, UI.Secondary);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(2, 2, 2, 2);
            _sitesPanel.Children.Add(hint);
        }
        foreach (var site in sites)
        {
            CloudflareTunnel.SiteProbe? probe = probeByHost is { } map && map.TryGetValue(site.Host, out var p) ? p : null;
            _sitesPanel.Children.Add(SiteRow(site, probe));
        }
    }

    // The BGE-M3 embedding service: the local llama.cpp server that serves the bge-m3 model, ported from the old
    // "BGE-M3 控制面板". The snapshot arrives from the controller, which polls /health and /slots every two seconds.
    public void UpdateBge(BgeM3Service.Snapshot snapshot)
    {
        _bgePanel.Children.Clear();
        bool configured = snapshot.ModelPath.Length > 0;
        bool missing = !snapshot.ServerFound || !configured || !snapshot.ModelFound;
        string state = snapshot.Running ? Loc.T("运行中", "running")
            : snapshot.Loading ? Loc.T("加载模型中…", "loading the model…")
            : !configured ? Loc.T("模型未配置", "no model configured")
            : !snapshot.ServerFound ? Loc.T("未找到 llama-server.exe", "llama-server.exe not found")
            : !snapshot.ModelFound ? Loc.T("模型缺失", "model missing")
            : Loc.T("已停止", "stopped");
        _bgePanel.Children.Add(EnvRow(Loc.T("服务状态", "Service state"), state, snapshot.Running ? OkColor : snapshot.Loading ? WarnColor : missing ? BadColor : IdleColor));
        bool busy = snapshot.Running && snapshot.BusySlots > 0;
        string processing = !snapshot.Running ? "--" : busy ? Loc.T("处理中", "busy") : Loc.T("空闲", "idle");
        _bgePanel.Children.Add(EnvRow(Loc.T("处理状态", "Processing"), processing, !snapshot.Running ? IdleColor : busy ? WarnColor : OkColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("槽位", "Slots"), snapshot.TotalSlots > 0 ? $"{snapshot.IdleSlots} / {snapshot.TotalSlots}" : "--", IdleColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("累计请求", "Requests"), snapshot.TotalRequests.ToString(), IdleColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("累计 Token", "Tokens"), BgeM3Service.FormatTokens(snapshot.TotalTokens), IdleColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("端口", "Port"),
            $"{BgeM3Service.Port}  |  {Loc.T("模型", "model")} {BgeM3Service.ModelLabel}  |  {Loc.T("维度", "dims")} {BgeM3Service.EmbeddingDimensions}", IdleColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("API", "API"), BgeM3Service.EmbeddingsApi, IdleColor));
        _bgePanel.Children.Add(EnvRow(Loc.T("模型文件", "Model file"),
            snapshot.ModelPath.Length > 0 ? snapshot.ModelPath : Loc.T("未配置（在 services.json 里设置 bge.modelPath）", "not configured (set bge.modelPath in services.json)"),
            snapshot.ModelPath.Length > 0 ? IdleColor : BadColor));
    }

    // The Android emulator (the old "AxTV 模拟器管理器"): the AVD to run, the window's shape and the app on the
    // device. The snapshot arrives from the controller, which reads the device off the UI thread. The block is
    // built once and only its text is filled in: the state is read every few seconds, and building it again
    // each time would take the text box with the path in it apart from its place.
    public void UpdateAxTv(AxTvEmulator.StatusSnapshot status, string avd)
    {
        bool missing = !status.SdkFound;
        string state = missing ? Loc.T("未找到 Android SDK", "Android SDK not found")
            : status.DeviceOnline ? Loc.T("在线", "online")
            : status.ProcessAlive ? Loc.T("启动中", "starting")
            : Loc.T("已停止", "stopped");
        _axtvState.Set(state, missing ? BadColor : status.DeviceOnline ? OkColor : status.ProcessAlive ? WarnColor : IdleColor);
        _axtvAvd.Set($"{status.AvdName}  ·  {status.AvdLabel}  ·  {status.Panel}", IdleColor);
        _axtvWindow.Set(WindowText(status), status.WindowMode == "fullscreen" ? OkColor : IdleColor);
        _axtvOrientation.Set(OrientationText(status), IdleColor);
        _axtvVersion.Set(
            status.AppVersion.Length > 0 ? $"{AxTvEmulator.Package}  {status.AppVersion}" : Loc.T("未安装", "not installed"),
            status.AppVersion.Length > 0 ? OkColor : IdleColor);
        _axtvShotDir.Set(AxTvEmulator.ShotDir, IdleColor);
    }

    // Builds the emulator's reading rows once. The buttons that change any of this live in the settings page.
    private void BuildAxTv()
    {
        _axtvState = new Row(Loc.T("模拟器", "Emulator"));
        _axtvAvd = new Row(Loc.T("AVD", "AVD"));
        _axtvWindow = new Row(Loc.T("窗口", "Window"));
        _axtvOrientation = new Row(Loc.T("方向", "Orientation"));
        _axtvVersion = new Row(Loc.T("应用版本", "App version"));
        _axtvShotDir = new Row(Loc.T("截图目录", "Screenshot folder"));
        _axtvPanel.Children.Add(_axtvState.Grid);
        _axtvPanel.Children.Add(_axtvAvd.Grid);
        _axtvPanel.Children.Add(_axtvWindow.Grid);
        _axtvPanel.Children.Add(_axtvOrientation.Grid);
        _axtvPanel.Children.Add(_axtvVersion.Grid);
        _axtvPanel.Children.Add(_axtvShotDir.Grid);
    }

    // A labelled row whose value can be filled in later, so the panel is built once and only its text changes.
    internal sealed class Row
    {
        private readonly TextBlock _value = UI.Text("", 12.5, UI.Primary);
        private readonly Ellipse _dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _label;
        private readonly Button _action;
        private Action? _click;

        public Grid Grid { get; }

        public Row(string label)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _label = UI.Fixed(label, 12.5, 18, UI.Secondary);
            _label.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(_label);
            var right = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            right.Children.Add(_dot);
            _value.Margin = new Thickness(6, 0, 0, 0);
            _value.TextTrimming = TextTrimming.CharacterEllipsis;
            _value.MinWidth = 0;
            Grid.SetColumn(_value, 1);
            right.Children.Add(_value);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            _action = ActionButton("", () => _click?.Invoke(), small: true);
            _action.Margin = new Thickness(8, 0, 0, 0);
            _action.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_action, 2);
            grid.Children.Add(_action);
            Grid = grid;
        }

        public void SetLabel(string label) => _label.Text = label;

        public void Set(string text, string color)
        {
            _value.Text = text;
            _dot.Fill = UI.Brush(color);
        }

        public void SetAction(string? label, string? name, string? help, Action? click)
        {
            _click = click;
            bool shown = label is { Length: > 0 } && click is not null;
            _action.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            if (!shown) return;
            _action.Content = UI.Fixed(label!, 11.5, 18, UI.Secondary);
            _action.ToolTip = help;
            System.Windows.Automation.AutomationProperties.SetName(_action, name ?? label!);
        }
    }

    internal static string WindowText(AxTvEmulator.StatusSnapshot status) => status.WindowMode switch
    {
        "fullscreen" => Loc.T("全屏", "full screen") + "  " + status.WindowRect,
        "small" => Loc.T("小窗", "small window") + "  " + status.WindowRect,
        _ => Loc.T("未启动", "not running"),
    };

    internal static string OrientationText(AxTvEmulator.StatusSnapshot status) => status.Orientation switch
    {
        "portrait_avd" => Loc.T("竖屏 AVD（物理 1080×1920）", "portrait AVD (physical 1080×1920)"),
        "portrait" => Loc.T("竖屏 1080×1920", "portrait 1080×1920"),
        "custom" => Loc.T("自定义", "custom"),
        "landscape" => Loc.T("横屏 1920×1080", "landscape 1920×1080"),
        _ => "--",
    };

    internal static TextBlock Section(string text) => UI.Fixed(text, 13, 20, UI.Tertiary, FontWeights.SemiBold);
    internal static Grid CoreRow(bool ok) => EnvRow(Loc.T("基础环境", "Core infra"), ok ? Loc.T("就绪", "ready") : Loc.T("缺失", "incomplete"), ok ? OkColor : WarnColor);

    internal static Grid EnvRow(string label, string value, string color)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelBlock = UI.Fixed(label, 12.5, 18, UI.Secondary);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(labelBlock);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(Dot(color));
        var valueBlock = UI.Text(value, 12.5, UI.Primary);
        valueBlock.Margin = new Thickness(6, 0, 0, 0);
        valueBlock.TextTrimming = TextTrimming.CharacterEllipsis;
        right.Children.Add(valueBlock);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    internal static Grid SiteRow(CloudflareTunnel.Site site, CloudflareTunnel.SiteProbe? probe)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string dot = IdleColor;
        if (probe is { } p)
        {
            dot = p.Level switch { "ok" => OkColor, "warn" => WarnColor, _ => BadColor };
        }
        left.Children.Add(Dot(dot));
        var labelBlock = UI.Fixed(site.Label, 12.5, 18, UI.Primary, FontWeights.SemiBold);
        labelBlock.Margin = new Thickness(6, 0, 0, 0);
        labelBlock.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(labelBlock);
        var hostBlock = UI.Fixed(site.Host, 11.5, 18, UI.Tertiary);
        hostBlock.Margin = new Thickness(8, 0, 0, 0);
        hostBlock.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(hostBlock);
        left.Children.Add(KindBadge(site.Kind));
        Grid.SetColumn(left, 0);

        var detail = UI.Text(probe is { } dp ? dp.Detail : Loc.T("点击「探测站点」查看状态", "Click Probe sites for status"), 11.5, UI.Secondary);
        detail.Margin = new Thickness(10, 0, 10, 0);
        detail.VerticalAlignment = VerticalAlignment.Center;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(detail, 1);

        grid.Children.Add(left); grid.Children.Add(detail);
        return grid;
    }

    internal static Ellipse Dot(string color) => new() { Width = 8, Height = 8, Fill = UI.Brush(color), VerticalAlignment = VerticalAlignment.Center };

    internal static Border KindBadge(CloudflareTunnel.SiteKind kind)
    {
        string text = kind switch { CloudflareTunnel.SiteKind.Http => "HTTP", CloudflareTunnel.SiteKind.Tcp => "TCP", CloudflareTunnel.SiteKind.Rdp => "RDP", CloudflareTunnel.SiteKind.Ssh => "SSH", _ => "?" };
        var border = new Border
        {
            Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(5, 1, 5, 1), CornerRadius = new CornerRadius(3),
            Background = UI.Brush(UI.Track), VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = UI.Fixed(text, 10, 14, UI.Secondary);
        border.Child = badge;
        return border;
    }

    internal static Button ActionButton(string label, Action onClick, bool small = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            Content = UI.Fixed(label, small ? 11.5 : 12.5, 18, UI.Secondary),
            Margin = new Thickness(4, 4, 4, 0), Padding = new Thickness(small ? 8 : 10, 0, small ? 8 : 10, 0), Height = small ? 22 : 26,
        };
        button.Click += (_, _) => onClick();
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        return button;
    }
}
