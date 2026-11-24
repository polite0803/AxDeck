using System.Net.Http;
using System.Text.Json;

namespace AxDeck.Core;

// 映射表（apps.json）里每个通道的最新版本查询。
// 这里只负责「问一次、读出版本号字符串」，版本比较交给 SoftwareVersion，是否可更新交给 SoftwareUpdates。
// 取不到就返回 null：限流、超时、字段缺失、响应不是预期的 JSON，一律如实当作「这次没问到」，
// 绝不退回一个猜出来的版本号——面板据此显示「未检查到」，而不是假的「已是最新」。
public sealed class SoftwareChannelFeed : IDisposable
{
    // GitHub 未认证调用上限 60 次/小时，一条通道一次检查只问一次，够用。
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // GitHub 要求带 User-Agent，否则直接 403。
    private const string UserAgent = "AxDeck";

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public SoftwareChannelFeed(HttpClient? http = null)
    {
        // 出站请求保留系统代理（企业网络里 GitHub 常常只能经代理访问）；只有访问本机服务才关代理。
        _http = http ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = Timeout };
        _ownsHttp = http is null;
    }

    // 一条通道的最新版本号；任何失败都是 null。
    public async Task<string?> LatestAsync(SoftwareChannel channel, CancellationToken token = default)
    {
        try
        {
            var (url, accept) = channel.Kind switch
            {
                SoftwareChannelKind.GitHubRelease when channel.Repo is { Length: > 0 } repo =>
                    ($"https://api.github.com/repos/{repo.Trim().Trim('/')}/releases/latest", "application/vnd.github+json"),
                SoftwareChannelKind.UrlJson when channel.Url is { Length: > 0 } page =>
                    (page, "application/json"),
                _ => (null, null),
            };
            if (url is null) return null;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (accept is not null) request.Headers.TryAddWithoutValidation("Accept", accept);

            using var response = await _http.SendAsync(request, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            return channel.Kind == SoftwareChannelKind.GitHubRelease
                ? VersionFromGitHubRelease(body)
                : VersionFromJson(body, channel.VersionField);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or UriFormatException)
        {
            // 网络不通、超时、URL 写错：这次没问到，等下个周期再试。
            return null;
        }
    }

    // releases/latest 的 tag_name，去掉前导的 v（v1.15.1 → 1.15.1）。
    public static string? VersionFromGitHubRelease(string? body) => VersionFromJson(body, "tag_name") is { } tag ? TrimLeadingVee(tag) : null;

    // 响应 JSON 顶层的字符串字段；field 为空时整段正文当版本号（个别接口返回纯文本）。
    // 字段存在但不是字符串（数字、null、对象）也算没问到——那些形态的语义各不相同，不猜。
    public static string? VersionFromJson(string? body, string? field)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        if (string.IsNullOrWhiteSpace(field))
        {
            var text = body.Trim();
            return text.Length is > 0 and <= 64 && !text.StartsWith('{') && !text.StartsWith('[') ? text : null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (!document.RootElement.TryGetProperty(field.Trim(), out var value)) return null;
            if (value.ValueKind != JsonValueKind.String) return null;
            var version = value.GetString()?.Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (JsonException) { return null; }
    }

    private static string TrimLeadingVee(string version) =>
        version.Length > 1 && (version[0] is 'v' or 'V') && char.IsAsciiDigit(version[1]) ? version[1..] : version;

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}
