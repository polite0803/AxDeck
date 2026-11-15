using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services & tools page: the personal Cloudflare tunnel at a glance. The view is built once and filled by Update from the
// live status the controller reads off the UI thread; the actions route back to the controller (MainWindow.ServicesPanel.cs),
// which runs the side effects and refreshes the data. No network is touched here.
internal sealed class ServicesView : Grid
{
    private static readonly string OkColor = "#3FB950", BadColor = "#F85149", WarnColor = "#D29922", IdleColor = "#8B949E";

    private readonly MainWindow _owner;
    private readonly StackPanel _envPanel = new() { Margin = new Thickness(0, 0, 0, 14) };
    private readonly StackPanel _sitesPanel = new();
    private readonly StackPanel _bgePanel = new();
    private readonly StackPanel _axtvPanel = new();
    private readonly TextBlock _status = UI.Text("", 12, UI.Tertiary);
    // The path of the package to install, and the name of the AVD to start. Both belong to the controller: the view
    // only shows them and reports the changes.
    private readonly TextBox _apkPath = new();
    private string _avd = "";

    // The emulator's rows. They are built once and filled in as the state is read, so the path in the text box
    // stays where the user put it.
    private Row _axtvState = null!, _axtvAvd = null!, _axtvWindow = null!, _axtvOrientation = null!, _axtvVersion = null!, _axtvShotDir = null!;

    public ServicesView(MainWindow owner)
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
        scroll.Content = content;
        Children.Add(scroll);

        var bar = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(_status);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = double.NaN, Margin = new Thickness(-4, 0, -4, 0) };
        buttons.Children.Add(ActionButton(Loc.T("刷新", "Refresh"), () => _owner.ServicesRefresh(false)));
        buttons.Children.Add(ActionButton(Loc.T("探测站点", "Probe sites"), () => _owner.ServicesRefresh(true)));
        buttons.Children.Add(ActionButton(Loc.T("SSH 登录测试", "SSH login test"), () => _owner.ServicesSshTest(CloudflareTunnel.DefaultSshHost)));
        buttons.Children.Add(ActionButton(Loc.T("容器状态", "Container status"), () => _owner.ServicesContainer("status")));
        buttons.Children.Add(ActionButton(Loc.T("容器日志", "Container logs"), () => _owner.ServicesContainer("logs")));
        buttons.Children.Add(ActionButton(Loc.T("容器重启", "Restart container"), () => _owner.ServicesContainer("restart")));
        buttons.Children.Add(ActionButton(Loc.T("编辑配置", "Edit config"), () => _owner.ServicesEditConfig()));
        buttons.Children.Add(ActionButton(Loc.T("复制诊断", "Copy diagnostics"), () => _owner.ServicesCopyDiagnostics()));
        bar.Children.Add(buttons);
        Grid.SetRow(bar, 1);
        Children.Add(bar);
    }

    public void SetStatus(string text) => _status.Text = text;

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
        foreach (var site in CloudflareTunnel.Sites)
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
        bool missing = !snapshot.ServerFound || !snapshot.ModelFound;
        string state = snapshot.Running ? Loc.T("运行中", "running")
            : snapshot.Loading ? Loc.T("加载模型中…", "loading the model…")
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

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        actions.Children.Add(ActionButton(Loc.T("启动", "Start"), () => _owner.ServicesStartBge(), small: true));
        actions.Children.Add(ActionButton(Loc.T("停止", "Stop"), () => _owner.ServicesStopBge(), small: true));
        actions.Children.Add(ActionButton(Loc.T("复制 API", "Copy API"), () => _owner.ServicesCopyBgeApi(), small: true));
        actions.Children.Add(ActionButton(Loc.T("打开日志", "Open log"), () => _owner.ServicesOpenBgeLog(), small: true));
        _bgePanel.Children.Add(actions);
    }

    // The Android emulator (the old "AxTV 模拟器管理器"): the AVD to run, the window's shape and the app on the
    // device. The snapshot arrives from the controller, which reads the device off the UI thread. The block is
    // built once and only its text is filled in: the state is read every few seconds, and building it again
    // each time would take the text box with the path in it apart from its place.
    public void UpdateAxTv(AxTvEmulator.StatusSnapshot status, string avd)
    {
        _avd = avd;
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

    // Builds the emulator's block: the rows that show its state, the row with the package to install and the
    // buttons that act on it.
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

        // The package to install: a path, a button to pick one and the install itself.
        var apkRow = new Grid { Margin = new Thickness(0, 6, 0, 2) };
        apkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        apkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        apkRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _apkPath.Height = 24;
        _apkPath.Margin = new Thickness(0, 0, 4, 0);
        _apkPath.Padding = new Thickness(6, 2, 6, 2);
        _apkPath.FontSize = 11.5;
        _apkPath.VerticalContentAlignment = VerticalAlignment.Center;
        _apkPath.ToolTip = Loc.T("要安装到模拟器里的 .apk 文件", "The .apk file to install on the emulator");
        apkRow.Children.Add(_apkPath);
        var browse = ActionButton(Loc.T("浏览…", "Browse…"), () => _owner.ServicesBrowseApk(), small: true);
        Grid.SetColumn(browse, 1);
        apkRow.Children.Add(browse);
        var install = ActionButton(Loc.T("安装", "Install"), () => _owner.ServicesInstallApk(), small: true);
        Grid.SetColumn(install, 2);
        apkRow.Children.Add(install);
        _axtvPanel.Children.Add(apkRow);

        var run = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        run.Children.Add(ActionButton(Loc.T("启动", "Start"), () => _owner.ServicesAxTvStart(), small: true));
        run.Children.Add(ActionButton(Loc.T("关闭", "Stop"), () => _owner.ServicesAxTvStop(), small: true));
        run.Children.Add(ActionButton(Loc.T("强制结束", "Force end"), () => _owner.ServicesAxTvForceKill(), small: true));
        run.Children.Add(ActionButton(Loc.T("重启", "Restart"), () => _owner.ServicesAxTvRestart(), small: true));
        run.Children.Add(ActionButton(Loc.T("切换 AVD", "Switch AVD"), () => _owner.ServicesAxTvSwitchAvd(), small: true));
        _axtvPanel.Children.Add(run);

        var window = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        window.Children.Add(ActionButton(Loc.T("全屏", "Full screen"), () => _owner.ServicesAxTvFullscreen(), small: true));
        window.Children.Add(ActionButton(Loc.T("小窗", "Small window"), () => _owner.ServicesAxTvSmall(), small: true));
        window.Children.Add(ActionButton(Loc.T("窗口归位", "Reset window"), () => _owner.ServicesAxTvResetWindow(), small: true));
        window.Children.Add(ActionButton(Loc.T("切换方向", "Switch orientation"), () => _owner.ServicesAxTvToggleOrientation(), small: true));
        _axtvPanel.Children.Add(window);

        var app = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        app.Children.Add(ActionButton(Loc.T("启动应用", "Launch app"), () => _owner.ServicesAxTvLaunchApp(), small: true));
        app.Children.Add(ActionButton(Loc.T("停止应用", "Stop app"), () => _owner.ServicesAxTvStopApp(), small: true));
        app.Children.Add(ActionButton(Loc.T("卸载", "Uninstall"), () => _owner.ServicesAxTvUninstallApp(), small: true));
        app.Children.Add(ActionButton(Loc.T("截图", "Screenshot"), () => _owner.ServicesAxTvScreenshot(), small: true));
        app.Children.Add(ActionButton(Loc.T("打开日志", "Open log"), () => _owner.ServicesAxTvOpenLog(), small: true));
        _axtvPanel.Children.Add(app);
    }

    /// <summary>The path of the package to install, as the user typed or picked it.</summary>
    public string ApkPath => _apkPath.Text;

    /// <summary>Shows the package that is going to be installed.</summary>
    public void SetApkPath(string path) => _apkPath.Text = path;

    // A labelled row whose value can be filled in later, so the panel is built once and only its text changes.
    private sealed class Row
    {
        private readonly TextBlock _value = UI.Text("", 12.5, UI.Primary);
        private readonly Ellipse _dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };

        public Grid Grid { get; }

        public Row(string label)
        {
            var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var labelBlock = UI.Fixed(label, 12.5, 18, UI.Secondary);
            labelBlock.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(labelBlock);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            right.Children.Add(_dot);
            _value.Margin = new Thickness(6, 0, 0, 0);
            _value.TextTrimming = TextTrimming.CharacterEllipsis;
            right.Children.Add(_value);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            Grid = grid;
        }

        public void Set(string text, string color)
        {
            _value.Text = text;
            _dot.Fill = UI.Brush(color);
        }
    }

    private static string WindowText(AxTvEmulator.StatusSnapshot status) => status.WindowMode switch
    {
        "fullscreen" => Loc.T("全屏", "full screen") + "  " + status.WindowRect,
        "small" => Loc.T("小窗", "small window") + "  " + status.WindowRect,
        _ => Loc.T("未启动", "not running"),
    };

    private static string OrientationText(AxTvEmulator.StatusSnapshot status) => status.Orientation switch
    {
        "portrait_avd" => Loc.T("竖屏 AVD（物理 1080×1920）", "portrait AVD (physical 1080×1920)"),
        "portrait" => Loc.T("竖屏 1080×1920", "portrait 1080×1920"),
        "custom" => Loc.T("自定义", "custom"),
        "landscape" => Loc.T("横屏 1920×1080", "landscape 1920×1080"),
        _ => "--",
    };

    private static TextBlock Section(string text) => UI.Fixed(text, 13, 20, UI.Tertiary, FontWeights.SemiBold);
    private static Grid CoreRow(bool ok) => EnvRow(Loc.T("基础环境", "Core infra"), ok ? Loc.T("就绪", "ready") : Loc.T("缺失", "incomplete"), ok ? OkColor : WarnColor);

    private static Grid EnvRow(string label, string value, string color)
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

    private Grid SiteRow(CloudflareTunnel.Site site, CloudflareTunnel.SiteProbe? probe)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

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

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (site.Kind == CloudflareTunnel.SiteKind.Http)
            actions.Children.Add(ActionButton(Loc.T("打开", "Open"), () => _owner.ServicesOpenSite(site), small: true));
        else if (site.Kind == CloudflareTunnel.SiteKind.Ssh)
        {
            actions.Children.Add(ActionButton(Loc.T("测试", "Test"), () => _owner.ServicesSshTest(site.Host), small: true));
            actions.Children.Add(ActionButton(Loc.T("复制", "Copy"), () => _owner.ServicesCopyCommand(site), small: true));
        }
        else
        {
            bool running = CloudflareTunnel.ForwarderRunning(site.Host);
            var toggle = ActionButton(running ? Loc.T("停止", "Stop") : Loc.T("启动", "Start"), () => _owner.ServicesToggleForwarder(site), small: true);
            actions.Children.Add(toggle);
            actions.Children.Add(ActionButton(Loc.T("复制", "Copy"), () => _owner.ServicesCopyCommand(site), small: true));
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(left); grid.Children.Add(detail); grid.Children.Add(actions);
        return grid;
    }

    private static Ellipse Dot(string color) => new() { Width = 8, Height = 8, Fill = UI.Brush(color), VerticalAlignment = VerticalAlignment.Center };

    private static Border KindBadge(CloudflareTunnel.SiteKind kind)
    {
        string text = kind switch { CloudflareTunnel.SiteKind.Http => "HTTP", CloudflareTunnel.SiteKind.Tcp => "TCP", CloudflareTunnel.SiteKind.Ssh => "SSH", _ => "?" };
        var border = new Border
        {
            Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(5, 1, 5, 1), CornerRadius = new CornerRadius(3),
            Background = UI.Brush(UI.Track), VerticalAlignment = VerticalAlignment.Center,
        };
        var badge = UI.Fixed(text, 10, 14, UI.Secondary);
        border.Child = badge;
        return border;
    }

    private static Button ActionButton(string label, Action onClick, bool small = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            Content = UI.Fixed(label, small ? 11.5 : 12.5, 18, UI.Secondary),
            Margin = new Thickness(4, 4, 4, 0), Padding = new Thickness(small ? 8 : 10, 0, small ? 8 : 10, 0), Height = small ? 22 : 26,
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}
