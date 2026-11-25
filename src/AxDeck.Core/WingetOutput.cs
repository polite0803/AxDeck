namespace AxDeck.Core;

// winget 自己的说法，两条判断都是纯文本规则：升级失败时哪一句该给用户看，以及「按包 ID 找不到已安装项」
// 要不要换一种方式再试。放在 Core 便于测试；WingetCli 只负责启动进程、把输出交过来。
public static class WingetOutput
{
    // winget 用「--id」精确匹配不到已安装记录时回这个退出码（0x8A150014）。用 MSI 装的包（PowerShell 7 这类）
    // 注册表里只有 DisplayName、没挂 winget 包 ID，就属于这种情况，换用表格第一列的名称还能命中。
    private const int NoMatchingApp = unchecked((int)0x8A150014);

    // 升级失败时给用户的那一句。winget 先打印「已找到 …」「正在下载 …」，原因在最后，而且可能在 stdout 也可能
    // 在 stderr：只取第一行会把过程信息当成失败原因。优先两路流里的失败行，都没有才退到 stderr 末行、stdout 末行。
    public static string? Failure(string stdout, string stderr, bool timedOut)
    {
        if (timedOut) return Loc.T("升级超时，已中止。", "The upgrade timed out and was stopped.");
        var errors = Lines(stderr);
        var output = Lines(stdout);
        return First(errors, SoundsLikeFailure) ?? First(output, SoundsLikeFailure)
            ?? errors.LastOrDefault() ?? output.LastOrDefault();
    }

    // 这次失败是不是「按包 ID 精确匹配不到已安装项」。退出码优先，文本兜底（有本地化差异）。
    public static bool Unmatched(int exitCode, string? text) =>
        exitCode == NoMatchingApp || UnmatchedText(text);

    // 中英文都认：upgrade 与 list 的措辞略不同（「与输入条件匹配」/「与输入标准匹配」）。
    public static bool UnmatchedText(string? text) =>
        text is not null &&
        (text.Contains("No app found matching", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("找不到与输入条件匹配", StringComparison.Ordinal) ||
         text.Contains("找不到与输入标准匹配", StringComparison.Ordinal));

    private static string[] Lines(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? []
            : [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string? First(IReadOnlyList<string> lines, Func<string, bool> match)
    {
        foreach (var line in lines) if (match(line)) return line;
        return null;
    }

    // winget 的失败措辞：英文 failed / error、中文「失败」「错误」；另有 HRESULT 与「was not …」这类否定句。
    // 进度行（已找到 / 正在下载 / 已成功验证）不含这些词，不会被误认。
    private static bool SoundsLikeFailure(string line) =>
        line.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("was not", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("no app found", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("失败", StringComparison.Ordinal) ||
        line.Contains("错误", StringComparison.Ordinal) ||
        line.Contains("找不到", StringComparison.Ordinal) ||
        line.Contains("0x8a15", StringComparison.OrdinalIgnoreCase);
}
