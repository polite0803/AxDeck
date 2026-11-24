namespace AxDeck.Core;

// WinGet 报出的版本号不止 x.y.z：llama.cpp 是 b10107，PostgreSQL 是 18.4-2，PowerShell 是 7.6.10.0，OpenJDK 是
// 17.0.16+8。ApplicationVersion.Parse 只认点分三段，这些在它眼里全落到 0.0.0，所以「有没有新版」用这一套比：
// 按非数字切成数字段逐段比，缺段补 0，1.15.1 与 1.15.1.0 相等。
public readonly struct SoftwareVersion : IComparable<SoftwareVersion>
{
    private readonly long[]? _parts;

    private SoftwareVersion(string text, long[] parts)
    {
        Text = text;
        _parts = parts;
    }

    // 原样保留，界面显示用。不要在显示时重排或补零。
    public string Text { get; }

    // 切不出数字的版本（空串、"< 未知 >"）：不参与比较，界面如实显示「版本未知」，绝不当作 0.0.0。
    public bool Known => _parts is { Length: > 0 };

    public static SoftwareVersion Parse(string? text)
    {
        var raw = (text ?? "").Trim();
        if (raw.Length == 0) return new SoftwareVersion(raw, []);

        var parts = new List<long>(4);
        for (var index = 0; index < raw.Length && parts.Count < 8;)
        {
            if (!char.IsAsciiDigit(raw[index])) { index++; continue; }
            var start = index;
            while (index < raw.Length && char.IsAsciiDigit(raw[index])) index++;
            // 超长数字段现实里不会出现；按 long.MaxValue 处理，免得把一个已知版本误判成未知。
            parts.Add(long.TryParse(raw.AsSpan(start, index - start), out var value) ? value : long.MaxValue);
        }
        return new SoftwareVersion(raw, [.. parts]);
    }

    public int CompareTo(SoftwareVersion other)
    {
        var length = Math.Max(_parts?.Length ?? 0, other._parts?.Length ?? 0);
        for (var i = 0; i < length; i++)
        {
            var left = _parts is not null && i < _parts.Length ? _parts[i] : 0;
            var right = other._parts is not null && i < other._parts.Length ? other._parts[i] : 0;
            if (left != right) return left.CompareTo(right);
        }
        return 0;
    }

    // available 比 current 新才算有更新。任一方版本未知就无法判断，按「不更新」处理。
    public static bool IsNewer(string? available, string? current)
    {
        var target = Parse(available);
        var source = Parse(current);
        return target.Known && source.Known && target.CompareTo(source) > 0;
    }

    public override string ToString() => Text ?? "";
}
