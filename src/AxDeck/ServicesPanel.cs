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

    // The Android emulator's state. It is read every few seconds while this page is open, so the panel shows the
    // emulator booting and coming online without the user having to ask for it.
    private DispatcherTimer? _axtvTimer;
    private bool _axtvPolling;

    // Reads services.json again and hands it to the page and the embedding server. The file belongs to the user; nothing in it
    // ships with AxDeck. Reloading on every open picks up an edit without a restart.
    private void ServicesReloadConfig()
    {
        var config = ServicesConfig.Load(_app.Store.DirectoryPath);
        CloudflareTunnel.Config = config;
        _bge.ModelPath = config.Bge.ModelPath;
        _servicesConfigPath = config.FilePath;
    }

    private string _servicesConfigPath = "";

    private void RenderServices()
    {
        bool shown = ServicesContent.Visibility == Visibility.Visible && _servicesView is not null && ServicesContent.Children.Contains(_servicesView);
        ServicesReloadConfig();
        RefreshStatus();
        ShowPageContent(ServicesContent);
        ModelToolbar.Visibility = Visibility.Collapsed; ApplicationPages.Visibility = Visibility.Collapsed; ClockLabel.Visibility = Visibility.Visible;
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        // The header keeps the usage page's controls, as the music and monitor pages do, so the switch does not move between pages.
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        ServicesContent.Margin = new Thickness(_header.Side, 0, _header.Side, 10);
        // The embedding server is watched while this page is open, the way the old panel watched it every two seconds.
        EnsureBgeTimer();
        // The emulator's state is read as the page opens, so it is never shown as unknown.
        ServicesRefreshAxTv();
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
        ServicesReloadConfig();
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
        // Without a host in services.json there is nothing to connect to; say so instead of running `ssh ""`.
        if (string.IsNullOrWhiteSpace(host))
        {
            _servicesView?.SetStatus(Loc.T("services.json 里还没有配置 SSH Host（tunnel.defaultSshHost）。", "No SSH host configured yet: set tunnel.defaultSshHost in services.json."));
            return;
        }
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
        if (string.IsNullOrWhiteSpace(CloudflareTunnel.DefaultSshHost))
        {
            _servicesView?.SetStatus(Loc.T("services.json 里还没有配置 SSH Host（tunnel.defaultSshHost）。", "No SSH host configured yet: set tunnel.defaultSshHost in services.json."));
            return;
        }
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

    // services.json: the site list, the tunnel host and the model path. Written from a template the first time, so the file
    // opened is never empty and every placeholder in it says what it is for.
    internal void ServicesOpenConfig()
    {
        try
        {
            var config = ServicesConfig.Load(_app.Store.DirectoryPath);
            if (!System.IO.File.Exists(config.FilePath))
            {
                System.IO.Directory.CreateDirectory(_app.Store.DirectoryPath);
                System.IO.File.WriteAllText(config.FilePath, config.Template());
            }
            Process.Start(new ProcessStartInfo("notepad.exe", config.FilePath) { UseShellExecute = true });
            _servicesView?.SetStatus(Loc.T("已打开 ", "Opened ") + config.FilePath + Loc.T("，保存后点「刷新」生效", "; save it, then press Refresh"));
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
            // Where the entries come from, so a diagnostic pasted elsewhere says which file was read.
            sb.AppendLine($"services.json: {_servicesConfigPath} ({(System.IO.File.Exists(_servicesConfigPath) ? "present" : "missing")}, {CloudflareTunnel.Sites.Count} sites)");
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
        if (_axtvTimer is null)
        {
            _axtvTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _axtvTimer.Tick += (_, _) => ServicesPollAxTv();
        }
        if (!_axtvTimer.IsEnabled) _axtvTimer.Start();
    }

    internal void ServicesPollBge()
    {
        if (CurrentPage != DeckPage.Services || ServicesContent.Visibility != Visibility.Visible) { _bgeTimer?.Stop(); _axtvTimer?.Stop(); return; }
        if (_bgePolling) return;
        _bgePolling = true;
        Task.Run(() =>
        {
            var snapshot = _bge.Poll();
            _bgePolling = false;
            OnUi(() => _servicesView?.UpdateBge(snapshot));
        });
    }

    // The emulator is polled on its own, a little less often than the embedding server: its state is read
    // through adb, which is slower than a local HTTP call.
    internal void ServicesPollAxTv()
    {
        if (CurrentPage != DeckPage.Services || ServicesContent.Visibility != Visibility.Visible) { _axtvTimer?.Stop(); return; }
        if (_axtvPolling) return;
        _axtvPolling = true;
        Task.Run(() =>
        {
            string avd = AxTvAvd;
            var status = AxTvEmulator.CollectStatus(avd, quick: true);
            _axtvPolling = false;
            OnUi(() => _servicesView?.UpdateAxTv(status, avd));
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

    // The log in a window of its own, read out of the file the server writes. The file is read off the UI thread: a long
    // log takes a moment, and the panel must not stall while it is read.
    internal void ServicesShowBgeLog()
    {
        Task.Run(() =>
        {
            var lines = BgeM3Service.ReadLogTail(ServiceLog.DefaultLines);
            OnUi(() =>
            {
                var window = new ServiceLogWindow(Settings.Theme, Loc.T("BGE-M3 日志", "BGE-M3 log"), lines, BgeM3Service.LogPath) { Owner = this };
                window.Show();
            });
        });
    }

    // The emulator's log, in a window of its own, the same way as the embedding server's.
    internal void ServicesShowAxTvLog()
    {
        Task.Run(() =>
        {
            var lines = AxTvEmulator.ReadLogTail(ServiceLog.DefaultLines);
            OnUi(() =>
            {
                var window = new ServiceLogWindow(Settings.Theme, Loc.T("AxTV 日志", "AxTV log"), lines, AxTvEmulator.LogPath) { Owner = this };
                window.Show();
            });
        });
    }

    // Whether the embedding server starts with Windows on its own. The command line is written into the Run key, next to
    // AxDeck's own entry but under its own name, so the two can be turned off separately.
    internal bool ServicesBgeAutostartEnabled()
    {
        try { return StartupRegistration.IsBgeEnabled(); }
        catch (System.Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    internal void ServicesSetBgeAutostart(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var server = BgeM3Service.ResolveServerPath();
                if (BgeAutostart.Refusal(server, _bge.ModelPath) is { } refusal)
                {
                    _servicesView?.SetStatus(refusal, true);
                    _servicesView?.SetBgeAutostart(false);
                    return;
                }
                StartupRegistration.SetBgeEnabled(true, BgeAutostart.Command(server!, _bge.ModelPath));
                _servicesView?.SetStatus(Loc.T("已设置 BGE-M3 开机自启", "BGE-M3 now starts with Windows"));
            }
            else
            {
                StartupRegistration.SetBgeEnabled(false, "");
                _servicesView?.SetStatus(Loc.T("已取消 BGE-M3 开机自启", "BGE-M3 no longer starts with Windows"));
            }
            _servicesView?.SetBgeAutostart(enabled);
        }
        catch (System.Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _servicesView?.SetStatus(Loc.T("无法设置开机自启：", "Could not change the startup setting: ") + ex.Message, true);
            _servicesView?.SetBgeAutostart(ServicesBgeAutostartEnabled());
        }
    }

    // ----------------------------------------------------------------------
    // The Android emulator (the old "AxTV 模拟器管理器", ported to native C#)
    // ----------------------------------------------------------------------

    // The AVD the panel is set to, and the one the machine has that comes after it.
    private string AxTvAvd => AxTvEmulator.ResolveAvd(Settings.AxTvAvd);

    private string NextAxTvAvd()
    {
        var avds = AxTvEmulator.ListAvds();
        if (avds.Count == 0) return AxTvAvd;
        int index = avds.ToList().FindIndex(name => string.Equals(name, AxTvAvd, StringComparison.OrdinalIgnoreCase));
        return avds[(index + 1) % avds.Count];
    }

    // Reads the emulator's state and shows it. The reading runs off the UI thread: adb and the window calls take
    // their time, and the panel stays responsive while they do.
    internal void ServicesRefreshAxTv(bool quick = false)
    {
        Task.Run(() =>
        {
            string avd = AxTvAvd;
            var status = AxTvEmulator.CollectStatus(avd, quick);
            OnUi(() => _servicesView?.UpdateAxTv(status, avd));
        });
    }

    internal void ServicesBrowseApk()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("选择 APK", "Choose an APK"),
            Filter = Loc.T("Android 安装包 (*.apk)|*.apk|所有文件 (*.*)|*.*", "Android package (*.apk)|*.apk|All files (*.*)|*.*"),
            InitialDirectory = System.IO.Directory.Exists(AxTvEmulator.ShotDir) ? null : null,
        };
        if (dialog.ShowDialog() == true) _servicesView?.SetApkPath(dialog.FileName);
    }

    internal void ServicesInstallApk()
    {
        string path = _servicesView?.ApkPath.Trim() ?? "";
        if (path.Length == 0) { _servicesView?.SetStatus(Loc.T("请先选择一个 .apk 文件", "Choose an .apk file first")); return; }
        _servicesView?.SetStatus(Loc.T("安装中…", "Installing…"));
        Task.Run(() =>
        {
            var result = AxTvEmulator.InstallApk(path, Settings.AxTvAutoReinstall);
            string text = result.Outcome switch
            {
                AxTvEmulator.InstallOutcome.Success => Loc.T("安装成功", "Installed"),
                AxTvEmulator.InstallOutcome.SignatureMismatch => Loc.T("签名冲突：已装版本与这个 APK 的签名不同，无法覆盖安装",
                    "The signatures differ: this APK cannot be put over the installed one"),
                AxTvEmulator.InstallOutcome.AbiMismatch => Loc.T("ABI 不匹配：这个 AVD 装不了这个包", "ABI mismatch: this AVD cannot run this package"),
                _ => Loc.T("安装失败", "Installation failed"),
            };
            OnUi(() =>
            {
                _servicesView?.SetStatus(text + "\n" + result.Detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvStart()
    {
        string avd = AxTvAvd;
        _servicesView?.SetStatus(Loc.T("正在启动模拟器…", "Starting the emulator…"));
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.Start(avd);
            OnUi(() =>
            {
                _servicesView?.SetStatus(ok ? Loc.T("模拟器已启动，正在等待设备上线…", "The emulator started; waiting for the device…") : Loc.T("启动失败：", "Start failed: ") + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvStop()
    {
        _servicesView?.SetStatus(Loc.T("正在关闭模拟器…", "Stopping the emulator…"));
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.Stop();
            OnUi(() =>
            {
                _servicesView?.SetStatus(ok ? Loc.T("模拟器已关闭", "The emulator is closed") : Loc.T("关闭未完成：", "Closing did not finish: ") + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvForceKill()
    {
        _servicesView?.SetStatus(Loc.T("正在结束模拟器进程…", "Ending the emulator processes…"));
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.ForceKill();
            OnUi(() =>
            {
                _servicesView?.SetStatus(ok ? Loc.T("进程已清空", "The processes are gone") : Loc.T("仍有残留进程", "Processes are still running") + "\n" + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvRestart()
    {
        _servicesView?.SetStatus(Loc.T("正在重启模拟器…", "Restarting the emulator…"));
        Task.Run(() =>
        {
            AxTvEmulator.Stop();
            Thread.Sleep(2000);
            var (ok, detail) = AxTvEmulator.Start(AxTvAvd);
            OnUi(() =>
            {
                _servicesView?.SetStatus(ok ? Loc.T("模拟器已重启", "The emulator restarted") : Loc.T("重启失败：", "Restart failed: ") + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    // Switching the AVD means closing the running one and starting the other: the two share the same port and the
    // same SDK, so they cannot run side by side.
    internal void ServicesAxTvSwitchAvd()
    {
        string next = NextAxTvAvd();
        if (string.Equals(next, AxTvAvd, StringComparison.OrdinalIgnoreCase))
        {
            _servicesView?.SetStatus(Loc.T("这台机器上只有这一个 AVD", "This machine has only this one AVD"));
            return;
        }
        Settings.AxTvAvd = next; _app.QueueSave();
        _servicesView?.SetStatus(Loc.T("正在切换到 ", "Switching to ") + next + Loc.T("，先关闭当前的…", ", closing the current one first…"));
        Task.Run(() =>
        {
            if (AxTvEmulator.DeviceOnline() || AxTvEmulator.EmulatorProcessAlive())
            {
                AxTvEmulator.Stop();
                if (AxTvEmulator.EmulatorProcessAlive()) AxTvEmulator.ForceKill();
                Thread.Sleep(2000);
            }
            var (ok, detail) = AxTvEmulator.Start(next);
            OnUi(() =>
            {
                _servicesView?.SetStatus(ok ? Loc.T("已切换到 ", "Switched to ") + next : Loc.T("切换失败：", "Switch failed: ") + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvFullscreen()
    {
        string avd = AxTvAvd;
        Task.Run(() =>
        {
            IntPtr window = EmulatorWindow.FindEmulatorWindow();
            if (window == IntPtr.Zero) { OnUi(() => _servicesView?.SetStatus(Loc.T("没找到模拟器窗口", "The emulator window was not found"))); return; }
            var profile = AxTvEmulator.ProfileFor(avd);
            var rect = EmulatorWindow.EnterFullscreen(window, profile.Aspect, profile.Portrait);
            OnUi(() =>
            {
                _servicesView?.SetStatus(Loc.T("已切到无边框全屏，窗口 ", "Borderless full screen, window ") + rect);
                ServicesRefreshAxTv(true);
            });
        });
    }

    internal void ServicesAxTvSmall()
    {
        string avd = AxTvAvd;
        Task.Run(() =>
        {
            IntPtr window = EmulatorWindow.FindEmulatorWindow();
            if (window == IntPtr.Zero) { OnUi(() => _servicesView?.SetStatus(Loc.T("没找到模拟器窗口", "The emulator window was not found"))); return; }
            var profile = AxTvEmulator.ProfileFor(avd);
            var rect = EmulatorWindow.MoveSmall(window, profile.Aspect, profile.Portrait);
            OnUi(() =>
            {
                _servicesView?.SetStatus(Loc.T("窗口已归位到小窗：", "The window is back in its small shape: ") + rect);
                ServicesRefreshAxTv(true);
            });
        });
    }

    internal void ServicesAxTvResetWindow()
    {
        ServicesAxTvSmall();
    }

    // The guest screen is turned to portrait and back. The window keeps its shape: the emulator holds the aspect
    // ratio of its physical panel, so only the picture inside it changes.
    internal void ServicesAxTvToggleOrientation()
    {
        string avd = AxTvAvd;
        _servicesView?.SetStatus(Loc.T("正在切换显示方向…", "Switching the orientation…"));
        Task.Run(() =>
        {
            var profile = AxTvEmulator.ProfileFor(avd);
            if (profile.Portrait)
            {
                OnUi(() => _servicesView?.SetStatus(Loc.T("当前是竖屏 AVD：窗口的形状由物理面板定死，要换方向得冷启动另一个 AVD",
                    "This is a portrait AVD: the window's shape is fixed by its physical panel; switching needs a cold start of the other AVD")));
                return;
            }
            if (!AxTvEmulator.DeviceOnline()) { OnUi(() => _servicesView?.SetStatus(Loc.T("模拟器未运行", "The emulator is not running"))); return; }
            bool goPortrait = AxTvEmulator.WmOverride() != AxTvEmulator.PortraitSize;
            IntPtr window = EmulatorWindow.FindEmulatorWindow();
            bool wasFullscreen = window != IntPtr.Zero && EmulatorWindow.IsFullscreen(window);
            AxTvEmulator.SetWmOverride(goPortrait ? AxTvEmulator.PortraitSize : null);
            Thread.Sleep(2000);
            if (window != IntPtr.Zero)
            {
                // A full screen window keeps its shape; anything else goes back to the small centred one.
                if (wasFullscreen || goPortrait) EmulatorWindow.EnsureVisible(window);
                else EmulatorWindow.ApplyWindowRect(window, EmulatorWindow.SmallWindowRect(EmulatorWindow.ActiveMonitorRect(window), profile.Aspect));
            }
            var (ok, bytes) = AxTvEmulator.DisplayHealth();
            if (!ok)
            {
                // The picture did not come up: the screen goes back to its native size and the window is restored.
                AxTvEmulator.SetWmOverride(null);
                if (window != IntPtr.Zero) EmulatorWindow.ApplyWindowRect(window, EmulatorWindow.SmallWindowRect(EmulatorWindow.ActiveMonitorRect(window), profile.Aspect));
                OnUi(() => _servicesView?.SetStatus(Loc.T("画面自检未通过，已回滚到横屏", "The picture check failed; rolled back to landscape") + $" ({bytes} B)"));
                ServicesRefreshAxTv();
                return;
            }
            bool relaunched = AxTvEmulator.RelayoutApp();
            if (wasFullscreen && window != IntPtr.Zero) EmulatorWindow.SetFullscreenGeometry(window, profile.Aspect, profile.Portrait);
            OnUi(() =>
            {
                _servicesView?.SetStatus((goPortrait ? Loc.T("已切换到竖屏", "Switched to portrait") : Loc.T("已切换到横屏", "Switched to landscape"))
                    + $" ({bytes} B" + (relaunched ? Loc.T(", 已重启 AxTV", ", AxTV restarted") : "") + ")");
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvLaunchApp()
    {
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.LaunchApp();
            OnUi(() => _servicesView?.SetStatus((ok ? Loc.T("已启动 AxTV", "AxTV started") : Loc.T("启动失败：", "Start failed: ")) + detail));
        });
    }

    internal void ServicesAxTvStopApp()
    {
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.StopApp();
            OnUi(() => _servicesView?.SetStatus((ok ? Loc.T("已停止 AxTV", "AxTV stopped") : Loc.T("停止失败：", "Stop failed: ")) + detail));
        });
    }

    internal void ServicesAxTvUninstallApp()
    {
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.UninstallApp();
            OnUi(() =>
            {
                _servicesView?.SetStatus((ok ? Loc.T("已卸载 AxTV", "AxTV uninstalled") : Loc.T("卸载失败：", "Uninstall failed: ")) + detail);
                ServicesRefreshAxTv();
            });
        });
    }

    internal void ServicesAxTvScreenshot()
    {
        _servicesView?.SetStatus(Loc.T("截图中…", "Taking a screenshot…"));
        Task.Run(() =>
        {
            var (ok, detail) = AxTvEmulator.Screenshot();
            OnUi(() => _servicesView?.SetStatus((ok ? Loc.T("截图已保存：", "Screenshot saved: ") : Loc.T("截图失败：", "Screenshot failed: ")) + detail));
        });
    }

    internal void ServicesAxTvOpenLog()
    {
        try
        {
            if (System.IO.File.Exists(AxTvEmulator.LogPath))
                Process.Start(new ProcessStartInfo("notepad.exe", AxTvEmulator.LogPath) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", System.IO.Path.GetDirectoryName(AxTvEmulator.LogPath) ?? "") { UseShellExecute = true });
            _servicesView?.SetStatus(Loc.T("已打开日志", "Log opened"));
        }
        catch (System.Exception ex) { _servicesView?.SetStatus(Loc.T("打开失败：", "Open failed: ") + ex.Message); }
    }
}
