using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The services & tools page controller. The page is built once and refilled from a status snapshot read off the UI thread
// (CloudflareTunnel), so opening it or resizing it never blocks; the side effects (starting a forwarder, restarting the remote
// container, opening the browser) run and then refresh the data. No Python and no external .exe invocation: the logic lives in
// Native.CloudflareTunnel, which calls the same native tools (cloudflared.exe, ssh.exe, tasklist) the old app did.
public partial class MainWindow
{
    private ServicesView? _servicesView;
    private string _servicesKey = "";
    private bool _servicesFade;

    // The local BGE-M3 embedding server (the old "BGE-M3 控制面板"), polled while this page is open.
    private readonly BgeM3Service _bge = new();
    private DispatcherTimer? _bgeTimer;
    private bool _bgePolling;

    private void RenderServices()
    {
        bool shown = ServicesContent.Visibility == Visibility.Visible && _servicesView is not null && ServicesContent.Children.Contains(_servicesView);
        RefreshStatus();
        UsageContent.Visibility = Visibility.Collapsed; MusicContent.Visibility = Visibility.Collapsed; ShelfContent.Visibility = Visibility.Collapsed; DevicesContent.Visibility = Visibility.Collapsed; MonitorContent.Visibility = Visibility.Collapsed; ServicesContent.Visibility = Visibility.Visible;
        ModelToolbar.Visibility = Visibility.Collapsed; ApplicationPages.Visibility = Visibility.Collapsed; ClockLabel.Visibility = Visibility.Visible;
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        // The header keeps the usage page's controls, as the music and monitor pages do, so the switch does not move between pages.
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        ServicesContent.Margin = new Thickness(_header.Side, 0, _header.Side, 10);
        // The embedding server is watched while this page is open, the way the old panel watched it every two seconds.
        EnsureBgeTimer();
        // Built once: rebuilding on every resize would re-read the network. The data is refreshed by the buttons and on first open.
        var key = "services";
        bool fade = _servicesFade; _servicesFade = false;
        if (_servicesView is not null && key == _servicesKey && ServicesContent.Children.Contains(_servicesView)) return;
        _servicesKey = key;
        var old = shown ? _servicesView : null;
        _servicesView = new ServicesView(this);
        if (old is not null) MonitorCrossfade.Swap(ServicesContent, _servicesView, old, Soft.Enabled);
        else
        {
            ServicesContent.Children.Clear(); ServicesContent.Children.Add(_servicesView);
            if (fade && Soft.Enabled) _servicesView.BeginAnimation(OpacityProperty, Soft.Fade(.15, 1, Soft.Short));
        }
        ServicesRefresh(false);
    }

    private void OnUi(Action action)
    {
        if (!this.Dispatcher.HasShutdownStarted) this.Dispatcher.Invoke(action);
    }

    // probe = true also runs ProbeSites (per-site status); false refreshes only the environment and the site list.
    internal void ServicesRefresh(bool probe)
    {
        _servicesView?.SetStatus(Loc.T("读取中…", "Reading…"));
        Task.Run(() =>
        {
            var status = CloudflareTunnel.CollectStatus();
            List<CloudflareTunnel.SiteProbe>? probes = null;
            if (probe)
            {
                try { probes = CloudflareTunnel.ProbeSites(); }
                catch (System.Exception ex) { OnUi(() => _servicesView?.SetStatus(Loc.T("探测出错：", "Probe error: ") + ex.Message)); }
            }
            var snap = status; var list = probes;
            OnUi(() =>
            {
                _servicesView?.Update(snap, list);
                _servicesView?.SetStatus(probe ? Loc.T("已探测全部站点", "All sites probed") : Loc.T("已刷新环境", "Environment refreshed"));
            });
        });
    }

    internal void ServicesSshTest(string host)
    {
        _servicesView?.SetStatus(Loc.T("SSH 测试中…", "SSH test…"));
        Task.Run(() =>
        {
            var (ok, detail, rc) = CloudflareTunnel.SshLoginTest(host);
            var note = CloudflareTunnel.ExplainFailure(host, detail);
            OnUi(() => _servicesView?.SetStatus(
                (ok ? Loc.T("SSH 登录成功", "SSH login ok") : Loc.T("SSH 登录失败", "SSH login failed")) + $" (rc={rc})\n{detail}"
                + (note.Length > 0 ? "\n" + note : "")));
        });
    }

    internal void ServicesContainer(string action)
    {
        _servicesView?.SetStatus(Loc.T("容器操作中…", "Container op…"));
        Task.Run(() =>
        {
            var result = action switch
            {
                "logs" => CloudflareTunnel.RemoteContainerLogs(CloudflareTunnel.DefaultSshHost),
                "restart" => CloudflareTunnel.RemoteContainerRestart(CloudflareTunnel.DefaultSshHost),
                _ => CloudflareTunnel.RemoteContainerStatus(CloudflareTunnel.DefaultSshHost),
            };
            var text = result.ReturnCode == 0 ? result.Stdout : $"rc={result.ReturnCode}\n{result.Stderr}\n{result.Stdout}";
            OnUi(() => _servicesView?.SetStatus($"{action}: rc={result.ReturnCode}\n{text}"));
        });
    }

    internal void ServicesEditConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", CloudflareTunnel.SshConfig) { UseShellExecute = true });
            _servicesView?.SetStatus(Loc.T("已用记事本打开 SSH 配置", "Opened SSH config in Notepad"));
        }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("打开失败：", "Open failed: ") + ex.Message); }
    }

    internal void ServicesOpenSite(CloudflareTunnel.Site site)
    {
        try
        {
            var url = CloudflareTunnel.ClientCommand(site);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            _servicesView?.SetStatus(Loc.T("已在浏览器打开 ", "Opened in browser: ") + url);
        }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("打开失败：", "Open failed: ") + ex.Message); }
    }

    internal void ServicesToggleForwarder(CloudflareTunnel.Site site)
    {
        if (CloudflareTunnel.ForwarderRunning(site.Host))
        {
            var (ok, detail) = CloudflareTunnel.StopForwarder(site.Host);
            _servicesView?.SetStatus((ok ? Loc.T("已停止转发 ", "Forwarder stopped ") : Loc.T("停止失败 ", "Stop failed ")) + site.Host + " — " + detail);
        }
        else
        {
            var (ok, detail) = CloudflareTunnel.StartForwarder(site);
            _servicesView?.SetStatus((ok ? Loc.T("已启动转发 ", "Forwarder started ") : Loc.T("启动失败 ", "Start failed ")) + site.Host + " — " + detail);
        }
        ServicesRefresh(true);
    }

    internal void ServicesCopyCommand(CloudflareTunnel.Site site)
    {
        var cmd = CloudflareTunnel.ClientCommand(site);
        try { Clipboard.SetText(cmd); _servicesView?.SetStatus(Loc.T("已复制连接命令", "Copied connect command")); }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("复制失败：", "Copy failed: ") + ex.Message); }
    }

    internal void ServicesCopyDiagnostics()
    {
        _servicesView?.SetStatus(Loc.T("读取诊断…", "Reading diagnostics…"));
        Task.Run(() =>
        {
            var s = CloudflareTunnel.CollectStatus();
            var sb = new StringBuilder();
            sb.AppendLine($"cloudflared: {s.CloudflaredPath ?? "未找到"} ({s.CloudflaredVersion})");
            sb.AppendLine($"local processes: {s.LocalProcesses}");
            sb.AppendLine($"public 443: {s.PublicTcp443}");
            sb.AppendLine($"edge: {s.Edge.Detail}");
            sb.AppendLine($"ssh config exists: {s.SshConfigExists} ({s.SshConfig})");
            sb.AppendLine($"proxy bat exists: {s.ProxyBatExists}");
            sb.AppendLine($"proxy command ok: {s.ProxyCommandOk}");
            sb.AppendLine($"ssh hosts: {string.Join(", ", s.SshHosts)}");
            sb.AppendLine($"ports: {string.Join(", ", s.Ports.Select(p => $"{p.Key}={(p.Value ? "open" : "closed")}"))}");
            var text = sb.ToString();
            OnUi(() =>
            {
                try { Clipboard.SetText(text); _servicesView?.SetStatus(Loc.T("诊断已复制到剪贴板", "Diagnostics copied")); }
                catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("复制失败：", "Copy failed: ") + ex.Message); }
            });
        });
    }

    // ----------------------------------------------------------------------
    // BGE-M3 embedding server (the old "BGE-M3 控制面板", ported to native C#)
    // ----------------------------------------------------------------------

    // The old panel watched the server from a background thread every two seconds. Here the page drives the same
    // poll with a timer that stops as soon as another page takes over.
    private void EnsureBgeTimer()
    {
        if (_bgeTimer is null)
        {
            _bgeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _bgeTimer.Tick += (_, _) => ServicesPollBge();
        }
        if (!_bgeTimer.IsEnabled) _bgeTimer.Start();
    }

    internal void ServicesPollBge()
    {
        if (CurrentPage != DeckPage.Services || ServicesContent.Visibility != Visibility.Visible) { _bgeTimer?.Stop(); return; }
        if (_bgePolling) return;
        _bgePolling = true;
        Task.Run(() =>
        {
            var snapshot = _bge.Poll();
            _bgePolling = false;
            OnUi(() => _servicesView?.UpdateBge(snapshot));
        });
    }

    internal void ServicesStartBge()
    {
        _servicesView?.SetStatus(Loc.T("正在启动 llama-server…", "Starting llama-server…"));
        Task.Run(() =>
        {
            var (ok, detail) = _bge.Start();
            var snapshot = _bge.Poll();
            OnUi(() =>
            {
                _servicesView?.UpdateBge(snapshot);
                _servicesView?.SetStatus(ok ? Loc.T("BGE-M3 已启动", "BGE-M3 started") : Loc.T("启动失败：", "Start failed: ") + detail);
            });
        });
    }

    internal void ServicesStopBge()
    {
        _servicesView?.SetStatus(Loc.T("正在停止…", "Stopping…"));
        Task.Run(() =>
        {
            var (ok, detail) = _bge.Stop();
            var snapshot = _bge.Poll();
            OnUi(() =>
            {
                _servicesView?.UpdateBge(snapshot);
                _servicesView?.SetStatus(ok ? Loc.T("BGE-M3 已停止", "BGE-M3 stopped") : Loc.T("停止失败：", "Stop failed: ") + detail);
            });
        });
    }

    internal void ServicesCopyBgeApi()
    {
        try { Clipboard.SetText(BgeM3Service.EmbeddingsApi); _servicesView?.SetStatus(Loc.T("已复制 API 地址", "API address copied")); }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("复制失败：", "Copy failed: ") + ex.Message); }
    }

    internal void ServicesOpenBgeLog()
    {
        try
        {
            if (System.IO.File.Exists(BgeM3Service.LogPath))
                Process.Start(new ProcessStartInfo("notepad.exe", BgeM3Service.LogPath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", System.IO.Path.GetDirectoryName(BgeM3Service.LogPath) ?? "") { UseShellExecute = true });
            _servicesView?.SetStatus(Loc.T("已打开日志", "Log opened"));
        }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("打开失败：", "Open failed: ") + ex.Message); }
    }
}
