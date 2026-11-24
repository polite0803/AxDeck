using System.Text.RegularExpressions;

namespace AxDeck.Core;

// 从卸载注册表（Uninstall）读到的一条已安装应用。只保留界面与匹配要用的字段，全部是注册表原文。
public sealed record InstalledApp(
    string Name,
    string Version,
    string? Publisher,
    string? IconPath,
    string? InstallLocation)
{
    // 从 DisplayIcon 推出来的可执行文件名（小写）。DisplayIcon 也常指向 .ico 或卸载程序，那时是 null。
    // 映射表的 exe 匹配与图标提取都用它。
    public string? Executable => InstalledApps.ExecutableFromIcon(IconPath);
}

public static class InstalledApps
{
    // Windows 补丁项的 DisplayName 长这样：KB5077181、Security Update for ... (KB1234567)。
    private static readonly Regex Patch = new(@"\bKB\d{6,8}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // 这些 ReleaseType 的条目是补丁/更新，不是独立应用。
    private static readonly string[] PatchReleases = ["Update", "Security Update", "Hotfix", "ServicePack", "Service Pack"];

    // 注册表里一条记录是否算「用户装的应用」。不算就返回 null，由调用方直接跳过。
    // systemComponent 与 releaseType 由 Native 侧从注册表取出后传进来（Core 不碰注册表类型）。
    public static InstalledApp? Create(
        string? displayName,
        string? version,
        string? publisher,
        string? displayIcon,
        string? installLocation,
        bool systemComponent = false,
        string? releaseType = null,
        string? parentKeyName = null)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name)) return null;

        // 系统组件是运行库、驱动、隐性安装件，列出来只会淹没真正能升级的应用。
        if (systemComponent) return null;
        if (!string.IsNullOrWhiteSpace(parentKeyName)) return null;
        if (Patch.IsMatch(name)) return null;
        if (releaseType is { Length: > 0 } kind)
        {
            foreach (var patch in PatchReleases)
                if (kind.Contains(patch, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return new InstalledApp(
            name,
            (version ?? "").Trim(),
            Blank(publisher),
            Blank(displayIcon),
            Blank(installLocation));
    }

    // DisplayIcon 的三种真实形式：带引号的 exe 路径、"C:\...\app.exe,0"、以及指向 .ico 或卸载程序。
    // 取扩展名是 .exe 的文件名并小写；其它一律返回 null（不要去猜）。
    public static string? ExecutableFromIcon(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;
        var path = displayIcon.Trim().Trim('"');

        // ",0" 索引后缀。只砍最后一个逗号之后的部分，路径里的逗号（罕见）因此不会误伤。
        var comma = path.LastIndexOf(',');
        if (comma > 0) path = path[..comma].Trim().Trim('"');

        var file = SoftwareCatalog.FileName(path);
        return file is { Length: > 4 } && file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file : null;
    }

    // 同名条目去重：同一应用可能同时出现在每用户与每机器的卸载键里。
    // 保留版本更高的那条（同版本时保留先出现的，调用方按 64 位优先喂进来），名称大小写不敏感。
    public static IReadOnlyList<InstalledApp> Distinct(IEnumerable<InstalledApp> apps)
    {
        var byName = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            if (!byName.TryGetValue(app.Name, out var kept)) { byName[app.Name] = app; continue; }
            if (SoftwareVersion.IsNewer(app.Version, kept.Version)) byName[app.Name] = app;
        }

        return [.. byName.Values.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    // 两个来源应用名的对齐键：小写、去括号内容、去尾部版本号。
    // "DBeaver 26.2.1 (current user)" 与 ARP 里的 "DBeaver" 因此能对上。只用于对齐，不用于显示。
    public static string Key(string? name)
    {
        var text = (name ?? "").Trim();
        if (text.Length == 0) return "";
        var cut = text.IndexOfAny(['(', '（']);
        if (cut > 0) text = text[..cut];
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        while (true)
        {
            var space = text.LastIndexOf(' ');
            if (space <= 0) break;
            var tail = text[(space + 1)..];
            // 只砍「像版本号」的尾段：含点且以数字开头（26.2.1、10.0.204）。"18"、"x64" 这类保留。
            if (!(tail.Length > 0 && char.IsAsciiDigit(tail[0]) && tail.Contains('.'))) break;
            text = text[..space];
        }
        return text.ToLowerInvariant();
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
