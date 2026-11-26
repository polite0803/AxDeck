using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The settings page's services & tools actions. Everything that changes the machine lives here; the readings are the same
// ServicesView the panel uses, with its link hidden, so a snapshot is drawn once and shown in both places. Buttons only
// route to the existing MainWindow controller methods, which do the work off the UI thread and report back through the
// controller's status broadcast.
internal sealed class ServicesActionsView : Grid, IServicesDisplay
{
    private readonly MainWindow _owner;
    private readonly ServicesView _readings;
    private readonly TextBlock _status = UI.Text("", 12, UI.Tertiary);
    private readonly StackPanel _sites = new();
    private readonly TextBox _apkPath = new();
    private Button _bgeAutostart = null!;
    private bool _bgeAutostartState;
    private readonly List<ServicesView.Row> _facts = [];
    private readonly StackPanel _stops = new();
    private string _stopsKey = "";

    public ServicesActionsView(MainWindow owner)
    {
        _owner = owner;
        Margin = new Thickness(2, 4, 2, 4);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var actions = new StackPanel();
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 0, 0, 8);
        BuildTunnel(actions);
        BuildBge(actions);
        BuildAxTv(actions);
        BuildDiagnostics(actions);
        actions.Children.Add(_status);
        Children.Add(actions);

        _readings = new ServicesView(owner, showSettingsLink: false) { MaxHeight = 320 };
        Grid.SetRow(_readings, 1);
        Children.Add(_readings);

        _bgeAutostartState = owner.ServicesBgeAutostartEnabled();
        UpdateAutostartLabel();
        _apkPath.Text = owner.ServicesApkPath;
        _apkPath.TextChanged += (_, _) => owner.ServicesApkPath = _apkPath.Text;
    }

    private static Button Make(string label, string name, Action action, bool small = true)
    {
        var button = ServicesView.ActionButton(label, action, small);
        AutomationProperties.SetName(button, name);
        return button;
    }

    private static Button Add(StackPanel panel, string label, string name, Action action, bool small = true)
    {
        var button = Make(label, name, action, small);
        panel.Children.Add(button);
        return button;
    }

    private static StackPanel Buttons(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var button in buttons) row.Children.Add(button);
        return row;
    }

    private void BuildTunnel(StackPanel root)
    {
        root.Children.Add(ServicesView.Section(Loc.T("Cloudflare 隧道", "Cloudflare tunnel")));
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        Add(top, Loc.T("刷新", "Refresh"), Loc.T("刷新环境", "Refresh environment"), () => _owner.ServicesRefresh(false));
        Add(top, Loc.T("探测站点", "Probe sites"), Loc.T("探测全部站点", "Probe all sites"), () => _owner.ServicesRefresh(true));
        Add(top, Loc.T("SSH 登录测试", "SSH login test"), Loc.T("SSH 登录测试", "SSH login test"), () => _owner.ServicesSshTest(CloudflareTunnel.DefaultSshHost));
        Add(top, Loc.T("容器状态", "Container status"), Loc.T("容器状态", "Container status"), () => _owner.ServicesContainer("status"));
        Add(top, Loc.T("容器日志", "Container logs"), Loc.T("容器日志", "Container logs"), () => _owner.ServicesContainer("logs"));
        Add(top, Loc.T("容器重启", "Restart container"), Loc.T("重启容器", "Restart container"), () => _owner.ServicesContainer("restart"));
        root.Children.Add(top);
        var config = new StackPanel { Orientation = Orientation.Horizontal };
        Add(config, Loc.T("打开配置", "Open config"), Loc.T("打开 services.json", "Open services.json"), () => _owner.ServicesOpenConfig());
        Add(config, Loc.T("编辑 SSH 配置", "Edit SSH config"), Loc.T("编辑 SSH 配置", "Edit SSH config"), () => _owner.ServicesEditConfig());
        Add(config, Loc.T("复制诊断", "Copy diagnostics"), Loc.T("复制诊断", "Copy diagnostics"), () => _owner.ServicesCopyDiagnostics());
        root.Children.Add(config);
        root.Children.Add(ServicesView.Section(Loc.T("站点入口", "Site entries")));
        root.Children.Add(_sites);
    }

    private void BuildBge(StackPanel root)
    {
        root.Children.Add(ServicesView.Section(Loc.T("BGE-M3 向量服务", "BGE-M3 embedding service")));
        root.Children.Add(Buttons(
            Make(Loc.T("启动", "Start"), Loc.T("启动 BGE-M3", "Start BGE-M3"), () => _owner.ServicesStartBge()),
            Make(Loc.T("停止", "Stop"), Loc.T("停止 BGE-M3", "Stop BGE-M3"), () => _owner.ServicesStopBge()),
            Make(Loc.T("复制 API", "Copy API"), Loc.T("复制 BGE-M3 API 地址", "Copy the BGE-M3 API address"), () => _owner.ServicesCopyBgeApi()),
            Make(Loc.T("查看日志", "View log"), Loc.T("查看 BGE-M3 日志", "View the BGE-M3 log"), () => _owner.ServicesShowBgeLog()),
            Make(Loc.T("打开日志", "Open log"), Loc.T("打开 BGE-M3 日志文件", "Open the BGE-M3 log file"), () => _owner.ServicesOpenBgeLog()),
            _bgeAutostart = Make("", "", () => _owner.ServicesSetBgeAutostart(!_bgeAutostartState))));
    }

    private void UpdateAutostartLabel()
    {
        if (_bgeAutostart is null) return;
        string label = _bgeAutostartState ? Loc.T("开机自启：已开启", "Start with Windows: on") : Loc.T("开机自启：已关闭", "Start with Windows: off");
        _bgeAutostart.Content = UI.Fixed(label, 11.5, 18, UI.Secondary);
        AutomationProperties.SetName(_bgeAutostart, label);
    }

    private void BuildAxTv(StackPanel root)
    {
        root.Children.Add(ServicesView.Section(Loc.T("AxTV 模拟器", "AxTV emulator")));
        var apk = new Grid { Margin = new Thickness(0, 4, 0, 2) };
        apk.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        apk.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        apk.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _apkPath.Height = 24;
        _apkPath.Padding = new Thickness(6, 2, 6, 2);
        _apkPath.FontSize = 11.5;
        _apkPath.VerticalContentAlignment = VerticalAlignment.Center;
        _apkPath.ToolTip = Loc.T("要安装到模拟器里的 .apk 文件", "The .apk file to install on the emulator");
        AutomationProperties.SetName(_apkPath, Loc.T("APK 路径", "APK path"));
        apk.Children.Add(_apkPath);
        var browse = Make(Loc.T("浏览…", "Browse…"), Loc.T("选择 APK 文件", "Choose an APK file"), () => _owner.ServicesBrowseApk());
        var install = Make(Loc.T("安装", "Install"), Loc.T("安装 APK", "Install the APK"), () => _owner.ServicesInstallApk());
        Grid.SetColumn(browse, 1); apk.Children.Add(browse);
        Grid.SetColumn(install, 2); apk.Children.Add(install);
        root.Children.Add(apk);
        root.Children.Add(Buttons(
            Make(Loc.T("启动", "Start"), Loc.T("启动模拟器", "Start the emulator"), () => _owner.ServicesAxTvStart()),
            Make(Loc.T("关闭", "Stop"), Loc.T("关闭模拟器", "Stop the emulator"), () => _owner.ServicesAxTvStop()),
            Make(Loc.T("强制结束", "Force end"), Loc.T("强制结束模拟器进程", "Force end the emulator processes"), () => _owner.ServicesAxTvForceKill()),
            Make(Loc.T("重启", "Restart"), Loc.T("重启模拟器", "Restart the emulator"), () => _owner.ServicesAxTvRestart()),
            Make(Loc.T("切换 AVD", "Switch AVD"), Loc.T("切换 AVD", "Switch AVD"), () => _owner.ServicesAxTvSwitchAvd())));
        root.Children.Add(Buttons(
            Make(Loc.T("全屏", "Full screen"), Loc.T("模拟器全屏", "Make the emulator full screen"), () => _owner.ServicesAxTvFullscreen()),
            Make(Loc.T("小窗", "Small window"), Loc.T("模拟器小窗", "Make the emulator a small window"), () => _owner.ServicesAxTvSmall()),
            Make(Loc.T("窗口归位", "Reset window"), Loc.T("模拟器窗口归位", "Reset the emulator window"), () => _owner.ServicesAxTvResetWindow()),
            Make(Loc.T("切换方向", "Switch orientation"), Loc.T("切换显示方向", "Switch the display orientation"), () => _owner.ServicesAxTvToggleOrientation())));
        root.Children.Add(Buttons(
            Make(Loc.T("启动应用", "Launch app"), Loc.T("启动 AxTV 应用", "Launch the AxTV app"), () => _owner.ServicesAxTvLaunchApp()),
            Make(Loc.T("停止应用", "Stop app"), Loc.T("停止 AxTV 应用", "Stop the AxTV app"), () => _owner.ServicesAxTvStopApp()),
            Make(Loc.T("卸载", "Uninstall"), Loc.T("卸载 AxTV 应用", "Uninstall the AxTV app"), () => _owner.ServicesAxTvUninstallApp()),
            Make(Loc.T("截图", "Screenshot"), Loc.T("模拟器截图", "Take a screenshot"), () => _owner.ServicesAxTvScreenshot()),
            Make(Loc.T("查看日志", "View log"), Loc.T("查看 AxTV 日志", "View the AxTV log"), () => _owner.ServicesShowAxTvLog()),
            Make(Loc.T("打开日志", "Open log"), Loc.T("打开 AxTV 日志文件", "Open the AxTV log file"), () => _owner.ServicesAxTvOpenLog())));
    }

    private void BuildDiagnostics(StackPanel root)
    {
        root.Children.Add(ServicesView.Section(Loc.T("运行开销与能力", "Runtime cost and capabilities")));
        for (int i = 0; i < 11; i++)
        {
            var row = new ServicesView.Row("");
            row.Grid.Tag = "DiagnosticFact";
            _facts.Add(row);
            root.Children.Add(row.Grid);
        }
        root.Children.Add(_stops);
    }

    public void SetStatus(string text) => SetStatus(text, false);

    public void SetStatus(string text, bool warning)
    {
        _status.Text = text;
        _status.Foreground = UI.Brush(warning ? ServicesView.WarnColor : UI.Tertiary);
        _readings.SetStatus(text, warning);
    }

    public void Update(CloudflareTunnel.StatusSnapshot status, IReadOnlyList<CloudflareTunnel.SiteProbe>? probes)
    {
        _readings.Update(status, probes);
        _sites.Children.Clear();
        var probeByHost = probes?.ToDictionary(p => p.Host);
        foreach (var site in CloudflareTunnel.Sites)
        {
            CloudflareTunnel.SiteProbe? probe = probeByHost is { } map && map.TryGetValue(site.Host, out var p) ? p : null;
            _sites.Children.Add(SiteActions(site, probe));
        }
        if (CloudflareTunnel.Sites.Count == 0)
        {
            var hint = UI.Text(Loc.T("还没有配置站点。上面「打开配置」会生成一份 services.json 模板。",
                "No sites configured yet. Open config above writes a services.json template."), 11.5, UI.Secondary);
            hint.TextWrapping = TextWrapping.Wrap;
            _sites.Children.Add(hint);
        }
    }

    private Grid SiteActions(CloudflareTunnel.Site site, CloudflareTunnel.SiteProbe? probe)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        string dot = probe is not { } p ? ServicesView.IdleColor : p.Level switch { "ok" => ServicesView.OkColor, "warn" => ServicesView.WarnColor, _ => ServicesView.BadColor };
        left.Children.Add(ServicesView.Dot(dot));
        var label = UI.Fixed(site.Label, 12.5, 18, UI.Primary, FontWeights.SemiBold);
        label.Margin = new Thickness(6, 0, 0, 0);
        left.Children.Add(label);
        var host = UI.Fixed(site.Host, 11.5, 18, UI.Tertiary);
        host.Margin = new Thickness(8, 0, 0, 0);
        left.Children.Add(host);
        left.Children.Add(ServicesView.KindBadge(site.Kind));
        Grid.SetColumn(left, 0);
        var detail = UI.Text(probe?.Detail ?? "", 11.5, UI.Secondary);
        detail.Margin = new Thickness(10, 0, 10, 0);
        detail.VerticalAlignment = VerticalAlignment.Center;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(detail, 1);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (site.Kind == CloudflareTunnel.SiteKind.Http)
            Add(actions, Loc.T("打开", "Open"), Loc.T("打开站点 ", "Open site ") + site.Label, () => _owner.ServicesOpenSite(site));
        else if (site.Kind == CloudflareTunnel.SiteKind.Ssh)
        {
            Add(actions, Loc.T("测试", "Test"), Loc.T("测试 SSH ", "Test SSH ") + site.Label, () => _owner.ServicesSshTest(site.Host));
            Add(actions, Loc.T("复制", "Copy"), Loc.T("复制连接命令 ", "Copy the connect command for ") + site.Label, () => _owner.ServicesCopyCommand(site));
        }
        else
        {
            bool running = CloudflareTunnel.ForwarderRunning(site.Host);
            Add(actions, running ? Loc.T("停止", "Stop") : Loc.T("启动", "Start"),
                (running ? Loc.T("停止转发 ", "Stop the forwarder for ") : Loc.T("启动转发 ", "Start the forwarder for ")) + site.Label,
                () => _owner.ServicesToggleForwarder(site));
            Add(actions, Loc.T("复制", "Copy"), Loc.T("复制连接命令 ", "Copy the connect command for ") + site.Label, () => _owner.ServicesCopyCommand(site));
        }
        Grid.SetColumn(actions, 2);
        grid.Children.Add(left); grid.Children.Add(detail); grid.Children.Add(actions);
        return grid;
    }

    public void UpdateDiagnostics(AppDiagnostics diagnostics)
    {
        _readings.UpdateDiagnostics(diagnostics);
        for (int i = 0; i < _facts.Count; i++)
        {
            var row = _facts[i];
            if (i >= diagnostics.Facts.Count)
            {
                row.Set("", ServicesView.IdleColor);
                row.Grid.Visibility = Visibility.Collapsed;
                continue;
            }
            var fact = diagnostics.Facts[i];
            row.Grid.Visibility = Visibility.Visible;
            row.SetLabel(AppDiagnostics.SubjectName(fact.Subject));
            row.Set($"{AppDiagnostics.StateName(fact.State)} · {fact.Detail}", ServicesView.DiagnosticColor(fact.State));
            row.Grid.ToolTip = fact.Detail;
            var action = _owner.ServicesDiagnosticAction(fact);
            row.SetAction(action is { } value ? ServicesView.DiagnosticActionLabel(value) : null,
                action is { } named ? ServicesView.DiagnosticActionName(named, fact) : null,
                action is not null ? Loc.T("打开对应的设置，检查这项读数", "Open the matching settings and check this reading") : null,
                action is { } run ? () => _owner.ServicesDiagnostic(run) : null);
        }
        string key = string.Join("|", diagnostics.Stops.Select(stop => $"{stop.Reading}:{stop.Device}:{stop.Method}:{stop.Name}"));
        if (key != _stopsKey)
        {
            _stopsKey = key;
            _stops.Children.Clear();
            foreach (var stop in diagnostics.Stops)
            {
                var row = new ServicesView.Row(Loc.T("需要恢复", "Needs resume"));
                row.Set(SensorStops.Notice(stop), ServicesView.WarnColor);
                row.Grid.ToolTip = SensorStops.Notice(stop);
                row.SetAction(Loc.T("重新开启", "Turn on again"),
                    Loc.T("重新开启：", "Turn on again: ") + SensorStops.Notice(stop),
                    Loc.T("重新开启后如果电脑再次卡住，AxDeck 会再次自动停止读取。", "Should the PC freeze again, AxDeck stops the reading again by itself."),
                    () => _owner.ServicesResumeReading(stop));
                _stops.Children.Add(row.Grid);
            }
        }
    }

    public void UpdateBge(BgeM3Service.Snapshot snapshot) => _readings.UpdateBge(snapshot);

    public void UpdateAxTv(AxTvEmulator.StatusSnapshot status, string avd) => _readings.UpdateAxTv(status, avd);

    /// <summary>Keeps the startup switch in step with the registry, including a refused change.</summary>
    public void SetBgeAutostart(bool enabled)
    {
        _bgeAutostartState = enabled;
        UpdateAutostartLabel();
    }

    /// <summary>The path of the package to install, shared with the controller so both views read the same value.</summary>
    public string ApkPath => _apkPath.Text;

    /// <summary>Shows a path picked in the file dialog without losing the text box's place.</summary>
    public void SetApkPath(string path)
    {
        if (!string.Equals(_apkPath.Text, path, StringComparison.Ordinal)) _apkPath.Text = path;
    }
}
