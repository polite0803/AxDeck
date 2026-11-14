using System.Text.Json;
using AxDeck.Core;

internal static class LocalAgentUsageTests
{
    // An application's own quota record, encrypted with its own safeStorage key. The fake desktop plays the part of
    // DesktopSources: it returns the row from state.vscdb and the text the key decrypts to.
    private const string QoderRow = "{\"type\":\"Buffer\",\"data\":[118,49,48,240,19]}";
    private const string QoderQuota = """
        {"userId":"u-1","isQuotaExceeded":true,"userQuota":{"total":300,"used":300,"remaining":0,"percentage":1,"unit":"credits"}}
        """;

    public static void Run(Action<string, bool> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "axdeck-local-" + Guid.NewGuid().ToString("N"));
        var home = Path.Combine(root, "home");
        var roaming = Path.Combine(root, "roaming");
        var local = Path.Combine(root, "local");
        Directory.CreateDirectory(home); Directory.CreateDirectory(roaming); Directory.CreateDirectory(local);
        var paths = new LocalAgentPaths(home, roaming, local, Watch: false);
        DateTimeOffset Clock() => new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(8));
        var database = Path.Combine(roaming, "Qoder", "User", "globalStorage", "state.vscdb");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        File.WriteAllText(database, "");
        File.WriteAllText(Path.Combine(roaming, "Qoder", "Local State"), "{}");

        var desktop = new FakeDesktop { Rows = { [database] = [new string?[] { "secret://aicoding.auth.creditUsage", QoderRow }] }, Plain = QoderQuota };
        using var reader = new LocalAgentUsage(desktop, paths, Clock);
        var entry = new AppEntry { QuotaSource = ProviderId.LocalApp, AgentKey = "qoder", DisplayName = "Qoder" };
        entry.Normalize();

        var first = reader.Read([entry]).Single();
        check("A local application's encrypted quota record is read automatically",
            first.LiveQuota && first.StatusLabel == "本地数据" && first.Metrics.Count == 1
            && first.Metrics[0] is { Id: "local-credits", Amount: 0, Total: 300, Kind: MetricKind.Balance }
            && first.Metrics[0].UsedPercent == 100 && desktop.DecryptCalls == 1);
        check("The reading names its source and the fields it used, and reports the exhausted allowance",
            first.Source.Contains("Qoder") && first.Details.ContainsKey("读取字段") && first.Status.Contains("已用尽"));
        check("DashboardUsage picks the local snapshot by the row's configuration key",
            DashboardUsage.Quota(entry, [first]).Metrics.Single().Total == 300);

        // The next refresh reads the file again. Nothing is stored in settings and nothing needs to be typed in.
        desktop.Plain = """{"userQuota":{"total":300,"used":180,"remaining":120,"unit":"credits"}}""";
        var second = reader.Read([entry]).Single();
        check("Each refresh rereads the application's data instead of using a value entered by hand",
            second.LiveQuota && second.Metrics.Single().Amount == 120 && second.Metrics.Single().UsedPercent == 60
            && desktop.QueryCalls >= 2);

        // A database with only unrelated records must not turn a token or an activity log into a quota.
        desktop.Rows[database] = [new string?[] { "github-user-usages", "[{\"lastUsed\":1793888430983}]" }];
        desktop.Plain = null;
        var unrelated = reader.Read([entry]).Single();
        check("Local data without a quota field is reported honestly, with no invented number",
            !unrelated.LiveQuota && unrelated.Metrics.Count == 0 && unrelated.StatusLabel == "本地无额度记录"
            && unrelated.Status.Contains("不会显示估算值"));

        // A product that is not installed on this PC has no data file at all.
        var missing = new AppEntry { QuotaSource = ProviderId.LocalApp, AgentKey = "not-installed", DisplayName = "Not Installed" };
        missing.Normalize();
        var absent = reader.Read([missing]).Single();
        check("An application with no local data is shown as not detected",
            !absent.LiveQuota && absent.Metrics.Count == 0 && absent.StatusLabel == "未检测到本地数据"
            && absent.Status.Contains("不会显示估算值"));

        // A user may point a column at the file the application writes; it is still reread automatically.
        var json = Path.Combine(root, "quota.json");
        File.WriteAllText(json, """{"quota":{"total":100,"used":25,"remaining":75,"unit":"积分"}}""");
        var overridden = new AppEntry { QuotaSource = ProviderId.LocalApp, AgentKey = "qoder", DisplayName = "Qoder", LocalDataPath = json };
        overridden.Normalize();
        var fromFile = reader.Read([overridden]).Single();
        check("An explicit local data file is parsed and drawn with its used share",
            fromFile.LiveQuota && fromFile.Metrics.Single() is { Amount: 75, Total: 100 }
            && fromFile.Metrics.Single().UsedPercent == 25);

        File.SetLastWriteTimeUtc(json, DateTime.UtcNow.AddDays(-10));
        var stale = reader.Read([overridden]).Single();
        check("An old local record is still shown, but marked as stale rather than as a live balance",
            stale.LiveQuota && stale.IsStale && stale.Status.Contains("7 天"));

        check("A token or a list of activity dates is never parsed as a quota",
            !LocalAgentUsage.HasQuota("""{"token":"abc","activeDays":["2026-11-14"]}""") && LocalAgentUsage.HasQuota(QoderQuota));
        check("An encrypted Electron Buffer is recognized, a plain quota object is not",
            LocalAgentUsage.IsEncrypted(QoderRow) && !LocalAgentUsage.IsEncrypted(QoderQuota));

        // The settings "test" button reads the same files through the service; it does not start an HTTP session.
        desktop.Rows[database] = [new string?[] { "secret://aicoding.auth.creditUsage", QoderRow }];
        desktop.Plain = QoderQuota;
        using var service = new UsageService(desktop, agentPaths: paths, clock: Clock);
        var test = service.TestProviderAsync(entry).Result;
        check("Testing a local-app row reads its files through UsageService",
            test.Snapshot.LiveQuota && test.Snapshot.Metrics.Single().Total == 300
            && test.Logs.Count == 1 && test.Logs[0].Contains("state.vscdb"));

        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private sealed class FakeDesktop : IDesktopSources
    {
        public Dictionary<string, IReadOnlyList<string?[]>> Rows { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Plain { get; set; }
        public int QueryCalls { get; private set; }
        public int DecryptCalls { get; private set; }
        public string? ReadCursorToken(string database) => null;
        public Task<IReadOnlyList<LocalEndpoint>> FindAntigravityAsync(CancellationToken cancellation)
            => Task.FromResult<IReadOnlyList<LocalEndpoint>>([]);
        public IReadOnlyList<string?[]>? QueryDatabase(string database, string sql)
        {
            QueryCalls++;
            return Rows.TryGetValue(database, out var rows) ? rows : null;
        }
        public string? DecryptSecret(string localState, string value) { DecryptCalls++; return Plain; }
    }
}
