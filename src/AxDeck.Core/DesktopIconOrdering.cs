namespace AxDeck.Core;

// 把「图标的真实位置」换成「要画的顺序」，再把一次拖动的落点换成「新的顺序」和「要写回去的位置」。
//
// 这一层刻意只做计算：不碰窗口、不发消息、不问 Explorer，所以 AxDeck.Tests 能拿编出来的坐标全验一遍。
// 顺序的定义跟着资源管理器走：它按列排（先把一列从上到下排满，再往右一列），所以同一列里比上下，
// 不同的列之间比左右。但用户自己摆的图标可能挤得不成列，所以「哪一列」用容差认，而不是要求 X 一字不差。

// 一次拖动的落点：松手时这一项（或这一组）要插到谁的前面或后面。
public readonly record struct DesktopIconInsertion(string? TargetKey, bool After)
{
    // 算不出落点（指针在空白处、目标就是被拖的项本身）时是空的，调用方什么都不做。
    public bool IsValid => TargetKey is not null;
    public static readonly DesktopIconInsertion None = new(null, false);
}

public static class DesktopIconOrdering
{
    // 列与列之间的容差：X 相差不到这么多算同一列。取的是「一个图标大概的宽度」量级，比图标间距宽、比一列的跨度窄，
    // 所以挤歪一点的同一列还认得出，真的换列了就不会认错。
    public const double ColumnTolerance = 24d;

    // 一项在网格里的位置，用来算落点：拖动的中线和行的归属都得看它的矩形，不能只看槽位。
    public readonly record struct Rect(string Key, double Left, double Top, double Width, double Height)
    {
        public double MiddleX => Left + Width / 2;
        public double Bottom => Top + Height;
    }

    // 快照里的槽位换成阅读顺序：列按左右，列内按上下。
    // 返回值按这个顺序排，调用方拿第 i 个槽位配第 i 项，就是「顺序」和「位置」之间那座桥。
    public static IReadOnlyList<DesktopIconSlot> CanonicalSlots(IReadOnlyList<DesktopIconSlot> slots, double tolerance = ColumnTolerance)
    {
        if (slots.Count == 0) return [];

        // 先按左右分列：排好序扫一遍，X 比上一列的代表值远过容差就开一列。
        List<List<DesktopIconSlot>> columns = [];
        foreach (var slot in slots.OrderBy(slot => slot.X).ThenBy(slot => slot.Y))
        {
            if (columns.Count > 0 && slot.X - columns[^1][0].X <= tolerance) { columns[^1].Add(slot); continue; }
            columns.Add([slot]);
        }
        // 列已经按左右排好了，列内按上下排，摊平就是阅读顺序。
        return [.. columns.SelectMany(column => column.OrderBy(slot => slot.Y))];
    }

    // 要画的项按真实位置重排。每一项的位置从快照里取；取不到的项留着不动，排到后面（它们没有位置，
    // 说明适配器认不出它们在列表里的哪一格，硬塞进去只会把整页顺序弄乱）。
    // 顺序的兜底是传进来的 items 本身的顺序：调用方给的是 DesktopItems.Ordered，所以位置缺失时页面照旧。
    public static IReadOnlyList<T> OrderByPositions<T>(IReadOnlyList<T> items, Func<T, string> keyOf, DesktopIconSnapshot snapshot)
    {
        if (snapshot.Status != DesktopIconPositionStatus.Ok || snapshot.Slots.Count == 0) return items;

        // 槽位先换成阅读顺序，于是「排第几」等于「在列表里的第几个格」。
        // 两处撞在同一个格（快照理论上允许）时记住第一个，别让它俩抢同一个名次。
        var order = CanonicalSlots([.. snapshot.Slots.Values]);
        var rankOf = new Dictionary<DesktopIconSlot, int>();
        for (int i = 0; i < order.Count; i++) rankOf.TryAdd(order[i], i);
        int Rank(string key)
            => snapshot.Slots.TryGetValue(key, out var slot) && rankOf.TryGetValue(slot, out int at) ? at : order.Count;
        // 同格或都没位置的，保持它们在 items 里的先后，不让人家看起来在乱跳。
        return [.. items.Select((item, at) => (item, at))
            .OrderBy(pair => Rank(keyOf(pair.item)))
            .ThenBy(pair => pair.at)
            .Select(pair => pair.item)];
    }

    // 落点：指针停在谁的前面或后面。
    // rects 是页面上现在画着的每一项的矩形（视图把自己的格子换算进来），dragged 是正在被拖走的那些。
    //
    // 思路与旧版分组窗口那条一致（按上下分行、行内按中线定前后），只是重写一遍，不引用旧类型：
    // 被拖的那些不参与计算——它们正跟着鼠标走，留在原处只会让落点跳来跳去。
    public static DesktopIconInsertion ResolveInsertion(
        IReadOnlyList<Rect> rects, double pointerX, double pointerY, IReadOnlySet<string> dragged, double rowGap)
    {
        var candidates = rects.Where(rect => !dragged.Contains(rect.Key)).ToList();
        if (candidates.Count == 0) return DesktopIconInsertion.None;

        var rows = SplitRows(candidates);
        if (rows.Count == 0) return DesktopIconInsertion.None;

        // 指针落在两行之间时归到更近的那一行，tolerance 给到半个行距，免得行与行的缝里点不出落点。
        var tolerance = Math.Max(8, rowGap / 2);
        var row = rows.FirstOrDefault(candidate => pointerY <= RowBottom(candidate) + tolerance) ?? rows[^1];
        foreach (var item in row)
            if (pointerX <= item.MiddleX) return new DesktopIconInsertion(item.Key, false);
        return new DesktopIconInsertion(row[^1].Key, true);
    }

    // 一次拖动提交后的新顺序：把被拖的那一组从原处整组摘掉，再按它们原本的先后插到落点。
    // 落点无效、或落点目标自己也在被拖的组里，就什么也不改（原样返回，调用方据此不动）。
    public static IReadOnlyList<string> ApplyInsertion(
        IReadOnlyList<string> order, IReadOnlySet<string> dragged, DesktopIconInsertion insertion)
    {
        if (!insertion.IsValid || dragged.Count == 0) return order;
        if (insertion.TargetKey is { } target && dragged.Contains(target)) return order;
        if (!order.Any(key => dragged.Contains(key))) return order;

        // 摘掉被拖的那些，剩下的相对顺序不动。
        var kept = order.Where(key => !dragged.Contains(key)).ToList();
        // 被拖的那些按它们原来的先后成组。
        var moving = order.Where(key => dragged.Contains(key)).ToList();

        int at;
        if (insertion.TargetKey is not { } anchor) at = kept.Count;
        else
        {
            int index = kept.FindIndex(key => string.Equals(key, anchor, StringComparison.OrdinalIgnoreCase));
            if (index < 0) at = kept.Count;
            else at = insertion.After ? index + 1 : index;
        }
        kept.InsertRange(at, moving);
        return kept;
    }

    // 新顺序换成要写出去的位置：能写的那些项，按新顺序瓜分它们自己原来占着的那些槽位。
    //
    // 只动「这一页要写、且快照给得出位置」的项：快照里可能还有这一页不画的项（Explorer 列得出、
    // 页面按规则藏起来的），它们占的槽位原样留着——抢了不画项的位置，页面上看不出什么，
    // 真实桌面上却会把一个用户没选的图标挪走。
    // 每一项的新位置都在「可写项原有槽位」的集合里，所以怎么排都只是换主人，不会把图标甩到列表外面。
    public static IReadOnlyList<DesktopIconPlacement> PlanPlacements(
        IReadOnlyList<string> newOrder, DesktopIconSnapshot snapshot)
    {
        if (snapshot.Status != DesktopIconPositionStatus.Ok || snapshot.Slots.Count == 0) return [];
        var movable = newOrder.Where(snapshot.Slots.ContainsKey).ToList();
        if (movable.Count == 0) return [];
        // 这些项自己的槽位按阅读顺序排好，第 i 项配第 i 个。
        var pool = CanonicalSlots([.. movable.Select(key => snapshot.Slots[key])]);

        var placements = new List<DesktopIconPlacement>(movable.Count);
        for (int i = 0; i < movable.Count; i++)
            placements.Add(new DesktopIconPlacement(movable[i], pool[i].X, pool[i].Y));
        return placements;
    }

    // 按上下把各项分行：顶边挨得近的算同一行。
    private static List<List<Rect>> SplitRows(List<Rect> candidates)
    {
        var sorted = candidates.OrderBy(rect => rect.Top).ThenBy(rect => rect.Left).ToList();
        var rows = new List<List<Rect>>();
        // 行与行之间差一点高度就算同一行，不然行距一大图标就被拆成两行。
        var merge = Math.Max(4, sorted[0].Height * 0.25);
        foreach (var item in sorted)
        {
            if (rows.Count > 0 && Math.Abs(rows[^1][0].Top - item.Top) <= merge) { rows[^1].Add(item); continue; }
            rows.Add([item]);
        }
        return rows;
    }

    // 一行的下沿：取这一行里最高的那个图标的底边，这样同一行里有个图标的名字多占了一行高度时也不至于把行分错。
    private static double RowBottom(List<Rect> row) => row.Max(rect => rect.Bottom);
}
