using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxDeck.Core;

// 一次更新检查的完整结果，落盘后重启仍能立刻显示上次的列表。
// CheckedAt 只记成功的时间：检查失败时保留旧时间与旧列表，下次打开页面会自然重试。
public sealed class SoftwareUpdateState
{
    public DateTimeOffset? CheckedAt { get; set; }
    public string? Failure { get; set; }
    // 这台机器没装 winget：不是失败，界面上说「未检测到 WinGet」，不要与「检查失败」混为一谈。
    public bool MissingTool { get; set; }
    public bool Complete { get; set; } = true;
    public int Installed { get; set; }
    public List<SoftwareUpdate> Updates { get; set; } = [];

    // 写出这份缓存时 winget 表格解析器的版本。缺省 0 = 1.6.1 之前那一版，切列错位。
    public int Format { get; set; }
}

// software-updates.json 的读写。写法照 InstallerUpdates / AppUpdates：临时文件 + 原子替换，读失败一律降级为空状态。
public sealed class SoftwareUpdateStore
{
    public const string FileName = "software-updates.json";

    // 缓存由第几版 winget 表格解析器写出。解析器修好一个切列错误（1.6.1 起才读第五列「源」），
    // 旧缓存里的 winget 行全是错位的：可用版本写着「winget」，包 ID 却是版本号。这种行点「更新」时
    // 拿版本号去升级，winget 找不到包，界面上看起来就是「点了没反应」。缓存可以重跑，所以旧格式的
    // winget 行一律丢掉并当作从未检查过，下次打开页面立刻重查一轮。
    public const int Format = 1;

    // 12 小时内的结果直接显示，不重新跑 winget（一次要 1～2 分钟）。
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public SoftwareUpdateStore(string dataDirectory) => _path = Path.Combine(dataDirectory, FileName);

    public string FilePath => _path;

    public SoftwareUpdateState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new SoftwareUpdateState();
            var state = JsonSerializer.Deserialize<SoftwareUpdateState>(File.ReadAllText(_path), Options);
            if (state is null) return new SoftwareUpdateState();
            // 手改坏的条目直接丢掉：缺名称的行在界面上无从显示，也没有可点的动作。
            state.Updates = [.. (state.Updates ?? []).Where(update => update is { Name: { Length: > 0 } })];
            // 旧解析器写下的 winget 行已经不可用（见 Format 的说明）。映射表来源的行没受影响，留着。
            if (state.Format < Format)
            {
                state.Updates.RemoveAll(update => update.Origin == SoftwareUpdateOrigin.Winget);
                state.CheckedAt = null;
                state.Format = Format;
            }
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return new SoftwareUpdateState();
        }
    }

    public bool Save(SoftwareUpdateState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = _path + ".tmp";
            // 记下写出它的解析器版本：下次解析器再改切列规则时，旧行靠这个字段自行作废。
            state.Format = Format;
            File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
            File.Move(temp, _path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    // 上次成功检查距今不到 12 小时。时钟被回拨时 CheckedAt 落在未来，同样算新鲜，避免反复重扫。
    public static bool IsFresh(SoftwareUpdateState state, DateTimeOffset now) =>
        state.CheckedAt is { } checkedAt && now - checkedAt < FreshFor;

    // 是否已经成功检查过至少一次（与「刚查过」不同，用来区分「首屏还没数据」和「数据过期了」）。
    public static bool HasResult(SoftwareUpdateState state) => state.CheckedAt is not null;
}
