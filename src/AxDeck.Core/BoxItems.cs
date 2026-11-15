namespace AxDeck.Core;

// 分组里的一项，用一个字符串表示：真的文件是它的路径，系统项是 "shell::" 加它的编号。
// 真的文件和系统项记在同一张顺序表里，所以它们在分组里可以交错着排列。
// 移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），那里也是一张表。
public static class BoxItemKey
{
    // 系统项的编号在这串前缀后面。
    public const string ShellPrefix = "shell::";

    // 这一项是不是系统项。
    public static bool IsShell(string? key) => key is not null && key.StartsWith(ShellPrefix, StringComparison.Ordinal);

    // 系统项的编号；不是系统项时是空串。
    public static string ShellId(string key) => IsShell(key) ? key[ShellPrefix.Length..] : "";

    // 系统项的键。
    public static string ForShell(string shellId) => ShellPrefix + shellId;
}
