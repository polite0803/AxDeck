namespace AxDeck.Core;

// 桌面图标的真实位置：Windows 自己把桌面上的图标摆在资源管理器的图标列表里，那个位置就是「手动排好的顺序」的唯一真相。
// 这一层只描述「读到的位置」和「要写的位置」，不碰 Win32——真的去问 Explorer、真的去发消息，是 Native 层那一个适配器的事。
// 视图这边只见接口：测试注入一份假的，CI 就永远碰不到用户的真实桌面。

// 一次位置读取的结果是什么样的：能不能拿、能不能排，由这里说。
public enum DesktopIconPositionStatus
{
    // 每一项都拿到了位置，可以按位置排。
    Ok,
    // 拿不到图标列表（Explorer 没在跑、桌面被 Shell 扩展换掉了），或某一项对不上号。
    // 这不是错误，是可接受的状态：页面退回按名字自动排。
    Unavailable,
    // 资源管理器开了「自动排列图标」：写进去的位置会被它当场改回去，所以排序在这里没有意义。
    AutoArrange,
}

// 一个槽位：图标在列表里的那一处，是资源管理器的客户区坐标，不是屏幕坐标。
// 用 double 而不是 POINT，是因为视图那一侧的落点计算本来就是 WPF 的 DIP；真正的整数坐标在写的时候才化。
public readonly record struct DesktopIconSlot(double X, double Y);

// 读回来的一份快照：每项的位置，键是 DesktopItems.Item.Path（系统项是它的解析名），所以两边能直接比。
public sealed record DesktopIconSnapshot(DesktopIconPositionStatus Status, IReadOnlyDictionary<string, DesktopIconSlot> Slots)
{
    // 拿不到位置时都是空的，调用方先看 Status 再取 Slots。
    public static DesktopIconSnapshot Unavailable(DesktopIconPositionStatus status = DesktopIconPositionStatus.Unavailable)
        => new(status, new Dictionary<string, DesktopIconSlot>());
}

// 要写出去的一个位置：这一项，摆到这里。
public readonly record struct DesktopIconPlacement(string Key, double X, double Y);

// 写一次的回报：成功与否，以及不成功是因为什么。失败一律是返回值，不抛异常到 UI 线程。
public sealed record DesktopIconWriteResult(bool Ok, DesktopIconPositionStatus Status)
{
    public static readonly DesktopIconWriteResult Done = new(true, DesktopIconPositionStatus.Ok);
    public static DesktopIconWriteResult Failed(DesktopIconPositionStatus status) => new(false, status);
}

// 桌面图标位置的读写接口。
public interface IDesktopIconPositions
{
    // 现在这些图标各摆在哪里。读不到就返回 Unavailable，不要猜。
    DesktopIconSnapshot Read();
    // 把它们摆成这样。只有 Read 给出过位置的项会被移动，没给出的留着不动。
    DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements);
}

// 永远读不到位置的实现：没有注入就用它。它一个 Win32 都不碰，所以测试就算忘了换，也不会动到用户的桌面。
public sealed class NullDesktopIconPositions : IDesktopIconPositions
{
    public DesktopIconSnapshot Read() => DesktopIconSnapshot.Unavailable();
    public DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements)
        => DesktopIconWriteResult.Failed(DesktopIconPositionStatus.Unavailable);
}

// Explorer 图标列表里的一行：它在列表里的第几格、Explorer 给它显示的名字、它的坐标。
// 读这件事本身要借 explorer 的内存（Native 层），把它认回页面的项却是纯计算，所以放在这里，测试能覆盖。
public readonly record struct DesktopIconRow(int Index, string Name, double X, double Y);

// 把 Explorer 的图标行认回这一页要画的那些项：每项的位置，和它在列表里的第几格（写回时要用）。
//
// 认出多少用多少，不要求一项不落。要求「全都认得出」太脆：Explorer 显示的名字随时可能和这里的
// 名字对不上（扩展名显示与否、文件夹名里带点、本地化差异），只要一项对不上就整份作废，
// 排序在真实桌面上就几乎永远用不了。认不出的项不写进快照，它们保持原顺序、位置也不动。
public static class DesktopIconMapping
{
    // namesOf 给出一项在 Explorer 里可能显示成的所有名字：系统项的名字随系统语言，
    // 而 AxDeck 的界面语言未必跟系统一致，所以几种写法都要比一遍。
    public static (IReadOnlyDictionary<string, DesktopIconSlot> Slots, IReadOnlyDictionary<string, int> Indexes)? Map(
        IReadOnlyList<DesktopIconRow> rows, IReadOnlyList<string> keys, Func<string, IReadOnlyList<string>> namesOf)
    {
        if (rows.Count == 0 || keys.Count == 0) return null;
        var free = rows.ToList();
        var slots = new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 先按名字原样认。
        foreach (var key in keys)
        {
            if (Take(free, namesOf(key)) is not { } row) continue;
            slots[key] = new DesktopIconSlot(row.X, row.Y);
            indexes[key] = row.Index;
        }
        // 再按去掉扩展名的名字认：Explorer 默认不显示已知类型的扩展名，「报告.docx」在桌面上写的是「报告」。
        // 完整名字先认掉，剩下的才对短的，两者就不会抢同一个格子。
        foreach (var key in keys)
        {
            if (slots.ContainsKey(key) || Stem(System.IO.Path.GetFileName(key)) is not { } stem) continue;
            if (Take(free, new List<string> { stem }) is not { } row) continue;
            slots[key] = new DesktopIconSlot(row.X, row.Y);
            indexes[key] = row.Index;
        }
        // 认不出的项和剩下的行数量相同时，按「两边都是按名字序排的」依次配对。
        // 这比随机挑一行可靠：Explorer 的列表和这一页的项本来就都按名字排。
        if (free.Count > 0)
        {
            var unnamed = keys.Where(key => !slots.ContainsKey(key)).ToList();
            if (unnamed.Count == free.Count)
            {
                var lines = free.OrderBy(row => row.Index).ToList();
                for (int i = 0; i < unnamed.Count; i++)
                {
                    slots[unnamed[i]] = new DesktopIconSlot(lines[i].X, lines[i].Y);
                    indexes[unnamed[i]] = lines[i].Index;
                }
            }
        }
        // 一项都没认出来：这不是「部分可用」，是根本没读到位置，交给调用方退回名字序。
        return slots.Count > 0 ? (slots, indexes) : null;
    }

    // 名字撞上不止一行时取列表里的第一行。
    //
    // 同名多半是用户桌面和公共桌面各放了一份同名快捷方式：Explorer 的列表里这样就是两行，
    // 而这一页按名字去重只画一个（DesktopItems.Merged 留用户桌面那份）。Explorer 先列用户桌面的
    // 那一份，页面画的也是那一份，所以第一行就是它。
    // 从前这里「撞上就不认」，那两项于是拿不到位置，被 OrderByPositions 甩到页面末尾——
    // 用户会看到两个图标自己跑到了最后，而不是停在原来的地方。
    private static DesktopIconRow? Take(List<DesktopIconRow> rows, IReadOnlyList<string> names)
    {
        var matches = rows
            .Where(row => names.Any(name => string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(row => row.Index)
            .ToList();
        if (matches.Count == 0) return null;
        rows.Remove(matches[0]);
        return matches[0];
    }

    // 名字去掉扩展名的形式：Explorer 默认不显示已知类型的扩展名。
    private static string? Stem(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[..dot] : null;
    }
}
