namespace AxDeck.Core;

// 一条可更新的来源。面板按这个值显示来源标记，也据此决定点「更新」时走哪条路。
public enum SoftwareUpdateOrigin { Winget, Catalog }

// 面板上的一行：一个应用当前版本与可用版本。字段都是原样字符串，比较已由 SoftwareVersion 做过，显示不再改写。
// Id 只有 WinGet 来源才有（静默升级要用）；Page 是映射表给的手工下载页。
public sealed record SoftwareUpdate(
    string Name,
    string Current,
    string Available,
    SoftwareUpdateOrigin Origin,
    string? Id = null,
    string? Page = null,
    string? Icon = null);

// 把三层数据源拼成一张可更新列表。
public static class SoftwareUpdates
{
    // 层 1：winget upgrade 的输出本身就是可更新列表，逐条照抄。
    public static IReadOnlyList<SoftwareUpdate> FromWinget(WingetScan? scan) =>
        scan is null ? [] : [.. scan.Entries.Select(entry => new SoftwareUpdate(
            entry.Name, entry.Version, entry.Available, SoftwareUpdateOrigin.Winget, entry.Id))];

    // 层 2：映射表通道报回的最新版本。只有确实比已装版本新才算——接口答非所问时宁可没有，也不要误报。
    public static SoftwareUpdate? FromCatalog(SoftwareSource source, InstalledApp app, string? available)
    {
        var text = available?.Trim();
        if (string.IsNullOrEmpty(text) || !SoftwareVersion.IsNewer(text, app.Version)) return null;
        return new SoftwareUpdate(app.Name, app.Version, text, SoftwareUpdateOrigin.Catalog, null, source.Page, app.IconPath);
    }

    // 合并两批结果。同一应用两边都有时保留 primary（WinGet）那条——它给的版本更权威、也能直接静默升级；
    // 但把对方才有的图标与下载页补过去，免得界面上少东西。
    public static IReadOnlyList<SoftwareUpdate> Merge(IEnumerable<SoftwareUpdate> primary, IEnumerable<SoftwareUpdate> secondary)
    {
        var byKey = new Dictionary<string, SoftwareUpdate>(StringComparer.Ordinal);
        foreach (var update in primary)
        {
            if (Key(update) is not { Length: > 0 } key) continue;
            byKey.TryAdd(key, update);
        }
        foreach (var update in secondary)
        {
            if (Key(update) is not { Length: > 0 } key) continue;
            if (!byKey.TryGetValue(key, out var kept)) { byKey[key] = update; continue; }

            var merged = kept with
            {
                Page = kept.Page ?? update.Page,
                Icon = kept.Icon ?? update.Icon,
            };
            byKey[key] = merged;
        }
        return Sorted(byKey.Values);
    }

    // 名称升序。面板列出的全部是「有更新」的项，不需要再把有更新的排前面。
    public static IReadOnlyList<SoftwareUpdate> Sorted(IEnumerable<SoftwareUpdate> updates) =>
        [.. updates.OrderBy(update => update.Name, StringComparer.CurrentCultureIgnoreCase)];

    // 既没有 ID 也没有下载页的项只能展示：界面据此不画「更新」按钮。
    public static bool CanUpdate(SoftwareUpdate update) => update.Id is { Length: > 0 } || update.Page is { Length: > 0 };

    private static string Key(SoftwareUpdate update) => InstalledApps.Key(update.Name);
}
