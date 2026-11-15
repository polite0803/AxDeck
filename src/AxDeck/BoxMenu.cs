using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using AxDeck.Core;

namespace AxDeck;

// 分组自己的右键菜单：打开真实文件夹、重命名、锁定、标题位置、闲置可见度、删除。
// 菜单的样子跟面板里的菜单一样（ShelfResources.xaml），因为它们同在屏幕上。
internal static class BoxMenu
{
    // 分组自己的菜单。
    public static void Show(Window owner, BoxService boxes, DesktopBox box, Action rebuild)
    {
        var menu = Build();
        menu.Items.Add(Item(Loc.T("打开真实文件夹", "Open the real folder"), () =>
        {
            try { Native.BoxShell.Open(box.FolderPath); }
            catch (Exception ex) { BoxReport.Error(Loc.T("无法打开文件夹：", "The folder could not be opened: ") + ex.Message); }
        }, MenuIcons.Folder));
        menu.Items.Add(Item(Loc.T("重命名分组", "Rename the box"), () => BoxDialogs.Rename(owner, boxes, box, rebuild), MenuIcons.Edit));
        menu.Items.Add(Item(box.Locked ? Loc.T("解除锁定", "Unlock") : Loc.T("锁定位置", "Lock the place"), () =>
        {
            box.Locked = !box.Locked;
            boxes.Save(box);
            rebuild();
        }, box.Locked ? MenuIcons.Unlock : MenuIcons.Lock, check: box.Locked));
        menu.Items.Add(Separator());

        // 标题在上还是在下：两个选项里打勾的那个是当前的。
        var position = Submenu(Loc.T("标题位置", "Title position"), MenuIcons.Title, new[]
        {
            Choice(Loc.T("上方", "Top"), box.TitlePosition == BoxTitlePosition.Top, () =>
            {
                box.TitlePosition = BoxTitlePosition.Top;
                boxes.Save(box);
                rebuild();
            }),
            Choice(Loc.T("下方", "Bottom"), box.TitlePosition == BoxTitlePosition.Bottom, () =>
            {
                box.TitlePosition = BoxTitlePosition.Bottom;
                boxes.Save(box);
                rebuild();
            })
        });
        menu.Items.Add(position);

        menu.Items.Add(Item(Loc.T("闲置时可见度", "Opacity when idle"), () => BoxDialogs.Opacity(owner, boxes, box, rebuild),
            MenuIcons.Eye, hint: box.TitleOpacity + "%"));
        // 自动收起：开着的时候鼠标一离开，这个分组就折成只剩标题，鼠标回到标题上它又展开。
        // 原版的菜单里就是这一个开关（useBoxContextMenuActions.ts 的 updateBoxAutoCollapseFromMenu）。
        menu.Items.Add(Item(box.Collapsed ? Loc.T("自动收起：开启", "Auto collapse: on") : Loc.T("自动收起：关闭", "Auto collapse: off"), () =>
        {
            box.Collapsed = !box.Collapsed;
            boxes.Save(box);
            rebuild();
        }, MenuIcons.Rollup, check: box.Collapsed));
        menu.Items.Add(Separator());
        menu.Items.Add(Item(Loc.T("删除分组", "Delete the box"), () => BoxDialogs.Delete(owner, boxes, box), MenuIcons.Delete, danger: true));
        Open(owner, menu);
    }

    // ---- 共享的构建 ----

    // 一个菜单：面板的菜单样式，圆角和阴影都在里面。
    public static ContextMenu Build() => new() { Style = (Style)Application.Current.FindResource("ShelfMenu") };

    // 一行一个动作。check 有值时左边是一个勾，选中的那一行打勾。
    public static MenuItem Item(string label, Action act, string? icon = null, bool danger = false, bool? check = null,
        string? hint = null, bool enabled = true)
    {
        var header = new DockPanel { LastChildFill = true };
        void Lead(FrameworkElement part) { DockPanel.SetDock(part, Dock.Left); header.Children.Add(part); }
        if (check is { } chosen) Lead(Check(chosen));
        if (icon is not null) Lead(Glyph(icon, danger ? "#F0A6AA" : "#A5A5AF"));
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var item = new MenuItem
        {
            Style = (Style)Application.Current.FindResource("ShelfMenuItem"),
            Header = header,
            InputGestureText = hint ?? "",
            IsEnabled = enabled
        };
        AutomationProperties.SetName(item, label);
        if (check == true) AutomationProperties.SetItemStatus(item, Loc.T("已选中", "Selected"));
        item.Click += (_, _) => act();
        return item;
    }

    // 一个带更多选择的行：鼠标移到上面就展开，不用等系统的菜单延迟。
    public static MenuItem Submenu(string label, string icon, IEnumerable<object> items)
    {
        var header = new DockPanel { LastChildFill = true };
        var glyph = Glyph(icon, "#A5A5AF");
        DockPanel.SetDock(glyph, Dock.Left);
        header.Children.Add(glyph);
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var item = new MenuItem { Style = (Style)Application.Current.FindResource("ShelfMenuItem"), Header = header };
        AutomationProperties.SetName(item, label);
        foreach (var child in items) item.Items.Add(child);
        item.MouseEnter += (_, _) => { if (!item.IsSubmenuOpen) item.IsSubmenuOpen = true; };
        return item;
    }

    // 一个选项：左边是勾，选中的打勾。
    public static MenuItem Choice(string label, bool chosen, Action act)
    {
        var header = new DockPanel { LastChildFill = true };
        var check = Check(chosen);
        DockPanel.SetDock(check, Dock.Left);
        header.Children.Add(check);
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var item = new MenuItem { Style = (Style)Application.Current.FindResource("ShelfMenuItem"), Header = header };
        AutomationProperties.SetName(item, label);
        if (chosen) AutomationProperties.SetItemStatus(item, Loc.T("已选中", "Selected"));
        item.Click += (_, _) => act();
        return item;
    }

    // 一条细线，把几组动作分开。
    public static Separator Separator() => new() { Style = (Style)Application.Current.FindResource("ShelfMenuSeparator") };

    // 左边的那个勾。
    private static Path Check(bool chosen) => new()
    {
        Data = Geometry.Parse("M 1.5,5.2 L 4,7.7 L 8.5,2.5"),
        Stroke = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF4)),
        StrokeThickness = 1.6,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 10,
        Height = 10,
        Margin = new Thickness(1, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center,
        Visibility = chosen ? Visibility.Visible : Visibility.Hidden
    };

    // 画在 16 格上的 14 DIP 线条图标。
    private static Path Glyph(string data, string color) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = new SolidColorBrush(Color.FromRgb(0xA5, 0xA5, 0xAF)),
        StrokeThickness = 1.4,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 16,
        Height = 16,
        LayoutTransform = new ScaleTransform(.875, .875),
        Margin = new Thickness(0, 0, 8, 0),
        VerticalAlignment = VerticalAlignment.Center
    };

    // 菜单在鼠标所在的地方打开。
    public static void Open(Window owner, ContextMenu menu)
    {
        menu.PlacementTarget = owner;
        menu.Placement = PlacementMode.MousePoint;
        menu.HorizontalOffset = -12;
        menu.VerticalOffset = -12;
        menu.IsOpen = true;
    }

    // 菜单里那些线条图标的几何。
    internal static class MenuIcons
    {
        public const string Folder = "M 2,4.5 A 1,1 0 0 1 3,3.5 H 6.2 L 7.7,5 H 13 A 1,1 0 0 1 14,6 V 12 A 1,1 0 0 1 13,13 H 3 A 1,1 0 0 1 2,12 Z";
        public const string Edit = "M 10.5,2.5 L 13.5,5.5 L 6,13 H 3 V 10 Z";
        public const string Delete = "M 2.5,4.5 H 13.5 M 6,4.5 V 3 H 10 V 4.5 M 4,4.5 L 4.7,13.5 H 11.3 L 12,4.5";
        public const string Lock = "M 4,7 H 12 V 13.5 H 4 Z M 5.5,7 V 5 A 2.5,2.5 0 0 1 10.5,5 V 7";
        public const string Unlock = "M 4,7 H 12 V 13.5 H 4 Z M 5.5,7 V 5 A 2.5,2.5 0 0 1 10.4,4.2";
        public const string Eye = "M 1.5,8 C 4,4.5 12,4.5 14.5,8 C 12,11.5 4,11.5 1.5,8 Z M 8,10 A 2,2 0 1 1 8,6 A 2,2 0 1 1 8,10";
        public const string Title = "M 1.5,2.5 H 14.5 V 5.5 H 1.5 Z M 1.5,10.5 H 14.5 V 13.5 H 1.5 Z";
        public const string Open = "M 9,2.5 H 13.5 V 7 M 13.5,2.5 L 7.5,8.5 M 11.5,9.5 V 12.5 A 1,1 0 0 1 10.5,13.5 H 3.5 A 1,1 0 0 1 2.5,12.5 V 5.5 A 1,1 0 0 1 3.5,4.5 H 6.5";
        public const string Remove = "M 2.5,8 A 5.5,5.5 0 1 1 13.5,8 A 5.5,5.5 0 1 1 2.5,8 Z M 5.5,8 H 10.5";
        public const string Desktop = "M 1.5,3 H 14.5 V 11 H 1.5 Z M 5,13.5 H 11 M 8,11 V 13.5";
        // 折起来的样子：只剩上面一条标题，下面是空的。
        public const string Rollup = "M 1.5,3 H 14.5 V 6.5 H 1.5 Z M 3.5,10 H 12.5 M 3.5,13 H 12.5";
    }
}
