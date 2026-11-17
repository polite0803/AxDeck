using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

// 桌面页的拖动排序：位置从适配器读、页面照着排、拖动提交时写回去。
// 这里用一份假的适配器，绝不碰真实桌面：真适配器（ShellDesktopIconPositions）连编译都不进这个项目。
internal static class DesktopOrderDragTests
{
    public static void Run(Action<string, bool> check)
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        _ = app;

        // ---- 拖动数据：一份给本页排序，一份给别的程序 ----
        var data = DesktopDrag.Build([@"C:\D\b.txt", @"C:\D\a.txt"], [@"C:\D\b.txt", @"C:\D\a.txt"]);
        check("拖动数据里有本页自己的格式", DesktopDrag.IsInternal(data));
        check("拖动数据里的键按选中的先后",
            DesktopDrag.Keys(data) is [@"C:\D\b.txt", @"C:\D\a.txt"]);
        check("拖动数据里同时带着给别的程序的 FileDrop", data.GetDataPresent(DataFormats.FileDrop));
        // WPF 把 SetFileDropList 的那一份取回来时是 string[]（实测确认），不是 StringCollection。
        // 外面程序交进来的也是 string[]；两条都得认，Area_Drop 的 switch 正是这么写的。
        check("FileDrop 取得回来的是文件路径", data.GetData(DataFormats.FileDrop) is string[]);

        var onlySystem = DesktopDrag.Build(["::{645FF040-5081-101B-9F08-00AA002F954E}"], []);
        check("只拖系统项时没有 FileDrop，但排序格式还在",
            DesktopDrag.IsInternal(onlySystem) && !onlySystem.GetDataPresent(DataFormats.FileDrop));

        var foreign = new DataObject();
        foreign.SetData(DataFormats.FileDrop, new[] { @"C:\D\x.txt" });
        check("别的程序起的数据不算本页的拖动", !DesktopDrag.IsInternal(foreign));
        check("别的程序起的数据没有键", DesktopDrag.Keys(foreign).Length == 0);

        // ---- 页面按位置排，而不是按名字 ----
        // 测试项目里的 MainWindow 是 Window 的子类（见 ShadowLifetimeTests 的 partial 声明），可以直接显示。
        var window = new MainWindow { Width = 640, Height = 320, Left = -20000, Top = -20000,
            ShowActivated = false, WindowStyle = WindowStyle.None,
            AllowsTransparency = true, Background = Brushes.Transparent };
        var items = new List<DesktopItems.Item>
        {
            new(@"C:\D\a.txt", "a.txt", DesktopItems.Kind.File, true),
            new(@"C:\D\b.txt", "b.txt", DesktopItems.Kind.File, true),
            new(@"C:\D\c.txt", "c.txt", DesktopItems.Kind.File, true),
        };
        // 桌面上的真实位置：c 在第一格，a 在第二格，b 在第三格。名字序是 a,b,c。
        var fake = new FakePositions(new Dictionary<string, DesktopIconSlot>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\D\c.txt"] = new(0, 0),
            [@"C:\D\a.txt"] = new(0, 90),
            [@"C:\D\b.txt"] = new(0, 180),
        });
        DesktopIconsView.ItemsOverride = () => items;
        var view = new DesktopIconsView(window, fake) { Width = 640, Height = 320 };
        window.Content = view;
        try
        {
            window.Show();
            Pump();
            view.Refresh(force: true);
            Pump();

            var paths = DrawnPaths(view);
            check($"页面按真实位置排（{string.Join(",", paths)}）",
                paths.SequenceEqual([@"C:\D\c.txt", @"C:\D\a.txt", @"C:\D\b.txt"]));

            // ---- 位置读不到时退回名字序 ----
            fake.Status = DesktopIconPositionStatus.Unavailable;
            view.Refresh(force: true);
            Pump();
            check("位置读不到时退回名字序",
                DrawnPaths(view).SequenceEqual([@"C:\D\a.txt", @"C:\D\b.txt", @"C:\D\c.txt"]));

            // ---- 开了自动排列：顺序不动，并且说得出原因 ----
            fake.Status = DesktopIconPositionStatus.AutoArrange;
            view.Refresh(force: true);
            Pump();
            check("自动排列时退回名字序",
                DrawnPaths(view).SequenceEqual([@"C:\D\a.txt", @"C:\D\b.txt", @"C:\D\c.txt"]));
            check("自动排列时写入会被拒",
                !fake.Write([new DesktopIconPlacement(@"C:\D\a.txt", 0, 0)]).Ok);

            // ---- 提交排序：写回的是位置，槽位集合不变 ----
            fake.Status = DesktopIconPositionStatus.Ok;
            view.Refresh(force: true);
            Pump();
            // 名字序下一行三项：a(0,0) b(0,90) c(0,180)。把 c 拖到最前。
            var placements = DesktopIconOrdering.PlanPlacements([@"C:\D\c.txt", @"C:\D\a.txt", @"C:\D\b.txt"], fake.Read());
            check("写回的位置数是可写项数",
                placements.Count == 3);
            check("写回只换主人，不把图标甩出原来的槽",
                placements.Select(p => (p.X, p.Y)).OrderBy(p => p.Y)
                    .SequenceEqual(new (double X, double Y)[] { (0, 0), (0, 90), (0, 180) }));
            check("第一项拿到了最前面的那个槽",
                placements.First() is { Key: @"C:\D\c.txt", X: 0d, Y: 0d });
            check("写入按计划执行", fake.Write(placements).Ok);
            check("写进去的项数就是计划里的项数", fake.LastWrite.Count == 3);

            // ---- 提交真的走一遍页面：改 Children、写位置、再按真实位置重画 ----
            // 拖动本身要真的按住鼠标，这里直接调提交那一步，其余全是生产代码。
            // 提交失败时会弹说明窗口，测试里把它接住，免得 ShowDialog 挂住。
            var said = new List<string>();
            BoxReport.ErrorOverride = said.Add;
            // 现在的顺序是 c,a,b：把 b 拖到 a 之前，应变成 c,b,a。
            Commit(view, new DesktopIconInsertion(@"C:\D\a.txt", false), [@"C:\D\b.txt"]);
            Pump();
            check($"提交后页面按新顺序画（{string.Join(",", DrawnPaths(view))}）",
                DrawnPaths(view).SequenceEqual([@"C:\D\c.txt", @"C:\D\b.txt", @"C:\D\a.txt"]));
            check("提交真的把位置写出去了", fake.LastWrite.Count == 3);

            // 写失败：页面不能留着一个没存下来的排法，要退回真实位置那一种。
            // 现在画的是 c,b,a；把 a 拖到 b 之前应当变成 c,a,b，但写不进去，所以页面必须还是 c,b,a。
            fake.FailWrite = true;
            Commit(view, new DesktopIconInsertion(@"C:\D\b.txt", false), [@"C:\D\a.txt"]);
            Pump();
            check($"写失败时页面退回真实位置排出来的顺序（{string.Join(",", DrawnPaths(view))}）",
                DrawnPaths(view).SequenceEqual([@"C:\D\c.txt", @"C:\D\b.txt", @"C:\D\a.txt"]));
            check("写失败时结果不是成功", !fake.Write(placements).Ok);
            check($"写失败时告诉了用户（{said.FirstOrDefault() ?? "没有说"}）", said.Count == 1);
        }
        catch (Exception ex)
        {
            check($"拖动排序验得下去（{ex.GetType().Name}: {ex.Message}）", false);
        }
        finally
        {
            BoxReport.ErrorOverride = null;
            DesktopIconsView.ItemsOverride = null;
            window.Close();
        }
    }

    // 走一遍提交：拖动本身要真的按住鼠标，这一步是拖动松手之后生产代码做的事，直接调它。
    // 私有方法用反射，跟这个项目里读私有字段的写法一致；参数变了下一条检查就会失败，不会悄悄放过。
    private static void Commit(DesktopIconsView view, DesktopIconInsertion insertion, string[] dragged)
    {
        var method = typeof(DesktopIconsView).GetMethod("CommitOrder",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?? throw new InvalidOperationException("CommitOrder 不见了");
        var set = new HashSet<string>(dragged, StringComparer.OrdinalIgnoreCase);
        method.Invoke(view, new object?[] { insertion, set });
    }

    // 一份假的适配器：位置自己给，写出去的记下来。永远不碰真实桌面。
    private sealed class FakePositions(Dictionary<string, DesktopIconSlot> slots) : IDesktopIconPositions
    {
        public DesktopIconPositionStatus Status { get; set; } = DesktopIconPositionStatus.Ok;
        public bool FailWrite { get; set; }
        public IReadOnlyList<DesktopIconPlacement> LastWrite { get; private set; } = [];

        public DesktopIconSnapshot Read() => Status == DesktopIconPositionStatus.Ok
            ? new DesktopIconSnapshot(Status, slots)
            : DesktopIconSnapshot.Unavailable(Status);

        public DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements)
        {
            if (Status != DesktopIconPositionStatus.Ok || FailWrite) return DesktopIconWriteResult.Failed(Status);
            LastWrite = placements;
            // 真实适配器写下去之后，再读回来就是新位置：假的那一份也跟着变，页面重画才看得见。
            foreach (var placement in placements) slots[placement.Key] = new DesktopIconSlot(placement.X, placement.Y);
            return DesktopIconWriteResult.Done;
        }
    }

    // 页面现在按什么顺序画着。
    private static List<string> DrawnPaths(DesktopIconsView view)
    {
        var grid = FindGrid(view);
        if (grid is null) return [];
        return [.. grid.Children.OfType<Border>().Select(child => child.Tag as string ?? "")];
    }

    private static BoxIconGrid? FindGrid(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is BoxIconGrid grid) return grid;
            if (FindGrid(child) is { } found) return found;
        }
        return null;
    }

    private static void Pump()
    {
        for (int i = 0; i < 4; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}