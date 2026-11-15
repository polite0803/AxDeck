namespace AxDeck.Core;

// 分组里拖动时的落点：算出来它会落在哪一项的前面或后面。
// 分组里的图标按行排开，所以先按上下找出指针在哪一行，再在那一行里按左右找出落在谁的前面。
// 移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），那里的网格排序也是这么算的。

// 一项在网格里的位置。
public readonly record struct BoxItemRect(string Key, double Left, double Top, double Width, double Height)
{
    // 这一项的中线，指针在它左边就是落在它前面。
    public double MiddleX => Left + Width / 2;
    // 这一项的下沿。
    public double Bottom => Top + Height;
}

// 落点：落在目标项的前面还是后面。
public readonly record struct BoxInsertion(string? TargetKey, bool After)
{
    // 算不出落点时是空的。
    public bool IsValid => TargetKey is not null;
    // 落在最后一项之后。
    public static readonly BoxInsertion None = new(null, false);
}

public static class BoxInsertionResolver
{
    // 算落点。items 是分组里各项的位置，point 是指针的位置，dragged 是正在被拖动的那些项。
    public static BoxInsertion Resolve(IReadOnlyList<BoxItemRect> items, double pointerX, double pointerY, IReadOnlySet<string> dragged, double rowGap)
    {
        // 正在被拖动的那几项不参与计算：它们自己会跟着鼠标走，留在原处只会让落点跳来跳去。
        var candidates = items.Where(item => !dragged.Contains(item.Key)).ToList();
        if (candidates.Count == 0) return BoxInsertion.None;

        var rows = SplitRows(candidates);
        if (rows.Count == 0) return BoxInsertion.None;

        // 指针落在两行之间时，归到更近的那一行。
        var tolerance = Math.Max(8, rowGap / 2);
        var row = rows.FirstOrDefault(candidate => pointerY <= RowBottom(candidate) + tolerance) ?? rows[^1];
        return ResolveInRow(row, pointerX);
    }

    // 按上下把各项分行：顶边挨得近的算同一行。
    private static List<List<BoxItemRect>> SplitRows(List<BoxItemRect> candidates)
    {
        var sorted = candidates.OrderBy(item => item.Top).ThenBy(item => item.Left).ToList();
        var rows = new List<List<BoxItemRect>>();
        // 行与行之间差一点高度就算同一行，不然行距一大图标就被拆成两行。
        var merge = Math.Max(4, sorted[0].Height * 0.25);
        foreach (var item in sorted)
        {
            var current = rows.Count > 0 ? rows[^1] : null;
            if (current is not null && Math.Abs(current[0].Top - item.Top) <= merge) { current.Add(item); continue; }
            rows.Add([item]);
        }
        return rows;
    }

    // 在一行里按左右找落点：落在谁的左边就是谁的前面，一行最右边就落在最后一个的后面。
    private static BoxInsertion ResolveInRow(List<BoxItemRect> row, double pointerX)
    {
        foreach (var item in row)
            if (pointerX <= item.MiddleX) return new BoxInsertion(item.Key, false);
        return new BoxInsertion(row[^1].Key, true);
    }

    // 一行的下沿：取这一行里最高的那个图标的底边，
    // 这样同一行里有个图标的名字多占了一行高度时也不至于把行分错。
    private static double RowBottom(List<BoxItemRect> row) => row.Max(item => item.Bottom);
}
