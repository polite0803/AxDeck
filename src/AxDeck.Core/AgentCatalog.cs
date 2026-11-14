namespace AxDeck.Core;

public enum AgentRegion { Domestic, International }

// One product that can be shown as a column of the AI usage page. Products with a reader already built into AxDeck keep
// its quota source; the rest are added with ProviderId.LocalApp, which rereads the quota record the application keeps
// on this PC. When no such record can be read, the column says so instead of showing a number typed by hand.
public sealed record AgentPreset(
    string Key, string NameZh, string NameEn, AgentRegion Region, string Color, ProviderId QuotaSource, string NoteZh, string NoteEn)
{
    public string Name => Loc.IsEnglish ? NameEn : NameZh;
    public string Note => Loc.IsEnglish ? NoteEn : NoteZh;
    public bool UsesLocalApp => QuotaSource == ProviderId.LocalApp;
}

// The library behind "Add app" in Settings > Quotas and usage. It is data, not an enum: adding a product does not
// change any setting identifier, and every column stays editable (name, color, sources, order, enabled).
public static class AgentCatalog
{
    public static readonly IReadOnlyList<AgentPreset> Presets =
    [
        // Domestic products. Most have no public quota API, so AxDeck reads the record the local application writes.
        new("workbuddy", "WorkBuddy", "WorkBuddy", AgentRegion.Domestic, "#6C8CFF", ProviderId.LocalApp, "腾讯 AI 工作台", "Tencent AI workbench"),
        new("trae", "Trae", "Trae", AgentRegion.Domestic, "#7B6CF6", ProviderId.LocalApp, "字节跳动 AI IDE", "ByteDance AI IDE"),
        new("qoder", "Qoder", "Qoder", AgentRegion.Domestic, "#4FB3A5", ProviderId.LocalApp, "阿里 AI 编程平台", "Alibaba AI coding platform"),
        new("lingma", "通义灵码", "Tongyi Lingma", AgentRegion.Domestic, "#FF8A4C", ProviderId.LocalApp, "阿里云 AI 编码助手", "Alibaba Cloud coding assistant"),
        new("qwen-code", "Qwen Code", "Qwen Code", AgentRegion.Domestic, "#E0689A", ProviderId.LocalApp, "通义千问命令行智能体", "Qwen command-line agent"),
        new("comate", "文心快码", "Comate", AgentRegion.Domestic, "#4C8DFF", ProviderId.LocalApp, "百度 AI 编码助手", "Baidu coding assistant"),
        new("codebuddy", "CodeBuddy", "CodeBuddy", AgentRegion.Domestic, "#3EAD7A", ProviderId.LocalApp, "腾讯云 AI 编码助手", "Tencent Cloud coding assistant"),
        new("codegeex", "CodeGeeX", "CodeGeeX", AgentRegion.Domestic, "#8E7BF0", ProviderId.LocalApp, "智谱开源编码助手", "Zhipu open-source coding assistant"),
        new("marscode", "豆包 MarsCode", "MarsCode", AgentRegion.Domestic, "#F0876A", ProviderId.LocalApp, "字节跳动 AI 编程助手", "ByteDance coding assistant"),
        new("fitten", "Fitten Code", "Fitten Code", AgentRegion.Domestic, "#5BA8D8", ProviderId.LocalApp, "非十科技编码助手", "Fitten coding assistant"),
        new("minimax", "MiniMax", "MiniMax", AgentRegion.Domestic, "#D06BA8", ProviderId.LocalApp, "MiniMax 模型与智能体平台", "MiniMax model and agent platform"),
        new("siliconflow", "硅基流动", "SiliconFlow", AgentRegion.Domestic, "#7A9BF0", ProviderId.LocalApp, "模型服务平台", "Model serving platform"),
        new("bailian", "阿里云百炼", "Alibaba Bailian", AgentRegion.Domestic, "#E08A4C", ProviderId.LocalApp, "阿里云模型服务平台", "Alibaba Cloud model platform"),
        new("volcengine", "火山方舟", "Volcengine Ark", AgentRegion.Domestic, "#6BB0D8", ProviderId.LocalApp, "字节模型服务平台", "ByteDance model platform"),
        new("kimi", "Kimi Code", "Kimi Code", AgentRegion.Domestic, "#68CDEB", ProviderId.Kimi, "本机登录 Kimi Code 后自动读取", "Read automatically after signing in to Kimi Code"),
        new("glm", "智谱 GLM", "Zhipu GLM", AgentRegion.Domestic, "#CC9EED", ProviderId.GlmChina, "填写 GLM API 密钥", "Enter a GLM API key"),
        new("deepseek", "DeepSeek", "DeepSeek", AgentRegion.Domestic, "#6081DA", ProviderId.DeepSeek, "填写 DeepSeek API 密钥", "Enter a DeepSeek API key"),
        new("zcode", "ZCode", "ZCode", AgentRegion.Domestic, "#3A538D", ProviderId.ZCode, "本机登录 ZCode 后自动读取", "Read automatically after signing in to ZCode"),

        // International products with a reader in AxDeck, then the ones read from their local application data.
        new("claude", "Claude Code", "Claude Code", AgentRegion.International, "#E5A385", ProviderId.Claude, "本机登录后自动读取额度与用量", "Quota and usage are read after signing in on this PC"),
        new("codex", "Codex", "Codex", AgentRegion.International, "#98DBB0", ProviderId.Codex, "本机登录后自动读取额度与用量", "Quota and usage are read after signing in on this PC"),
        new("antigravity", "Antigravity", "Antigravity", AgentRegion.International, "#9CB9FF", ProviderId.Antigravity, "读取额度需要桌面版正在运行", "The desktop app must be running for its quota"),
        new("cursor", "Cursor", "Cursor", AgentRegion.International, "#D4D5DF", ProviderId.Cursor, "本机登录后自动读取账户用量", "Account usage is read after signing in on this PC"),
        new("copilot", "GitHub Copilot", "GitHub Copilot", AgentRegion.International, "#8E8E93", ProviderId.LocalApp, "GitHub / Microsoft 编码助手", "GitHub / Microsoft coding assistant"),
        new("gemini-cli", "Gemini CLI", "Gemini CLI", AgentRegion.International, "#9CB9FF", ProviderId.LocalApp, "Google 命令行智能体", "Google command-line agent"),
        new("windsurf", "Windsurf", "Windsurf", AgentRegion.International, "#7FD1C4", ProviderId.LocalApp, "AI 原生 IDE", "AI-native IDE"),
        new("cline", "Cline", "Cline", AgentRegion.International, "#B9A8FF", ProviderId.LocalApp, "VS Code 智能体扩展", "VS Code agent extension"),
        new("roo-code", "Roo Code", "Roo Code", AgentRegion.International, "#E7A0D0", ProviderId.LocalApp, "VS Code 智能体扩展", "VS Code agent extension"),
        new("aider", "Aider", "Aider", AgentRegion.International, "#98DBB0", ProviderId.LocalApp, "开源终端结对编程", "Open-source terminal pair programming"),
        new("kiro", "Kiro / Amazon Q", "Kiro / Amazon Q", AgentRegion.International, "#F0B37E", ProviderId.LocalApp, "AWS 智能体 IDE 与 CLI", "AWS agent IDE and CLI"),
        new("augment", "Augment Code", "Augment Code", AgentRegion.International, "#E5C890", ProviderId.LocalApp, "企业级编码智能体", "Enterprise coding agent"),
        new("tabnine", "Tabnine", "Tabnine", AgentRegion.International, "#D4D5DF", ProviderId.LocalApp, "支持私有化部署的编码助手", "Coding assistant with private deployment"),
        new("grok-build", "Grok Build", "Grok Build", AgentRegion.International, "#E78284", ProviderId.LocalApp, "xAI 命令行智能体", "xAI command-line agent"),
        new("opencode", "OpenCode", "OpenCode", AgentRegion.International, "#8E8E93", ProviderId.OpenCode, "登录 OpenCode Console 后读取 Go 额度", "Go allowance after signing in to OpenCode Console"),
        new("openrouter", "OpenRouter", "OpenRouter", AgentRegion.International, "#CC9EED", ProviderId.OpenRouter, "填写 OpenRouter API 密钥", "Enter an OpenRouter API key"),
    ];

    public static IReadOnlyList<AgentPreset> In(AgentRegion region) => [.. Presets.Where(preset => preset.Region == region)];

    public static AgentPreset? Find(string key) => Presets.FirstOrDefault(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase));

    // A new column starts with the product's name and color. Its sources, name, color and order can all be changed later.
    public static AppEntry Create(AgentPreset preset) => new()
    {
        AgentKey = preset.Key,
        DisplayName = preset.Name,
        ThemeColor = preset.Color,
        QuotaSource = preset.QuotaSource,
        UsageSource = ProviderCatalog.IsUsageSource(preset.QuotaSource) ? preset.QuotaSource : ProviderId.Claude,
        Enabled = true
    };
}
