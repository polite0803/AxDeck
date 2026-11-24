using System.Text.RegularExpressions;

namespace AxDeck.Core;

// winget upgrade 表格里的一条可升级记录。四个字段都是 winget 原文，不加工。
public sealed record WingetEntry(string Name, string Id, string Version, string Available);

// winget upgrade 的解析结果。
// Complete 为 false 表示输出没能完整读懂（某行切不出四段、条数与末行计数不符）：界面据此提示结果可能缺失，
// 而不是把半份结果当成全部。
public sealed class WingetScan(
    IReadOnlyList<WingetEntry> entries,
    IReadOnlyList<string> unparsed,
    bool complete,
    string? message)
{
    public IReadOnlyList<WingetEntry> Entries { get; } = entries;

    // 读不懂的原始行，排错用；不进界面。
    public IReadOnlyList<string> Unparsed { get; } = unparsed;
    public bool Complete { get; } = complete;

    // 没有可升级项时 winget 给出的那一句话（中英文各一种）；解析彻底失败时是输出首行原文。成功且有条目时为 null。
    public string? Message { get; } = message;

    public bool Found => Entries.Count > 0;
}

// 把 `winget upgrade --include-unknown --disable-interactivity` 的文本表格读成条目。
// winget 没有机器可读输出（--output json 不存在），只有这张按空格对齐的表，且中英文表头列宽一致。
// 不要带 `--source`：upgrade 不接受它，winget 会把它当成搜索词，实测 exit=22。
public static class WingetTable
{
    // 零更新时 winget 只输出这一句白话，没有表头、没有分隔线、没有计数行。这不是解析失败。
    private static readonly string[] NoMatch =
    [
        "找不到与输入条件匹配的已安装程序包",
        "No installed package found matching input criteria",
    ];

    // 表格末行的计数：「18 upgrades available.」/「18 升级可用。」。只看最后一个非空行。
    private static readonly Regex Summary = new(@"^(\d+)\s+\S.*[.。]$", RegexOptions.CultureInvariant);

    public static WingetScan Parse(string? output)
    {
        var lines = (output ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        // 分隔线是整行由 '-' 组成的长行，长度跨语言一致，是「这确实是张表」的最稳信号。
        var separator = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length >= 20 && line.AsSpan().IndexOfAnyExcept('-') < 0) { separator = i; break; }
        }
        if (separator < 0) return NoTable(lines);

        // 分隔线的上一非空行是表头。再核一次列名，避免把 winget 的其它提示文本误当成表。
        var header = "";
        for (var i = separator - 1; i >= 0; i--)
        {
            if (lines[i].Trim() is { Length: > 0 } text) { header = text; break; }
        }
        if (!header.Contains("id", StringComparison.OrdinalIgnoreCase) ||
            !(header.Contains("name", StringComparison.OrdinalIgnoreCase) || header.Contains("名称")))
            return NoTable(lines);

        var rows = new List<string>();
        for (var i = separator + 1; i < lines.Length; i++)
        {
            var row = lines[i].TrimEnd();
            if (row.Trim().Length > 0) rows.Add(row);
        }

        int? total = null;
        if (rows.Count > 0 && Summary.Match(rows[^1]) is { Success: true } last)
        {
            total = int.Parse(last.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            rows.RemoveAt(rows.Count - 1);
        }

        var entries = new List<WingetEntry>(rows.Count);
        var unparsed = new List<string>();
        foreach (var row in rows)
        {
            if (TryParseRow(row, out var entry)) entries.Add(entry);
            else unparsed.Add(row.Trim());
        }

        var complete = unparsed.Count == 0 && (total is null || total == entries.Count);
        return new WingetScan(entries, unparsed, complete, null);
    }

    private static WingetScan NoTable(string[] lines)
    {
        foreach (var line in lines)
        {
            var text = line.Trim();
            if (text.Length == 0) continue;
            foreach (var fragment in NoMatch)
            {
                if (text.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return new WingetScan([], [], true, text);
            }
            // 既不像表、又没有「无匹配包」的白话：读取失败，把首行原文交给界面显示，不假装成功。
            return new WingetScan([], [text], false, text);
        }
        return new WingetScan([], [], false, null);
    }

    // 从右往左切：最后一段是「可用」、倒数第二段是「当前版本」，剩下的左侧再从最后一个空白处切成「ID | 名称」。
    // winget ID 不含空格，所以这条切口恒成立——哪怕名称与 ID 之间只有一个空格
    // （Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.30501 Microsoft.VCRedist.2013.x64）。
    private static bool TryParseRow(string row, out WingetEntry entry)
    {
        entry = null!;
        var end = row.Length;
        if (!Take(row, ref end, out var available)) return false;
        available = Widen(row, ref end, available);
        if (!Take(row, ref end, out var version)) return false;
        version = Widen(row, ref end, version);

        var head = row[..end].TrimEnd();
        var cut = head.LastIndexOfAny([' ', '\t']);
        if (cut <= 0) return false;

        var id = head[(cut + 1)..].Trim();
        var name = head[..cut].TrimEnd();
        if (id.Length == 0 || name.Length == 0) return false;

        entry = new WingetEntry(name, id, version, available);
        return true;
    }

    // 取 text[..end] 末尾的一段非空白，并把 end 移到它前面。
    private static bool Take(string text, ref int end, out string token)
    {
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        var finish = end;
        while (end > 0 && !char.IsWhiteSpace(text[end - 1])) end--;
        token = text[end..finish];
        return finish > end;
    }

    // 未知版本在表里写作「< 未知 >」，含空格会被切散；把尖括号里的内容还原成一个整体。
    private static string Widen(string text, ref int end, string token)
    {
        if (token.Contains('<') || !token.Contains('>')) return token;
        var parts = new List<string> { token };
        while (Take(text, ref end, out var part))
        {
            parts.Insert(0, part);
            if (part.Contains('<')) break;
        }
        return string.Join(' ', parts);
    }
}
