namespace AxDeck.Core;

// 一次 winget 查询的结论。MissingTool 表示这台机器没装 winget：这不是失败，界面上是「未检测到 WinGet」那种空态；
// Failure 是命令本身失败或超时的原文原因，如实显示，不编造结果。
public sealed record WingetProbe(WingetScan? Scan, bool MissingTool = false, string? Failure = null)
{
    public IReadOnlyList<SoftwareUpdate> Updates => SoftwareUpdates.FromWinget(Scan);
    public bool Complete => Scan?.Complete == true;
}

// 一次检查的编排：读已安装应用 → 跑 winget → 对映射表命中的应用问一下各自的最新版本 → 合并 → 落盘。
// 三层数据源各自失败都不影响其余层：winget 超时仍然显示映射表的结果，映射表接口不通也不影响 winget 的列表。
public sealed class SoftwareChecker
{
    private readonly SoftwareUpdateStore _store;
    private readonly Func<WingetProbe> _winget;
    private readonly Func<IReadOnlyList<InstalledApp>> _applications;
    private readonly Func<SoftwareSource, CancellationToken, Task<string?>> _latest;
    private readonly IReadOnlyList<SoftwareSource> _sources;
    private readonly Func<DateTimeOffset> _clock;

    public SoftwareChecker(
        SoftwareUpdateStore store,
        Func<WingetProbe> winget,
        Func<IReadOnlyList<InstalledApp>> applications,
        Func<SoftwareSource, CancellationToken, Task<string?>> latest,
        IReadOnlyList<SoftwareSource>? sources = null,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store; _winget = winget; _applications = applications; _latest = latest;
        _sources = sources ?? SoftwareCatalog.Sources;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public SoftwareUpdateStore Store => _store;

    // 全部重活都在工作线程上：winget 一次要 1～2 分钟，绝不能压 UI 线程。
    // 调用方在 UI 线程上 await 它，回来时仍在 UI 线程（await 默认捕获上下文），可以直接更新界面。
    public async Task<SoftwareUpdateState> CheckAsync(CancellationToken token = default)
    {
        var probe = await Task.Run(_winget, token);
        token.ThrowIfCancellationRequested();
        var apps = await Task.Run(_applications, token);
        token.ThrowIfCancellationRequested();
        var catalog = await CatalogAsync(apps, token);

        var state = new SoftwareUpdateState
        {
            CheckedAt = _clock(),
            Failure = probe.Failure,
            MissingTool = probe.MissingTool,
            Complete = probe.Complete,
            Installed = apps.Count,
            Updates = [.. SoftwareUpdates.Merge(probe.Updates, catalog)],
        };
        _store.Save(state);
        return state;
    }

    // 层 2：映射表命中的应用逐个问最新版本。同一条通道只问一次；不同通道并发，每条通道自己带 15 秒超时，
    // 串行会让几条不通的通道把整次检查拖到几分钟。
    private async Task<IReadOnlyList<SoftwareUpdate>> CatalogAsync(IReadOnlyList<InstalledApp> apps, CancellationToken token)
    {
        if (_sources.Count == 0) return [];
        var matched = new Dictionary<SoftwareSource, List<InstalledApp>>();
        foreach (var app in apps)
        {
            if (SoftwareCatalog.Match(_sources, app.Executable, app.Name) is not { } source) continue;
            if (!matched.TryGetValue(source, out var list)) matched[source] = list = [];
            list.Add(app);
        }
        if (matched.Count == 0) return [];

        var versions = new Dictionary<SoftwareSource, string?>();
        await Task.WhenAll(matched.Keys.Select(async source =>
        {
            string? latest = null;
            try { latest = await _latest(source, token).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
            lock (versions) versions[source] = latest;
        }));

        var updates = new List<SoftwareUpdate>();
        foreach (var (source, list) in matched)
            foreach (var app in list)
                if (SoftwareUpdates.FromCatalog(source, app, versions.GetValueOrDefault(source)) is { } update)
                    updates.Add(update);
        return updates;
    }
}

// 自动检查的节奏，与 AxDeck 自身的更新检查一致：启动后 90 秒首次，之后 6 小时；失败按 15 分钟、1 小时、4 小时退避。
// 退避次数只在内存里：重启本来就该重新看一眼，没必要把它落盘。
public static class SoftwareCheckSchedule
{
    public static TimeSpan StartupDelay => UpdateSchedule.StartupDelay;
    public static TimeSpan Interval => UpdateSchedule.Interval;

    public static DateTimeOffset Next(DateTimeOffset started, SoftwareUpdateState state, int failures, DateTimeOffset? lastFailure)
    {
        var due = started + StartupDelay;
        if (state.CheckedAt is { } success && success + Interval > due) due = success + Interval;
        if (failures > 0 && lastFailure is { } failure && failure + UpdateSchedule.Backoff(failures) > due)
            due = failure + UpdateSchedule.Backoff(failures);
        return due;
    }
}
