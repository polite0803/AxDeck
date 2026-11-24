using System.Windows;
using System.Windows.Threading;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 「软件更新」页：整台机器上已安装应用的可用更新，集中列出、逐项可更新。
// 三层数据源在 SoftwareChecker 里拼：winget upgrade 给主干，内置映射表补 WinGet 认不出的常见应用，
// 注册表只用来枚举已装了什么。这一页只负责显示和把点击转成动作。
public partial class MainWindow
{
    private SoftwareView? _softwareView;
    private string _softwareKey = "";
    private SoftwareUpdateState _softwareState = new();
    private SoftwareChecker? _softwareChecker;
    private SoftwareChannelFeed? _softwareFeed;
    private DispatcherTimer? _softwareTimer;
    private CancellationTokenSource? _softwareCheck;
    private bool _softwareChecking;
    private int _softwareFailures;
    private DateTimeOffset? _softwareLastFailure;
    private DateTimeOffset _softwareStarted;
    // 行内状态（更新中 / 已更新 / 失败）。键是 InstalledApps.Key，重画时按名称找回来。
    private readonly Dictionary<string, SoftwareRowState> _softwareRows = [];

    private void InitializeSoftware()
    {
        var store = new SoftwareUpdateStore(_app.Store.DirectoryPath);
        _softwareFeed = new SoftwareChannelFeed();
        _softwareChecker = new SoftwareChecker(store, WingetCli.Probe, UninstallRegistry.Enumerate,
            (source, token) => _softwareFeed.LatestAsync(source.Channel, token));
        _softwareState = store.Load();
        // 上次检查是失败的：重启后也先按第一次退避等 15 分钟，别一启动又跑一遍两分钟的 winget。
        if (_softwareState.Failure is { Length: > 0 }) { _softwareFailures = 1; _softwareLastFailure = DateTimeOffset.Now; }
        _softwareStarted = DateTimeOffset.Now;

        // 低频后台：每分钟看一眼是否到点，到点才真的跑 winget。只有主面板负责检查，
        // 多显示器上其余面板读同一份 software-updates.json，不必各自再跑一遍 1～2 分钟的 winget。
        _softwareTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _softwareTimer.Tick += (_, _) => { if (SoftwareDue()) _ = CheckSoftwareAsync(manual: false); };
        if (_lead is null && Settings.SoftwareAutoCheck) _softwareTimer.Start();

        Closed += (_, _) =>
        {
            _softwareTimer?.Stop();
            _softwareCheck?.Cancel();
            _softwareFeed?.Dispose();
            _softwareFeed = null;
        };
    }

    private void RenderSoftware()
    {
        bool shown = SoftwareContent.Visibility == Visibility.Visible && _softwareView is not null && SoftwareContent.Children.Contains(_softwareView);
        RefreshStatus();
        ShowPageContent(SoftwareContent);
        ModelToolbar.Visibility = Visibility.Collapsed; ApplicationPages.Visibility = Visibility.Collapsed; ShowClock(true);
        double width = ContentCanvas.Width, height = ContentCanvas.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        var header = PanelHeader.For(ToolbarFor(height), height < 170 ? 14 : 20);
        ApplyHeader(Settings.UsagePage ? header.WithControlsOf(CachedUsageHeader()) : header);
        SoftwareContent.Margin = new Thickness(_header.Side, 0, _header.Side, MonitorBottom(height));

        var key = SoftwareKey();
        if (_softwareView is not null && key == _softwareKey && SoftwareContent.Children.Contains(_softwareView))
        {
            _softwareView.Update(SoftwareItems(), SoftwarePageStatus(), !_softwareChecking, Soft.Enabled);
        }
        else
        {
            _softwareKey = key;
            var old = shown ? _softwareView : null;
            _softwareView = new SoftwareView();
            _softwareView.UpdateRequested += UpdateSoftwareRow;
            _softwareView.CheckRequested += () => _ = CheckSoftwareAsync(manual: true);
            _softwareView.Update(SoftwareItems(), SoftwarePageStatus(), !_softwareChecking, animate: false);
            if (old is not null) MonitorCrossfade.Swap(SoftwareContent, _softwareView, old, MonitorMotion);
            else { SoftwareContent.Children.Clear(); SoftwareContent.Children.Add(_softwareView); }
        }

        // 页面第一次打开时，缓存为空或超过 12 小时就立刻在后台查一次；数据还新就先用上次的结果，
        // 不在每次展开面板时都跑一遍 winget。
        if (_lead is null && Settings.SoftwareAutoCheck && !_softwareChecking && !SoftwareUpdateStore.IsFresh(_softwareState, DateTimeOffset.Now))
            _ = CheckSoftwareAsync(manual: false);
    }

    private string SoftwareKey() => string.Join("|", Loc.Language, PanelTheme.Generation, SystemParameters.HighContrast);

    private bool SoftwareDue() => DateTimeOffset.Now >= SoftwareCheckSchedule.Next(_softwareStarted, _softwareState, _softwareFailures, _softwareLastFailure);

    private IReadOnlyList<SoftwareItem> SoftwareItems()
    {
        var items = new List<SoftwareItem>(_softwareState.Updates.Count);
        foreach (var update in _softwareState.Updates)
        {
            string key = InstalledApps.Key(update.Name);
            var state = _softwareRows.GetValueOrDefault(key);
            // 只有下载页的项点下去是打开浏览器，按钮上直接说「下载」；其余是静默升级。
            string action = update.Id is { Length: > 0 } ? Loc.T("更新", "Update") : Loc.T("下载", "Download");
            items.Add(new SoftwareItem(update.Name, update.Current, update.Available, Badge(update), key,
                SoftwareUpdates.CanUpdate(update), action, state));
        }
        return items;
    }

    private static string Badge(SoftwareUpdate update) =>
        update.Origin == SoftwareUpdateOrigin.Winget ? "WinGet" : Loc.T("厂商", "Vendor");

    private SoftwareStatus SoftwarePageStatus()
    {
        var state = _softwareState;
        int count = state.Updates.Count;
        bool failed = state.Failure is { Length: > 0 };

        string title = _softwareChecking ? Loc.T("正在检查…", "Checking…")
            : state.MissingTool ? Loc.T("未检测到 WinGet", "WinGet not found")
            : failed ? Loc.T("暂时无法检查更新", "Updates cannot be checked right now")
            : Loc.T($"共 {count} 项可更新", $"{count} updates available");
        string? note = _softwareChecking ? Loc.T("首次检查约需 1–2 分钟", "The first check takes 1–2 minutes")
            : failed ? state.Failure
            : state.MissingTool ? Loc.T("安装「应用安装程序」(App Installer) 后即可检查", "Install App Installer to check for updates")
            : state.CheckedAt is { } at ? Loc.T($"上次检查：{at:MM-dd HH:mm}", $"Last checked {at:MM-dd HH:mm}")
            : null;
        string empty = count > 0 ? ""
            : _softwareChecking ? Loc.T("正在检查…", "Checking…")
            : state.MissingTool ? Loc.T("未检测到 WinGet", "WinGet not found")
            : failed ? Loc.T("暂时无法检查更新", "Updates cannot be checked right now")
            : Loc.T("未检测到可更新的应用", "No updates found");
        string? emptyNote = count > 0 ? null
            : _softwareChecking ? Loc.T("首次检查约需 1–2 分钟", "The first check takes 1–2 minutes")
            : state.MissingTool ? Loc.T("安装「应用安装程序」(App Installer) 后即可检查", "Install App Installer to check for updates")
            : null;
        return new SoftwareStatus(title, note, empty, emptyNote, _softwareChecking, !_softwareChecking);
    }

    // 点「立即检查」或定时器到点都走这里。重活在 SoftwareChecker 的工作线程上，回来时仍在 UI 线程。
    private async Task CheckSoftwareAsync(bool manual)
    {
        if (_softwareChecking || _softwareChecker is null) return;
        if (!manual && (!Settings.SoftwareAutoCheck || !SoftwareDue())) return;

        _softwareChecking = true;
        var source = new CancellationTokenSource();
        _softwareCheck = source;
        if (CurrentPage == DeckPage.Software) RefreshSoftware();
        try
        {
            var state = await _softwareChecker.CheckAsync(source.Token);
            _softwareState = state;
            if (state.Failure is null) { _softwareFailures = 0; _softwareLastFailure = null; }
            else { _softwareFailures++; _softwareLastFailure = DateTimeOffset.Now; }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            // 意料之外的失败也要如实说，并且保留上一次的列表：不能把「查不到」显示成「没有更新」。
            _softwareFailures++; _softwareLastFailure = DateTimeOffset.Now;
            _softwareState = new SoftwareUpdateState
            {
                CheckedAt = _softwareState.CheckedAt,
                Failure = ex.Message,
                MissingTool = _softwareState.MissingTool,
                Complete = false,
                Installed = _softwareState.Installed,
                Updates = _softwareState.Updates,
            };
        }
        finally
        {
            if (ReferenceEquals(_softwareCheck, source)) { _softwareCheck = null; source.Dispose(); }
            _softwareChecking = false;
            if (CurrentPage == DeckPage.Software) RefreshSoftware();
        }
    }

    private void RefreshSoftware()
    {
        if (CurrentPage != DeckPage.Software || _softwareView is null) return;
        _softwareView.Update(SoftwareItems(), SoftwarePageStatus(), !_softwareChecking, Soft.Enabled);
    }

    // 设置页读的是同一份状态与同一个入口：它只说这一页读了什么、什么时候读的，不自己再跑一遍 winget。
    internal SoftwareUpdateState SoftwareState => _softwareState;

    internal async Task<SoftwareUpdateState> CheckSoftwareNow()
    {
        // 已经有一轮在跑（自动检查或页面上按的）：等它结束再交结果，别立刻返回一份旧状态。
        // 每 250 毫秒回一次 UI 线程看标志；14 分钟还没完说明卡住了，按现有状态返回。
        if (!_softwareChecking) await CheckSoftwareAsync(manual: true);
        else for (int i = 0; i < 3360 && _softwareChecking; i++) await Task.Delay(250);
        return _softwareState;
    }

    // 一行里的「更新」/「下载」。WinGet 项逐个串行升级（winget 自己有锁，并发会互相失败），
    // 完成后重新检查一次；映射表项没有静默安装的办法，打开它的下载页。
    private async void UpdateSoftwareRow(SoftwareItem item)
    {
        var update = _softwareState.Updates.FirstOrDefault(candidate => InstalledApps.Key(candidate.Name) == item.Key);
        if (update is null) return;
        if (update.Id is { Length: > 0 } id)
        {
            SetSoftwareRow(item.Key, SoftwareRowState.Busy);
            try
            {
                var result = await Task.Run(() => WingetCli.Upgrade(id));
                SetSoftwareRow(item.Key, result.ExitCode == 0 ? SoftwareRowState.Done : SoftwareRowState.Failed);
            }
            catch (Exception) { SetSoftwareRow(item.Key, SoftwareRowState.Failed); }
            await CheckSoftwareAsync(manual: true);
            return;
        }
        if (update.Page is { Length: > 0 } page)
            try { UI.OpenUrl(page); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void SetSoftwareRow(string key, SoftwareRowState state)
    {
        _softwareRows[key] = state;
        RefreshSoftware();
    }
}
