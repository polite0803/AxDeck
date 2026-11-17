using AxDeck.Core;

internal static class DesktopIconOrderingTests
{
    public static void Run(Action<string, bool> check)
    {
        var slot = (double x, double y) => new DesktopIconSlot(x, y);

        // ---- CanonicalSlots：列优先，列内按上下 ----
        // 两列，每列三项：先读满左边那一列，再读右边那一列。
        var twoColumns = new[] { slot(0, 0), slot(0, 90), slot(0, 180), slot(80, 0), slot(80, 90), slot(80, 180) };
        var read = DesktopIconOrdering.CanonicalSlots(twoColumns);
        check("列优先：先从上到下排满一列",
            read.SequenceEqual([slot(0, 0), slot(0, 90), slot(0, 180), slot(80, 0), slot(80, 90), slot(80, 180)]));

        // 输入乱序也要先分列再列内排：打乱一遍，结果应一样。
        var shuffled = new[] { slot(80, 180), slot(0, 90), slot(80, 0), slot(0, 180), slot(80, 90), slot(0, 0) };
        check("列优先不依赖输入顺序",
            DesktopIconOrdering.CanonicalSlots(shuffled).SequenceEqual(read));

        // 同一列里挤歪一点（X 差不到容差）还算一列。
        var wobble = new[] { slot(0, 0), slot(10, 90), slot(20, 180) };
        check("同一列里挤歪一点还认成一列",
            DesktopIconOrdering.CanonicalSlots(wobble).Count == 3
            && DesktopIconOrdering.CanonicalSlots(wobble)[0].Y == 0
            && DesktopIconOrdering.CanonicalSlots(wobble)[2].Y == 180);

        // 负坐标（第二块显示器在左边）：X 小的算更靠左的列。
        var negative = new[] { slot(-100, 0), slot(0, 0) };
        check("负坐标的列排在前面",
            DesktopIconOrdering.CanonicalSlots(negative)[0].X == -100);

        check("空槽位得到空顺序",
            DesktopIconOrdering.CanonicalSlots([]).Count == 0);

        // ---- OrderByPositions：把项按真实位置重排 ----
        var snapshot = new DesktopIconSnapshot(DesktopIconPositionStatus.Ok, new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = slot(0, 0), ["b"] = slot(0, 90), ["c"] = slot(80, 0), ["d"] = slot(80, 90),
        });
        // 喂进去的是字母序，按位置应变成列优先：a,b 在左列，c,d 在右列。
        var ordered = DesktopIconOrdering.OrderByPositions(new[] { "d", "c", "b", "a" }, k => k, snapshot);
        check("按位置重排成列优先",
            ordered.SequenceEqual(["a", "b", "c", "d"]));

        // 快照不可用时原样返回（调用方给的是名字序）。
        var none = DesktopIconOrdering.OrderByPositions(new[] { "x", "y" }, k => k, DesktopIconSnapshot.Unavailable());
        check("位置不可用时保持原顺序", none.SequenceEqual(["x", "y"]));

        // 某一项没有位置：留到后面，其余照位置排。
        var partial = new DesktopIconSnapshot(DesktopIconPositionStatus.Ok, new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = slot(80, 0), ["b"] = slot(0, 0),
        });
        var withMissing = DesktopIconOrdering.OrderByPositions(new[] { "z", "a", "b" }, k => k, partial);
        check("没有位置的项排到最后",
            withMissing.SequenceEqual(["b", "a", "z"]));

        // ---- ResolveInsertion：落点在哪一项前面 ----
        // 一行三项，每个 70 宽。指针落在第二项的左半边 → 落在第二项之前。
        var rects = new[]
        {
            new DesktopIconOrdering.Rect("a", 0, 0, 70, 90),
            new DesktopIconOrdering.Rect("b", 75, 0, 70, 90),
            new DesktopIconOrdering.Rect("c", 150, 0, 70, 90),
        };
        var into = DesktopIconOrdering.ResolveInsertion(rects, 80, 10, new HashSet<string>(), 4);
        check("落点在第二项左半边算它之前",
            into.TargetKey == "b" && !into.After);

        // 落在最后一项的右半边 → 排在最后一项之后。
        var tail = DesktopIconOrdering.ResolveInsertion(rects, 210, 10, new HashSet<string>(), 4);
        check("落在最后一项右边算它之后",
            tail.TargetKey == "c" && tail.After);

        // 被拖的那一项不参与落点：拖 b 经过 b 自己时落点应跳过它。
        var dragging = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b" };
        var skipSelf = DesktopIconOrdering.ResolveInsertion(rects, 80, 10, dragging, 4);
        check("被拖的项不当落点，跳到后面的项",
            skipSelf.TargetKey == "c");

        // 没有任何候选项时落点无效。
        var allDragged = DesktopIconOrdering.ResolveInsertion(rects, 80, 10,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a", "b", "c" }, 4);
        check("全在被拖集合里时落点无效", !allDragged.IsValid);

        // ---- ApplyInsertion：单把一项挪到落点 ----
        List<string> order = ["a", "b", "c", "d", "e"];
        var one = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b" };
        var moved = DesktopIconOrdering.ApplyInsertion(order, one, new DesktopIconInsertion("e", false));
        check("把 b 插到 e 之前",
            moved.SequenceEqual(["a", "c", "d", "b", "e"]));

        // 多选：拖 {b,d} 到 e 之前，组内保持原先后。
        var many = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "b", "d" };
        var movedMany = DesktopIconOrdering.ApplyInsertion(order, many, new DesktopIconInsertion("e", false));
        check("多选整体插入，组内顺序不变",
            movedMany.SequenceEqual(["a", "c", "b", "d", "e"]));

        // 落点无效（None）：什么都不改。
        check("落点无效时不改顺序",
            DesktopIconOrdering.ApplyInsertion(order, one, DesktopIconInsertion.None).SequenceEqual(order));

        // 落点目标就在被拖的组里：不动。
        check("落点是自己的项时不改顺序",
            DesktopIconOrdering.ApplyInsertion(order, many, new DesktopIconInsertion("b", true)).SequenceEqual(order));

        // ---- PlanPlacements：新顺序瓜分原有槽位 ----
        // a,b 在左列，c,d 在右列。把顺序倒过来，每项应拿到倒过来位置对应的槽。
        var positions = new DesktopIconSnapshot(DesktopIconPositionStatus.Ok, new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = slot(0, 0), ["b"] = slot(0, 90), ["c"] = slot(80, 0), ["d"] = slot(80, 90),
        });
        var placements = DesktopIconOrdering.PlanPlacements(["d", "c", "b", "a"], positions);
        // 阅读顺序的槽位池是 (0,0),(0,90),(80,0),(80,90)。d 拿第一个，c 拿第二个，b 第三，a 第四。
        check("倒序写回：每项分到池子里的一个槽",
            placements.Count == 4
            && placements[0] is { Key: "d", X: 0d, Y: 0d }
            && placements[1] is { Key: "c", X: 0d, Y: 90d }
            && placements[2] is { Key: "b", X: 80d, Y: 0d }
            && placements[3] is { Key: "a", X: 80d, Y: 90d });

        // 槽位集合不变：写回去的位置集合和读来的位置集合是同一批（只是换了主人）。
        var written = placements.Select(p => slot(p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y);
        var cameFrom = positions.Slots.Values.Select(s => (s.X, s.Y)).OrderBy(s => s.X).ThenBy(s => s.Y);
        check("写回的槽位集合和读来的同一批",
            written.Zip(cameFrom).All(pair => pair.First.X == pair.Second.X && pair.First.Y == pair.Second.Y));

        // 快照里有这一页不画的项：它占的槽不动，只瓜分可写项自己的槽。
        var withHidden = new DesktopIconSnapshot(DesktopIconPositionStatus.Ok, new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = slot(0, 0), ["b"] = slot(0, 90), ["ghost"] = slot(80, 0),
        });
        var partial2 = DesktopIconOrdering.PlanPlacements(["b", "a"], withHidden);
        check("不画项的槽不被抢，可写项只换自己的槽",
            partial2.Count == 2
            && partial2.All(p => p.Key != "ghost")
            && partial2.Any(p => p.Key == "b" && p.X == 0 && p.Y == 0)
            && partial2.Any(p => p.Key == "a" && p.X == 0 && p.Y == 90));

        // 快照不可用时写不出任何位置。
        check("位置不可用时写不出位置",
            DesktopIconOrdering.PlanPlacements(["a"], DesktopIconSnapshot.Unavailable()).Count == 0);

        // ---- DesktopIconMapping：把 Explorer 的行认回页面的项 ----
        // 名字对得上就认位置，序号也一起记住（写回要用）。
        var mapped = DesktopIconMapping.Map(
            [new DesktopIconRow(0, "报告", 0, 0), new DesktopIconRow(1, "notes.txt", 0, 90)],
            [@"C:\D\notes.txt", @"C:\D\报告.docx"],
            key => key.EndsWith(".txt") ? [System.IO.Path.GetFileName(key)] : ["报告"]);
        check("按名字认回位置与序号",
            mapped is { } m && m.Slots[@"C:\D\notes.txt"] == slot(0, 90) && m.Indexes[@"C:\D\notes.txt"] == 1);
        check("去掉扩展名的名字也认得出",
            mapped is { } m2 && m2.Slots[@"C:\D\报告.docx"] == slot(0, 0));

        // 重名：用户桌面和公共桌面各放了一份同名快捷方式，Explorer 的列表里就是两行，
        // 而页面按名字去重只画一个。这时必须认第一行，不能因为「撞上了」就放弃——
        // 放弃了这一项就拿不到位置，会被 OrderByPositions 甩到页面末尾，看起来是两个图标自己跑了。
        var clash = DesktopIconMapping.Map(
            [new DesktopIconRow(0, "Git Bash", 0, 0), new DesktopIconRow(13, "火绒", 0, 90), new DesktopIconRow(20, "Git Bash", 80, 0)],
            [@"C:\Users\me\Desktop\Git Bash.lnk", @"C:\D\火绒.lnk"],
            key => [System.IO.Path.GetFileName(key)]);
        check("重名时认列表里的第一行",
            clash is { } c && c.Slots[@"C:\Users\me\Desktop\Git Bash.lnk"] == slot(0, 0));
        check("重名时记的是第一行的序号",
            clash is { } c2 && c2.Indexes[@"C:\Users\me\Desktop\Git Bash.lnk"] == 0);

        // 名字一个都对不上、但剩下的行数和项数相同：按「两边都是按名字序排的」依次配对，认下来。
        // 这比随机挑一行可靠，也是曾经「名字普遍对不上」时仍能用的那条路。
        check("名字对不上但数量相同时按序号配对",
            DesktopIconMapping.Map([new DesktopIconRow(0, "别的", 5, 6)], [@"C:\D\x.txt"], _ => ["x"]) is { } paired
            && paired.Slots[@"C:\D\x.txt"] == slot(5, 6));
        // 行数比项数少、名字又都对不上：没法配对，交给调用方退回名字序。
        check("对不上又数量不匹配时返回空",
            DesktopIconMapping.Map([new DesktopIconRow(0, "别的", 0, 0), new DesktopIconRow(1, "另的", 0, 90)],
                [@"C:\D\x.txt"], _ => ["x"]) is null);
        check("没有行时返回空",
            DesktopIconMapping.Map([], [@"C:\D\x.txt"], _ => ["x"]) is null);
    }
}
