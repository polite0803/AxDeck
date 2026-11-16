using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AxDeck;

// 桌面页右键菜单的构建工具。菜单的样子跟面板里的菜单一样（ShelfResources.xaml），因为它们同在屏幕上。
//
// 早先这里还有分组窗口自己的菜单（打开真实文件夹、重命名、锁定、标题位置…），那些随分组一起删掉了。
internal static class BoxMenu
{
    // 一个菜单：面板的菜单样式，圆角和阴影都在里面。
    public static ContextMenu Build() => new() { Style = (Style)Application.Current.FindResource("ShelfMenu") };

    // 一行一个动作。
    public static MenuItem Item(string label, Action act, string? icon = null)
    {
        var header = new DockPanel { LastChildFill = true };
        if (icon is not null)
        {
            var glyph = Glyph(icon);
            DockPanel.SetDock(glyph, Dock.Left);
            header.Children.Add(glyph);
        }
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF1, 0xF4)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        var item = new MenuItem { Style = (Style)Application.Current.FindResource("ShelfMenuItem"), Header = header };
        AutomationProperties.SetName(item, label);
        item.Click += (_, _) => act();
        return item;
    }

    // 一条细线，把几组动作分开。
    public static Separator Separator() => new() { Style = (Style)Application.Current.FindResource("ShelfMenuSeparator") };

    // 画在 16 格上的 14 DIP 线条图标。
    private static Path Glyph(string data) => new()
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
        public const string Desktop = "M 1.5,3 H 14.5 V 11 H 1.5 Z M 5,13.5 H 11 M 8,11 V 13.5";
        // 折起来的样子：只剩上面一条标题，下面是空的。
        public const string Rollup = "M 1.5,3 H 14.5 V 6.5 H 1.5 Z M 3.5,10 H 12.5 M 3.5,13 H 12.5";
    }
}
