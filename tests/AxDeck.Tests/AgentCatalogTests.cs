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
        check("Products without a quota reader show credits, products with one keep its source",
            AgentCatalog.Find("workbuddy") is { UsesCredits: true } && AgentCatalog.Find("trae")!.UsesCredits
            && AgentCatalog.Find("copilot")!.UsesCredits && AgentCatalog.Find("claude")!.QuotaSource == ProviderId.Claude
            && AgentCatalog.Find("kimi")!.QuotaSource == ProviderId.Kimi && AgentCatalog.Find("glm")!.QuotaSource == ProviderId.GlmChina);

        var column = AgentCatalog.Create(AgentCatalog.Find("workbuddy")!);
        check("Adding a preset creates an enabled, named, colored column",
            column is { Enabled: true, AgentKey: "workbuddy", QuotaSource: ProviderId.Credits }
            && column.Name == "WorkBuddy" && column.ThemeColor == "#6C8CFF");
        check("A credits column still picks a valid statistics app", ProviderCatalog.IsUsageSource(column.UsageSource));
        check("Credits is a manual quota source with a stable key and manual theme",
            ProviderCatalog.Sources.Contains(ProviderId.Credits) && !ProviderCatalog.IsBuiltIn(ProviderId.Credits)
            && !ProviderCatalog.NeedsSite(ProviderId.Credits) && ProviderCatalog.Key(ProviderId.Credits) == "credits"
            && ProviderCatalog.Group(ProviderId.Credits) == "手动填写"
            && AppPresets.ThemeColor(ProviderId.Credits) == AppPresets.ManualColor
            && PanelThemes.AppKey(ProviderId.Credits) == "manual" && (int)ProviderId.Credits == 21);

        var settings = new DeckSettings { Apps = [column] };
        settings.Normalize();
        check("Credits columns survive settings normalization and round-trip with the stable key",
            settings.Apps.Single().QuotaSource == ProviderId.Credits && JsonSerializer.Serialize(settings).Contains("\"credits\"")
            && JsonSerializer.Deserialize<DeckSettings>(JsonSerializer.Serialize(settings))!.Apps.Single().QuotaSource == ProviderId.Credits);
        var bad = new AppEntry { QuotaSource = ProviderId.Credits, CreditBalance = -5, CreditTotal = 12.345m, CreditUnit = " 积分 " };
        bad.Normalize();
        check("Credits amounts reject negatives and keep two decimals", bad.CreditBalance is null && bad.CreditTotal == 12.35m && bad.CreditUnit == "积分");

        var empty = new AppEntry { QuotaSource = ProviderId.Credits, DisplayName = "Trae" };
        var waiting = DashboardUsage.Quota(empty, []);
        check("A credits column without a balance explains what to enter",
            waiting.Metrics.Count == 0 && waiting.Status.Contains("填写") && waiting.Source == "Trae");
        var filled = new AppEntry { QuotaSource = ProviderId.Credits, DisplayName = "Qoder", CreditBalance = 850, CreditTotal = 1000, CreditUnit = "积分" };
        var shown = DashboardUsage.Quota(filled, []);
        check("A credits column shows the remaining balance and a used share",
            shown.LiveQuota && shown.Metrics.Single() is { Id: "credits", Amount: 850, Total: 1000, UsedPercent: 15 }
            && shown.Metrics[0].ValueText.Contains("850"));
        check("The library maximum allows more columns than the original seven", AppPresets.MaximumApps == 12);

        var handler = new CountingHandler();
        using var http = new HttpClient(handler);
        using var providers = new ConfiguredProviders(null, http);
        var snapshots = await providers.RefreshAsync([filled], default);
        check("Credits columns issue no HTTP requests", snapshots.Count == 0 && handler.Calls == 0);
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
