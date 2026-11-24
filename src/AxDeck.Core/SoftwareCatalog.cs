using System.Text.Json;

namespace AxDeck.Core;

// 映射表支持的版本查询通道。只固定已经实测可用的两类，未知类型整条跳过，绝不猜。
public enum SoftwareChannelKind { GitHubRelease, UrlJson }

// 通道参数：GitHub 用 Repo（owner/name）取 releases/latest 的 tag_name；
// JSON 接口用 Url 取响应顶层的 VersionField（缺省则把整段正文当版本号）。
public sealed record SoftwareChannel(SoftwareChannelKind Kind, string? Repo, string? Url, string? VersionField);

// 映射表里的一条规则：把一个已安装应用接到一个版本通道上。
// Exe / Arp / Winget 都是匹配条件，可单用可组合；三个都没有的条目在加载时跳过（它匹配不到任何应用）。
// Page 是手工下载页，用于无法静默安装的项。
public sealed record SoftwareSource(
    string Name,
    string? Exe,
    string? Arp,
    string? Winget,
    SoftwareChannel Channel,
    string? Page);

// 内置映射表（apps.json 嵌入资源）的读取与匹配。
// 它是数据不是枚举：新增一个应用只要加一条 JSON，不改任何设置标识、不动任何 switch。
public static class SoftwareCatalog
{
    public const string ResourceName = "AxDeck.Core.apps.json";

    private static readonly IReadOnlyList<SoftwareSource> Embedded = ReadEmbedded();

    // 内置条目。读不到或解析失败时是空表——面板据此只显示 WinGet 的结果，而不是崩掉。
    public static IReadOnlyList<SoftwareSource> Sources => Embedded;

    public static IReadOnlyList<SoftwareSource> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return [];

            var sources = new List<SoftwareSource>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                if (Text(item, "name") is not { Length: > 0 } name) continue;
                if (Object(item, "channel") is not { } channelElement) continue;
                if (Channel(channelElement) is not { } channel) continue;

                var match = Object(item, "match");
                var source = new SoftwareSource(
                    name,
                    Text(match, "exe"),
                    Text(match, "arp"),
                    Text(match, "winget"),
                    channel,
                    Text(item, "page"));
                if (source.Exe is null && source.Arp is null && source.Winget is null) continue;
                sources.Add(source);
            }
            return sources;
        }
        catch (JsonException) { return []; }
    }

    // 匹配优先级：WinGet ID 精确 → exe 文件名精确 → ARP 显示名子串。全部忽略大小写。
    public static SoftwareSource? Match(IReadOnlyList<SoftwareSource> sources, string? exe, string? displayName, string? wingetId = null)
    {
        var file = FileName(exe);
        var id = wingetId?.Trim();
        var name = displayName?.Trim();

        if (!string.IsNullOrEmpty(id))
        {
            foreach (var source in sources)
                if (source.Winget is { } wanted && string.Equals(wanted.Trim(), id, StringComparison.OrdinalIgnoreCase))
                    return source;
        }
        if (file is { Length: > 0 })
        {
            foreach (var source in sources)
                if (source.Exe is { } wanted && string.Equals(wanted.Trim(), file, StringComparison.OrdinalIgnoreCase))
                    return source;
        }
        if (!string.IsNullOrEmpty(name))
        {
            foreach (var source in sources)
                if (source.Arp is { Length: > 0 } wanted && name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    return source;
        }
        return null;
    }

    // 只留文件名并小写：注册表与快捷方式给的都是完整路径，个别还带引号或 ",0" 后缀。
    public static string? FileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var text = path.Trim().Trim('"');
        var cut = text.LastIndexOfAny(['\\', '/']);
        return (cut >= 0 ? text[(cut + 1)..] : text).ToLowerInvariant();
    }

    private static SoftwareChannel? Channel(JsonElement element) => Text(element, "type") switch
    {
        "github-release" => Text(element, "repo") is { Length: > 0 } repo
            ? new SoftwareChannel(SoftwareChannelKind.GitHubRelease, repo, null, null)
            : null,
        "url-json" => Text(element, "url") is { Length: > 0 } url
            ? new SoftwareChannel(SoftwareChannelKind.UrlJson, null, url, Text(element, "versionField"))
            : null,
        _ => null,
    };

    private static JsonElement? Object(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    // 只认字符串；数字、null、缺字段一律当作「没写」。未知字段自然被忽略。
    private static string? Text(JsonElement? element, string property)
    {
        if (element is not { } parent || !parent.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
    }

    private static IReadOnlyList<SoftwareSource> ReadEmbedded()
    {
        try
        {
            using var stream = typeof(SoftwareCatalog).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null) return [];
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
