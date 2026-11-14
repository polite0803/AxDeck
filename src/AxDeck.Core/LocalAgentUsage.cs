using System.Globalization;
using System.Text.Json;

namespace AxDeck.Core;

// Where a locally installed agent keeps its own data. Everything this reader does is read-only and stays on this PC:
// it never asks the product's servers for a balance and never asks the user to type one in.
public sealed record LocalAgentPaths(string Home, string Roaming, string LocalAppData, bool Watch = true)
{
    public static LocalAgentPaths Detect() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string Root(LocalAgentRoot root) => root switch
    {
        LocalAgentRoot.Roaming => Roaming,
        LocalAgentRoot.LocalAppData => LocalAppData,
        _ => Home
    };
}

public enum LocalAgentRoot { Home, Roaming, LocalAppData }

// One file or VS Code-style state database owned by the application. LocalState is the Electron "Local State" that
// holds the key for values encrypted with the application's own safeStorage.
public sealed record LocalAgentProbe(LocalAgentRoot Root, string Relative, bool Database = false, string LocalState = "", string KeyHint = "");

// Known on-disk addresses of the products in AgentCatalog. Adding a product needs no change here unless its own
// data lives somewhere unusual: unknown keys fall back to their name under %APPDATA% and %USERPROFILE%.
public static class LocalAgentProbes
{
    private static readonly Dictionary<string, string[]> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["workbuddy"] = ["WorkBuddy"],
        ["trae"] = ["Trae CN", "Trae"],
        ["qoder"] = ["Qoder"],
        ["codebuddy"] = ["CodeBuddy CN", "CodeBuddy"],
        ["comate"] = ["Comate"],
        ["cursor"] = ["Cursor"],
        ["windsurf"] = ["Windsurf"],
        ["kiro"] = ["Kiro"],
        ["copilot"] = ["Code", "Code - Insiders"],
        ["cline"] = ["Code", "Code - Insiders"],
        ["roo-code"] = ["Code", "Code - Insiders"],
        ["marscode"] = ["Code", "Code - Insiders", "MarsCode"],
        ["lingma"] = ["Code", "Code - Insiders", "Lingma"],
        ["codegeex"] = ["Code", "Code - Insiders"],
        ["fitten"] = ["Code", "Code - Insiders"],
        ["tabnine"] = ["Code", "Code - Insiders"],
        ["augment"] = ["Code", "Code - Insiders", "Augment"],
        ["aider"] = ["Code", "Code - Insiders"],
        ["gemini-cli"] = ["Code", "Code - Insiders"],
        ["grok-build"] = ["Code", "Code - Insiders"]
    };

    private static readonly Dictionary<string, string[]> HomeFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["workbuddy"] = [".workbuddy"], ["trae"] = [".trae"], ["qoder"] = [".qoder"], ["codebuddy"] = [".codebuddy"],
        ["comate"] = [".comate"], ["gemini-cli"] = [".gemini"], ["qwen-code"] = [".qwen"], ["cline"] = [".cline"],
        ["aider"] = [".aider"], ["codegeex"] = [".codegeex"], ["marscode"] = [".marscode"], ["lingma"] = [".lingma"],
        ["fitten"] = [".fitten"], ["siliconflow"] = [".siliconflow"], ["minimax"] = [".minimax"], ["opencode"] = [".opencode"]
    };

    // Files an application may use for a quota snapshot. Only these names are opened, never a whole directory tree.
    private static readonly string[] QuotaFiles =
        ["quota.json", "credits.json", "credit.json", "balance.json", "usage.json", "account.json", "state.json", "storage.json", "usage-log.json"];

    public static IReadOnlyList<LocalAgentProbe> For(string key, string displayName)
    {
        string[] profiles = Profiles.TryGetValue(key, out var known) ? known : [displayName];
        string[] folders = HomeFolders.TryGetValue(key, out var homes) ? homes : ["." + key];
        var probes = new List<LocalAgentProbe>();
        foreach (var profile in profiles)
        {
            probes.Add(new(LocalAgentRoot.Roaming, Path.Join(profile, "User", "globalStorage", "state.vscdb"), true,
                Path.Join(profile, "Local State"), KeyHint(key)));
            foreach (var file in QuotaFiles.Take(5))
                probes.Add(new(LocalAgentRoot.Roaming, Path.Join(profile, "User", "globalStorage", file)));
        }
        foreach (var folder in folders)
            foreach (var file in QuotaFiles)
                probes.Add(new(LocalAgentRoot.Home, Path.Join(folder, file)));
        return probes;
    }

    // A shared editor profile holds several extensions' records; a hint keeps one product from showing another's quota.
    private static string KeyHint(string key) => key switch
    {
        "qoder" => "aicoding", "copilot" => "github", "codegeex" => "codegeex", "marscode" => "marscode",
        "comate" => "comate", "codebuddy" => "coding-copilot", "workbuddy" => "workbuddy", "trae" => "trae",
        "cline" => "cline", "roo-code" => "roo", "fitten" => "fitten", "lingma" => "lingma", "tabnine" => "tabnine",
        "augment" => "augment", "kiro" => "kiro", "windsurf" => "windsurf", "cursor" => "cursor", "aider" => "aider",
        _ => ""
    };

    // A column can point at a data file the product writes elsewhere. The path is configuration, not a value: it is
    // still reread on every sync, and a file that is gone or unreadable is reported rather than replaced by a guess.
    public static IReadOnlyList<LocalAgentProbe> Override(string path)
    {
        if (path.Length == 0) return [];
        bool database = new[] { ".db", ".vscdb", ".sqlite", ".sqlite3" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        var expanded = new List<string>();
        if (path.Contains('*') || path.Contains('?'))
        {
            try
            {
                var folder = Path.GetDirectoryName(path);
                if (folder is { Length: > 0 } && Directory.Exists(folder))
                    expanded.AddRange(Directory.GetFiles(folder, Path.GetFileName(path)).Take(20));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        else expanded.Add(path);
        // An Electron profile keeps its key three folders above the state database: .../Qoder/User/globalStorage/state.vscdb.
        string LocalState(string file)
        {
            var folder = Path.GetDirectoryName(file);
            for (int i = 0; i < 2 && !string.IsNullOrEmpty(folder); i++) folder = Path.GetDirectoryName(folder);
            return string.IsNullOrEmpty(folder) ? "" : Path.Combine(folder, "Local State");
        }
        return [.. expanded.Select(file => new LocalAgentProbe(LocalAgentRoot.Home, Path.GetFullPath(file), database,
            database ? LocalState(Path.GetFullPath(file)) : "", ""))];
    }
}

// A quota object found in an application's own record. Field names are kept so the column can say where a number came from.
internal sealed record LocalAgentQuota(string Path, string Field, decimal? Remaining, decimal? Total, decimal? Used,
    string Unit, bool Exceeded, DateTimeOffset RecordedAt, int Score);

public sealed class LocalAgentUsage : IDisposable
{
    private const int MaxFileBytes = 512 * 1024;
    private const int MaxProbes = 90;
    private const int MaxWatchers = 24;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);
    // Only quota-shaped keys are pulled out of a state database; tokens and other secrets never leave the query.
    private const string QuotaSql = """
        SELECT key, value FROM ItemTable
        WHERE lower(key) LIKE '%credit%' OR lower(key) LIKE '%quota%' OR lower(key) LIKE '%balance%'
           OR lower(key) LIKE '%point%' OR lower(key) LIKE '%allowance%' OR lower(key) LIKE '%usage%'
           OR key LIKE '%积分%' OR key LIKE '%余额%' LIMIT 40
        """;

    private readonly IDesktopSources _desktop;
    private readonly LocalAgentPaths _paths;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _watchGate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounce;
    private bool _disposed;
    public event Action? Changed;

    public LocalAgentUsage(IDesktopSources desktop, LocalAgentPaths paths, Func<DateTimeOffset>? clock = null)
    { _desktop = desktop; _paths = paths; _clock = clock ?? (() => DateTimeOffset.Now); }

    public IReadOnlyList<ProviderSnapshot> Read(IEnumerable<AppEntry> entries)
    {
        var snapshots = new List<ProviderSnapshot>();
        var watched = new List<string>();
        foreach (var entry in entries.Where(e => ProviderCatalog.IsLocalApp(e.QuotaSource)))
        {
            var (snapshot, files) = ReadOne(entry);
            snapshots.Add(snapshot);
            watched.AddRange(files);
        }
        WatchFiles(watched);
        return snapshots;
    }

    public ProviderTestResult Test(AppEntry entry)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var (snapshot, _) = ReadOne(entry);
        timer.Stop();
        return new(snapshot, timer.Elapsed.TotalSeconds, Checked(snapshot));
    }

    private static IReadOnlyList<string> Checked(ProviderSnapshot snapshot)
        => snapshot.Details.TryGetValue(Loc.T("检查位置", "Checked locations"), out var text) ? [text] : [];

    private (ProviderSnapshot Snapshot, IReadOnlyList<string> Files) ReadOne(AppEntry entry)
    {
        var now = _clock();
        var product = AgentCatalog.Find(entry.AgentKey)?.Name ?? entry.Name;
        var probes = LocalAgentProbes.Override(entry.LocalDataPath).Concat(LocalAgentProbes.For(
            entry.AgentKey.Length > 0 ? entry.AgentKey : product, product)).Take(MaxProbes).ToList();
        var snapshot = new ProviderSnapshot(ProviderId.LocalApp)
        { Configurations = [entry.ConfigurationKey], Source = product };
        var files = new List<string>();
        var found = new List<string>();
        LocalAgentQuota? best = null;
        string? failure = null;
        bool encrypted = false;

        foreach (var probe in probes)
        {
            string path;
            bool explicitPath = Path.IsPathRooted(probe.Relative);
            try { path = Path.GetFullPath(explicitPath ? probe.Relative : Path.Combine(_paths.Root(probe.Root), probe.Relative)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            // Built-in probes only open the application's own data under a known root; a path the user entered is followed as given.
            if (!explicitPath && !UnderRoot(path)) continue;
            if (found.Count < 8) found.Add(Short(path));
            if (!File.Exists(path)) continue;
            files.Add(path);
            try
            {
                if (probe.Database)
                {
                    var localState = Path.Combine(_paths.Root(probe.Root), probe.LocalState);
                    foreach (var text in DatabaseValues(path, probe.KeyHint, localState, ref encrypted))
                        Consider(ref best, Parse(text, path, File.GetLastWriteTimeUtc(path)));
                }
                else
                {
                    var info = new FileInfo(path);
                    if (info.Length > MaxFileBytes) continue;
                    Consider(ref best, Parse(File.ReadAllText(path), path, info.LastWriteTimeUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            { failure = ex is UnauthorizedAccessException ? Loc.T("没有读取本机应用数据的权限。", "This PC refused access to the application's data.") : ex.Message; }
        }

        if (best is { Score: > 0 })
        {
            snapshot.LiveQuota = true; snapshot.QuotaTime = now;
            snapshot.IsStale = now - best.RecordedAt > StaleAfter;
            snapshot.StatusLabel = Loc.T("本地数据", "Local data");
            var time = best.RecordedAt.LocalDateTime.ToString(best.RecordedAt.LocalDateTime.Date == now.LocalDateTime.Date ? "HH:mm" : "MM-dd HH:mm");
            snapshot.Status = Loc.T($"{product} 保存在本机的记录，更新于 {time}；每次同步自动重读。",
                $"From {product}'s own record on this PC, written at {time}; reread automatically on every sync.");
            if (snapshot.IsStale) snapshot.Status += Loc.T(" 这份本机记录已超过 7 天，可能不是最新余额。", " This local record is over 7 days old and may not be the latest balance.");
            if (best.Exceeded) snapshot.Status += Loc.T(" 记录显示额度已用尽。", " The record says the allowance is used up.");
            snapshot.Source = product + Loc.T(" · 本地应用数据", " · local app data");
            snapshot.Metrics = [Metric(best)];
            snapshot.Details = new Dictionary<string, string>
            {
                [Loc.T("数据来源", "Data source")] = Short(best.Path),
                [Loc.T("读取字段", "Fields read")] = best.Field,
                [Loc.T("记录时间", "Record time")] = time,
                [Loc.T("检查位置", "Checked locations")] = string.Join("\n", found.Distinct().Take(8))
            };
        }
        else
        {
            snapshot.LiveQuota = false;
            bool any = files.Count > 0;
            snapshot.StatusLabel = failure is not null ? Loc.T("读取失败", "Read failed")
                : any ? Loc.T("本地无额度记录", "No local quota record") : Loc.T("未检测到本地数据", "No local data found");
            snapshot.Status = failure is not null ? failure
                : any ? (encrypted
                    ? Loc.T($"已找到 {product} 的本机数据，但额度记录已加密且无法解密，或其中没有可识别的积分/额度字段；这里不会显示估算值。",
                        $"{product}'s local data was found, but its quota record is encrypted and could not be decrypted, or has no recognizable credits/quota field. No estimate is shown.")
                    : Loc.T($"已找到 {product} 的本机数据，但其中没有可识别的积分/额度字段；这里不会显示估算值。",
                        $"{product}'s local data was found, but it has no recognizable credits/quota field. No estimate is shown."))
                : Loc.T($"未在本机找到 {product} 保存的应用数据。请确认应用已安装、登录并产生过用量记录；读不到时这里不会显示估算值。",
                    $"No data saved by {product} was found on this PC. Check that the app is installed, signed in and has recorded usage. No estimate is shown when it cannot be read.");
            snapshot.Details = new Dictionary<string, string>
            { [Loc.T("检查位置", "Checked locations")] = string.Join("\n", found.Distinct().Take(8)) };
        }
        return (snapshot, files);
    }

    // A built-in probe must stay under one of the app-data roots; the separator check keeps sibling folders out.
    private bool UnderRoot(string path)
    {
        string[] roots = [_paths.Home, _paths.Roaming, _paths.LocalAppData];
        return roots.Any(root => root.Length > 0 &&
            (path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));
    }

    private IReadOnlyList<string> DatabaseValues(string database, string hint, string localState, ref bool encrypted)
    {
        var rows = _desktop.QueryDatabase(database, QuotaSql);
        if (rows is null) return [];
        var values = new List<string>();
        foreach (var row in rows)
        {
            if (row.Length < 2 || row[0] is null || row[1] is null) continue;
            string key = row[0]!, value = Unwrap(row[1]!);
            if (hint.Length > 0 && !key.Contains(hint, StringComparison.OrdinalIgnoreCase)) continue;
            values.Add(value);
            if (LooksEncrypted(value))
            {
                encrypted = true;
                string? plain = null;
                try { plain = _desktop.DecryptSecret(localState, value); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                if (plain is { Length: > 0 }) values.Add(plain);
            }
        }
        return values;
    }

    private static string Unwrap(string value)
    {
        var trimmed = value.AsSpan().Trim();
        if (trimmed.Length > 1 && trimmed[0] == '"')
        {
            try { using var doc = JsonDocument.Parse(value); if (doc.RootElement.ValueKind == JsonValueKind.String) return doc.RootElement.GetString() ?? value; }
            catch (JsonException) { }
        }
        return value;
    }

    internal static bool LooksEncrypted(string value)
    {
        if (value.StartsWith("cursor-safe-storage:v1:", StringComparison.Ordinal)) return true;
        var trimmed = value.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{') return false;
        try
        {
            using var doc = JsonDocument.Parse(value);
            return string.Equals(doc.RootElement.Get("type").Text(), "Buffer", StringComparison.OrdinalIgnoreCase)
                && doc.RootElement.Get("data").ValueKind == JsonValueKind.Array;
        }
        catch (JsonException) { return false; }
    }

    private static void Consider(ref LocalAgentQuota? best, LocalAgentQuota? candidate)
    {
        if (candidate is null or { Score: 0 }) return;
        if (best is null || candidate.Score > best.Score || (candidate.Score == best.Score && candidate.RecordedAt > best.RecordedAt)) best = candidate;
    }

    private static UsageMetric Metric(LocalAgentQuota quota)
    {
        string unit = quota.Unit.Length > 12 ? quota.Unit[..12] : quota.Unit;
        string note = Loc.T("来自本机应用记录", "From the local app record") + " · " + quota.Field
            + (unit.Length > 0 ? Loc.T(" · 单位 ", " · unit ") + unit : "");
        if (quota.Remaining is { } remaining)
            return new UsageMetric(MetricKind.Balance, Loc.T("积分余额", "Credits"))
            { Id = "local-credits", Amount = remaining, Total = quota.Total, Unit = unit, Note = note,
              ExpiresAt = null, Currency = "" };
        // A record that only says how much was used is shown as used / total rather than inventing a remaining balance.
        return new UsageMetric(MetricKind.Count, Loc.T("积分用量", "Credits used"))
        { Id = "local-credits", Used = quota.Used, Total = quota.Total, Unit = unit, Note = note };
    }

    // Small public probes for the console test runner; the record itself stays internal to the reader.
    public static bool HasQuota(string text) => Parse(text, "test", DateTime.UtcNow) is not null;
    public static bool IsEncrypted(string value) => LooksEncrypted(value);

    // Parses JSON, a JSON-lines log or a JSON string held in a database cell. Only quota-shaped objects are accepted;
    // a token, a banner or a list of dates never becomes a number.
    internal static LocalAgentQuota? Parse(string text, string path, DateTime recordedUtc)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxFileBytes) return null;
        var recorded = new DateTimeOffset(recordedUtc, TimeSpan.Zero);
        var pieces = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            pieces.Add(text);
            if (doc.RootElement.ValueKind == JsonValueKind.String) pieces.Add(doc.RootElement.GetString() ?? "");
        }
        catch (JsonException)
        {
            var lines = text.Split('\n');
            for (int i = lines.Length - 1; i >= 0 && pieces.Count < 3; i--)
                if (lines[i].Trim().Length > 0) pieces.Add(lines[i].Trim());
        }
        LocalAgentQuota? best = null;
        foreach (var piece in pieces)
        {
            try
            {
                using var doc = JsonDocument.Parse(piece);
                var found = Walk(doc.RootElement, "", 0);
                if (found is { Score: > 0 } quota && (best is null || quota.Score > best.Score))
                    best = quota with { Path = path, RecordedAt = recorded };
            }
            catch (JsonException) { }
        }
        return best;
    }

    private static LocalAgentQuota? Walk(JsonElement node, string path, int depth)
    {
        if (depth > 6) return null;
        LocalAgentQuota? best = null;
        if (node.ValueKind == JsonValueKind.Object)
        {
            best = FromObject(node, path);
            foreach (var property in node.EnumerateObject())
            {
                var child = Walk(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, depth + 1);
                if (child is { Score: > 0 } && (best is null || child.Score > best.Score)) best = child;
            }
            // A "quota exceeded" flag usually sits beside the quota object rather than inside it.
            if (best is { Score: > 0 } && Flag(node, "isQuotaExceeded", "quotaExceeded", "exceeded")) best = best with { Exceeded = true };
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            int count = node.GetArrayLength();
            for (int i = Math.Max(0, count - 8); i < count; i++)
            {
                var child = Walk(node[i], path + "[" + i + "]", depth + 1);
                if (child is { Score: > 0 } && (best is null || child.Score > best.Score)) best = child;
            }
        }
        return best;
    }

    private static LocalAgentQuota? FromObject(JsonElement obj, string path)
    {
        decimal? remaining = Number(obj, "remaining", "remainingCredits", "remain", "creditsLeft", "left", "balance",
            "available", "credit", "credits", "points", "pointBalance", "剩余", "余额", "积分");
        decimal? total = Number(obj, "total", "totalCredits", "quota", "limit", "allowance", "granted", "grant",
            "planCredits", "totalQuota", "总额", "总量", "总额度");
        decimal? used = Number(obj, "used", "usedCredits", "consumed", "spent", "usage", "已用", "消耗");
        // A record that only reports a percentage is ambiguous (some apps write 0..1, others 0..100), so it is not
        // turned into a number. Honesty about missing data matters more than filling the column.
        if (remaining is null && total is null && used is null) return null;
        decimal? left = remaining ?? (total is { } t && used is { } u ? Math.Max(0, t - u) : null);
        // A lone "used" or "total" is not a balance: without a pair there is nothing honest to draw.
        if (left is null && (used is null || total is null)) return null;
        int score = left is not null && total is not null ? 5 : used is not null && total is not null ? 4 : left is not null ? 3 : 1;
        string unit = Text(obj, "unit", "currency");
        bool exceeded = Flag(obj, "isQuotaExceeded", "quotaExceeded", "exceeded");
        string field = string.Join(", ", new[] {
            remaining is not null ? "remaining" : "", total is not null ? "total" : "", used is not null ? "used" : "" }.Where(s => s.Length > 0));
        return new LocalAgentQuota("", (path.Length > 0 ? path + "." : "") + field, left, total, used, unit, exceeded, default, score);
    }

    private static decimal? Number(JsonElement obj, params string[] names)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDecimal(out var number) && number >= 0) return number;
            if (property.Value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(property.Value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0) return parsed;
        }
        return null;
    }

    private static string Text(JsonElement obj, params string[] names)
    {
        foreach (var property in obj.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                return (property.Value.GetString() ?? "").Trim();
        return "";
    }

    private static bool Flag(JsonElement obj, params string[] names)
    {
        foreach (var property in obj.EnumerateObject())
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.True) return true;
        return false;
    }

    private static string Short(string path)
    {
        try
        {
            var file = Path.GetFileName(path);
            var parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
            return parent.Length > 0 ? parent + "/" + file : file;
        }
        catch (ArgumentException) { return path; }
    }

    private void WatchFiles(IReadOnlyList<string> files)
    {
        if (!_paths.Watch || _disposed) return;
        lock (_watchGate)
        {
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
            foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxWatchers))
            {
                try
                {
                    var watcher = new FileSystemWatcher(Path.GetDirectoryName(file)!, Path.GetFileName(file))
                    { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName, EnableRaisingEvents = true };
                    watcher.Changed += OnFileChanged; watcher.Created += OnFileChanged; watcher.Renamed += OnFileChanged;
                    _watchers.Add(watcher);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_watchGate)
        {
            if (_disposed) return;
            _debounce?.Dispose();
            _debounce = new Timer(_ => Changed?.Invoke(), null, 2000, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (_watchGate)
        {
            _disposed = true;
            _debounce?.Dispose(); _debounce = null;
            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();
        }
    }
}
