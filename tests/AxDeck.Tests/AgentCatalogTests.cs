using System.Net;
using System.Text.Json;
using AxDeck.Core;

internal static class AgentCatalogTests
{
    public static async Task Run(Action<string, bool> check)
    {
        var domestic = AgentCatalog.In(AgentRegion.Domestic);
        var international = AgentCatalog.In(AgentRegion.International);
        check("The library covers domestic and international agents", domestic.Count >= 15 && international.Count >= 12
            && domestic.Count + international.Count == AgentCatalog.Presets.Count);
        check("WorkBuddy, Trae and Qoder are domestic presets",
            domestic.Any(preset => preset.Key == "workbuddy") && domestic.Any(preset => preset.Key == "trae") && domestic.Any(preset => preset.Key == "qoder"));
        check("Other domestic IDEs and CLI agents are listed",
            new[] { "lingma", "comate", "codebuddy", "codegeex", "marscode", "qwen-code", "qoder", "fitten" }.All(key => AgentCatalog.Find(key) is { Region: AgentRegion.Domestic }));
        check("International mainstream agents are listed",
            new[] { "claude", "codex", "cursor", "copilot", "gemini-cli", "windsurf", "cline", "antigravity" }.All(key => AgentCatalog.Find(key) is { Region: AgentRegion.International }));
        check("Every preset has a unique key, a name, a note and a valid color",
            AgentCatalog.Presets.Select(preset => preset.Key).Distinct().Count() == AgentCatalog.Presets.Count
            && AgentCatalog.Presets.All(preset => preset.Key.Length > 0 && preset.Name.Length > 0 && preset.Note.Length > 0 && AppPresets.IsColor(preset.Color)));
        check("Products without a quota reader use the automatic local-app source, products with one keep its source",
            AgentCatalog.Find("workbuddy") is { UsesLocalApp: true } && AgentCatalog.Find("trae")!.UsesLocalApp
            && AgentCatalog.Find("copilot")!.UsesLocalApp && AgentCatalog.Find("claude")!.QuotaSource == ProviderId.Claude
            && AgentCatalog.Find("kimi")!.QuotaSource == ProviderId.Kimi && AgentCatalog.Find("glm")!.QuotaSource == ProviderId.GlmChina);

        var column = AgentCatalog.Create(AgentCatalog.Find("workbuddy")!);
        check("Adding a preset creates an enabled, named, colored column",
            column is { Enabled: true, AgentKey: "workbuddy", QuotaSource: ProviderId.LocalApp }
            && column.Name == "WorkBuddy" && column.ThemeColor == "#6C8CFF");
        check("A local-app column still picks a valid statistics app", ProviderCatalog.IsUsageSource(column.UsageSource));
        check("LocalApp is an automatic source with a stable key and the manual theme",
            ProviderCatalog.Sources.Contains(ProviderId.LocalApp) && !ProviderCatalog.IsBuiltIn(ProviderId.LocalApp)
            && ProviderCatalog.IsLocalApp(ProviderId.LocalApp) && !ProviderCatalog.NeedsSite(ProviderId.LocalApp)
            && ProviderCatalog.Key(ProviderId.LocalApp) == "local-app" && ProviderCatalog.Group(ProviderId.LocalApp) == "自动读取"
            && AppPresets.ThemeColor(ProviderId.LocalApp) == AppPresets.ManualColor
            && PanelThemes.AppKey(ProviderId.LocalApp) == "manual" && (int)ProviderId.LocalApp == 21);

        var settings = new DeckSettings { Apps = [column] };
        settings.Normalize();
        check("Local-app columns survive settings normalization and round-trip with the stable key",
            settings.Apps.Single().QuotaSource == ProviderId.LocalApp && JsonSerializer.Serialize(settings).Contains("\"local-app\"")
            && JsonSerializer.Deserialize<DeckSettings>(JsonSerializer.Serialize(settings))!.Apps.Single().QuotaSource == ProviderId.LocalApp);
        check("The first agent-library version's credits key still reads as the local-app source",
            JsonSerializer.Deserialize<DeckSettings>("""{"Apps":[{"QuotaSource":"credits","DisplayName":"Qoder"}]}""")!.Apps.Single().QuotaSource == ProviderId.LocalApp);
        var bad = new AppEntry { QuotaSource = ProviderId.LocalApp, LocalDataPath = "  C:\\data\\quota.json  " };
        bad.Normalize();
        var broken = new AppEntry { LocalDataPath = "C:\\data\\q.json\nmore" };
        broken.Normalize();
        check("A local data path is trimmed, and a path with a line break is dropped",
            bad.LocalDataPath == "C:\\data\\quota.json" && broken.LocalDataPath == "");

        var empty = new AppEntry { QuotaSource = ProviderId.LocalApp, DisplayName = "Trae", AgentKey = "trae" };
        var waiting = DashboardUsage.Quota(empty, []);
        check("Before the first automatic read a local-app column waits instead of inventing a balance",
            waiting.Metrics.Count == 0 && waiting.Status.Contains("等待更新") && !waiting.LiveQuota);
        check("The library maximum allows more columns than the original seven", AppPresets.MaximumApps == 12);

        // These rows are read from the application's own files by LocalAgentUsage, so the HTTP script path must skip them.
        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var providers = new ConfiguredProviders(null, http);
        var snapshots = await providers.RefreshAsync([empty], default);
        check("Local-app columns make no HTTP requests", snapshots.Count == 0 && handler.Calls == 0);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }
    }
}
