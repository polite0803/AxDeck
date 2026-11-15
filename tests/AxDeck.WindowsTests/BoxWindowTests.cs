using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using AxDeck.Core;

namespace AxDeck;

// The desktop organizer's window, built for real: the box is opened on the desktop, filled with real files and real
// system entries, and looked at afterwards. The checks read the visual tree, so they see what the user sees.
internal static class BoxWindowTests
{
    // The check the caller gave, and the detail a failed check wants to say.
    private static Action<string, bool> _check = null!;
    private static int _failed;
    private static readonly List<string> _failedNames = [];

    public static void Run(Action<string, bool> check)
    {
        _check = check;
        _failed = 0;
        _failedNames.Clear();

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The box's own menu takes the panel's menu style; the test app has none, so a plain one stands in. Each style
        // has to be written for its own kind of control, or the framework refuses it.
        foreach (var (key, type) in new (string, Type)[]
        {
            ("ShelfMenu", typeof(ContextMenu)),
            ("ShelfMenuItem", typeof(MenuItem)),
            ("ShelfMenuSeparator", typeof(Separator))
        })
            if (app.Resources[key] is null) app.Resources[key] = new Style(type);

        var sandbox = Path.Combine(Path.GetTempPath(), "axdeck-boxwindow-" + Guid.NewGuid().ToString("N")[..8]);
        var root = Path.Combine(sandbox, "boxes");
        Directory.CreateDirectory(root);
        try
        {
            var service = new BoxService();
            // The native icons are left alone here: this check is about the window, and the icons have their own check.
            service.Load(new DasktopData { Settings = new DasktopSettings { CollectionRoot = root, AutoHideShellIcons = false } });
            var box = service.Create("验证窗口");

            foreach (var name in new[] { "一.txt", "二.txt", "三.txt" })
                File.WriteAllText(Path.Combine(box.FolderPath, name), name);
            // The system entries go in between the real files, so the mixed order is what the window shows.
            service.AddVirtualItem(box.Id, "this-pc");
            service.AddVirtualItem(box.Id, "recycle-bin");

            var window = new BoxWindow(service, box);
            // Off screen and never activated: nothing shows where the tests run.
            window.Left = -20000; window.Top = -20000;
            try
            {
                window.Show();
                Pump();

                CheckWindow(window, service, box);
                CheckIcons(window, service, box);
                CheckDropMark(window);
            }
            catch (Exception ex) { Note($"the box window is built without complaint ({ex.GetType().Name}: {ex.Message})", false); }
            finally { window.Close(); }
        }
        finally { try { Directory.Delete(sandbox, true); } catch { /* 沙盒留在临时目录也无妨 */ } }

        foreach (var name in _failedNames) _check(name, false);
    }

    // 一条断言：名字，加上失败时要说的话。
    private static void Note(string name, bool ok, string? detail = null)
    {
        if (ok) { _check(detail is null ? name : name, true); return; }
        _failed++;
        _failedNames.Add(detail is null ? name : $"{name} -- {detail}");
    }

    // ---- 窗口本身 ----

    private static void CheckWindow(BoxWindow window, BoxService service, DesktopBox box)
    {
        Note("分组窗口真的开出来了", window.IsVisible);
        Note("分组窗口是透明的，盖在桌面上", window.AllowsTransparency && window.Background == Brushes.Transparent);
        Note("分组窗口不出现在任务栏里", !window.ShowInTaskbar);
        Note("分组窗口没有标题栏", window.WindowStyle == WindowStyle.None);
        Note("分组窗口不会被系统缩放", window.ResizeMode == ResizeMode.NoResize);
        Note("分组窗口在它自己存的位置上", window.Left == box.X && window.Top == box.Y);
        Note("分组窗口是它自己存的大小", window.Width == box.Width && window.Height == box.Height);

        // The title says the name of the box.
        Note("分组窗口的标题写着分组的名字", Texts(window).Any(text => text == box.Title));
    }

    // ---- 图标 ----

    private static void CheckIcons(BoxWindow window, BoxService service, DesktopBox box)
    {
        var icons = window.Icons;
        Note("分组窗口里有一块图标区", icons is not null);
        if (icons is null) return;
        Note("图标区在视觉树里", icons.IsVisible);

        var children = icons.Children.OfType<FrameworkElement>().ToList();
        Note("五项都在图标区里：三个真文件两个系统项", children.Count == 5, $"实际 {children.Count}");

        // Every item carries its own key, and the order is the order of the table.
        var tags = children.Select(child => child.Tag as string ?? "").ToList();
        Note("每一项都带着自己的键", tags.All(tag => tag.Length > 0), string.Join(" | ", tags));
        Note("图标区的顺序跟顺序表一样", tags.SequenceEqual(service.ItemKeys(box.Id)),
            string.Join(" | ", tags) + " vs " + string.Join(" | ", service.ItemKeys(box.Id)));

        // The real files and the system entries lie next to each other, not in two blocks.
        var shellAt = tags.Select((tag, index) => (tag, index)).Where(pair => BoxItemKey.IsShell(pair.tag)).Select(pair => pair.index).ToList();
        Note("系统项和真文件在图标区里交错着排", shellAt.Count == 2 && shellAt[0] > 0 && shellAt[0] < tags.Count - 1,
            string.Join(",", shellAt));

        // Each item draws its icon, at the size the settings ask for. The icon sits in its own little host,
        // so it is looked for down the tree rather than among the item's direct children.
        var images = children.Select(child => ImageOf(child)).ToList();
        Note("每一项都画了图标", images.Count == 5 && images.All(image => image is not null));
        Note("图标按设置的大小画", images.All(image => image!.Width == service.Settings.IconSize && image.Height == service.Settings.IconSize));
        Note("真文件的图标真的画出来了", images.Where((_, index) => !BoxItemKey.IsShell(tags[index])).All(image => image!.Source is not null));
        Note("系统项的图标真的画出来了", images.Where((_, index) => BoxItemKey.IsShell(tags[index])).All(image => image!.Source is not null));

        // Each item writes its name under the icon.
        var labels = children.Select(child => LabelOf(child)).ToList();
        Note("每一项都写了名字", labels.Count == 5 && labels.All(label => label is not null));
        var shellLabels = labels.Where((_, index) => BoxItemKey.IsShell(tags[index])).Select(label => label!.Text).ToList();
        Note("系统项的名字是它自己的名字",
            shellLabels.Contains(BoxShellItems.DisplayName("this-pc")) && shellLabels.Contains(BoxShellItems.DisplayName("recycle-bin")),
            string.Join(" | ", shellLabels));
        Note("真文件的名字是文件的名字", labels.Any(label => label!.Text == "一.txt"), string.Join(" | ", labels.Select(l => l?.Text)));

        // The grid the items stand on: one cell for each of them.
        Note("每一项都占着一格", children.All(child => child.Width == BoxLayout.ColumnWidth(service.Settings)
            && child.Height == BoxLayout.RowHeight(service.Settings)));
        Note("图标区按格宽排开", icons.ItemWidth == BoxLayout.ColumnWidth(service.Settings) && icons.ItemHeight == BoxLayout.RowHeight(service.Settings));
    }

    // ---- 落点：那条指示落点的竖线 ----

    // 一项在图标区里的位置：算期望落点时用它。
    private readonly record struct Placed(string Key, double Left, double Top, double Width, double Height)
    {
        // 这一项的中线，指针在它左边就是落在它前面。
        public double MiddleX => Left + Width / 2;
    }

    private static void CheckDropMark(BoxWindow window)
    {
        var icons = window.Icons;
        var mark = window.DropMark;
        Note("分组窗口里有一条指示落点的竖线", mark is not null);
        if (icons is null || mark is null) return;
        Note("落点没有时那条竖线是收着的", mark.Visibility == Visibility.Collapsed);

        // The line is a child of the area the icons stand in, so both are measured against the area: the line has to
        // land on the item's edge, not beside it.
        var area = (FrameworkElement)icons.Parent!;

        // Where every item sits, read off the drawing rather than off the order, so the check does not depend on
        // which items happen to come first.
        var items = icons.Children.OfType<FrameworkElement>().Select(child => new Placed(
            child.Tag as string ?? "",
            child.TranslatePoint(new Point(0, 0), area).X,
            child.TranslatePoint(new Point(0, 0), area).Y,
            child.ActualWidth, child.ActualHeight)).ToList();
        Note("图标区里有三项以上可以试落点", items.Count >= 3, $"只有 {items.Count} 项");
        if (items.Count < 3) return;

        // The items on one line: their tops sit at the same height, as in Dasktop's rows.
        var rows = new List<List<Placed>>();
        foreach (var item in items.OrderBy(item => item.Top).ThenBy(item => item.Left))
        {
            var current = rows.Count > 0 ? rows[^1] : null;
            if (current is not null && Math.Abs(current[0].Top - item.Top) <= Math.Max(4, current[0].Height * 0.25)) { current.Add(item); continue; }
            rows.Add([item]);
        }

        // The landing place is decided by the middle line of each icon, as in Dasktop: the first icon whose middle
        // the pointer has passed takes the item before it, and past the last middle the item goes after it.
        var firstRow = rows[0];
        var first = firstRow[0];
        var second = firstRow.Count > 1 ? firstRow[1] : first;

        // The pointer left of the first item's middle: the line stands before the first item.
        var insertion = window.ResolveInsertion(InIcons(icons, area, first.MiddleX - 2, first.Height / 2));
        Note("指针在第一项中线左边时，落点算出来是第一项前面",
            insertion.TargetKey == first.Key && !insertion.After, $"{insertion.TargetKey} after={insertion.After}");
        window.ShowDropMark(insertion);
        // The line is placed by a margin, so the layout has to run before where it landed can be measured.
        Pump();
        Note("指针在第一项中线左边时，竖线贴在第一项前面",
            MarkAt(mark, area, first, false),
            $"竖线在 {LineLeft(mark, area):0.0} 高 {mark.ActualHeight:0.0}，第一项在 {first.Left:0.0} 高 {first.Height:0.0}");

        // The pointer past the first item's middle but before the second one's: it lands before the second item.
        insertion = window.ResolveInsertion(InIcons(icons, area, (first.MiddleX + second.MiddleX) / 2, first.Height / 2));
        Note("指针在第一项中线之后、第二项中线之前时，落点算出来是第二项前面",
            insertion.TargetKey == second.Key && !insertion.After, $"{insertion.TargetKey} after={insertion.After}");
        window.ShowDropMark(insertion);
        Pump();
        Note("指针在第二项前面时，竖线贴在第二项前面",
            MarkAt(mark, area, second, false),
            $"竖线在 {LineLeft(mark, area):0.0}，第二项在 {second.Left:0.0}");

        // The pointer past the middle of the last item of the line: it lands after that item.
        var last = firstRow[^1];
        insertion = window.ResolveInsertion(InIcons(icons, area, last.MiddleX + 2, last.Height / 2));
        Note("指针在最后一项中线右边时，落点算出来是最后一项后面",
            insertion.TargetKey == last.Key && insertion.After, $"{insertion.TargetKey} after={insertion.After}");
        window.ShowDropMark(insertion);
        Pump();
        Note("指针在最后一项中线右边时，竖线贴在最后一项后面",
            MarkAt(mark, area, last, true),
            $"竖线在 {LineLeft(mark, area):0.0}，最后一项右边缘在 {last.Left + last.Width:0.0}");

        // The line stands level with the item it points at and is as tall as it, so it is clear which one it means.
        Note("竖线与它指的那一项同一高度", Math.Abs(mark.ActualHeight - last.Height) < 1.5
            && Math.Abs(mark.TranslatePoint(new Point(0, 0), area).Y - last.Top) < 1.5,
            $"竖线顶边 {mark.TranslatePoint(new Point(0, 0), area).Y:0.0} 高 {mark.ActualHeight:0.0}，最后一项顶边 {last.Top:0.0} 高 {last.Height:0.0}");
    }

    // 竖线是不是贴在那一项的边上，跟它一样高。
    private static bool MarkAt(Border mark, FrameworkElement area, Placed target, bool after)
    {
        if (mark.Visibility != Visibility.Visible) return false;
        var wanted = after ? target.Left + target.Width : target.Left;
        return Math.Abs(LineLeft(mark, area) - wanted) < 2.5
            && Math.Abs(mark.TranslatePoint(new Point(0, 0), area).Y - target.Top) < 1.5
            && Math.Abs(mark.ActualHeight - target.Height) < 1.5;
    }

    // 一个在这块图标区里的点：落点就是按这个坐标系算的。
    private static Point InIcons(WrapPanel icons, FrameworkElement area, double x, double y)
        => area.TranslatePoint(new Point(x, y), icons);

    // 竖线在它所在的那块区域里的左边缘。
    private static double LineLeft(Border mark, FrameworkElement area)
        => mark.TranslatePoint(new Point(0, 0), area).X;

    // 一项里的那个图标：它在图标自己的小框里，所以往下找一层。
    private static Image? ImageOf(FrameworkElement item)
    {
        if (item is not Border border) return null;
        var panel = border.Child as StackPanel;
        if (panel is null) return null;
        var host = panel.Children.OfType<Grid>().FirstOrDefault();
        return host?.Children.OfType<Image>().FirstOrDefault();
    }

    // 一项下面写的那个名字。
    private static TextBlock? LabelOf(FrameworkElement item)
    {
        if (item is not Border border) return null;
        var panel = border.Child as StackPanel;
        return panel?.Children.OfType<TextBlock>().FirstOrDefault();
    }

    // 让布局跑完，窗口里的东西都量好。
    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    // 窗口里的每一段文字。
    private static IReadOnlyList<string> Texts(DependencyObject root)
    {
        var list = new List<string>();
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock block) list.Add(block.Text);
            list.AddRange(Texts(child));
        }
        return list;
    }
}
