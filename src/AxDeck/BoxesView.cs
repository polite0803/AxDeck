using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// 「桌面整理」页面：分组列表、规则、原生桌面图标。
// 页面只画东西，动作都回给控制器（BoxesPanel.cs）和整理器（BoxService）。
internal sealed class BoxesView : Grid
{
    private readonly MainWindow _owner;
    private readonly BoxService _boxes;
    private readonly StackPanel _list = new();
    private readonly StackPanel _rules = new();
    private readonly StackPanel _icons = new();
    private readonly TextBlock _status = UI.Text("", 12, UI.Tertiary);

    public BoxesView(MainWindow owner, BoxService boxes)
    {
        _owner = owner;
        _boxes = boxes;
        Margin = new Thickness(2, 2, 2, 2);
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new StackPanel { Margin = new Thickness(0, 4, 6, 4) };

        // 分组：列表加一个「新建分组」。
        var boxHead = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        boxHead.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        boxHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        boxHead.Children.Add(Section(Loc.T("分组", "Boxes")));
        var newBox = ActionButton(Loc.T("新建分组", "New box"), () => _owner.BoxesCreate());
        Grid.SetColumn(newBox, 1);
        boxHead.Children.Add(newBox);
        content.Children.Add(boxHead);
        content.Children.Add(_list);

        // 规则：文件拖进拖出、同名怎么办、删分组时文件去哪、名字怎么显示。
        content.Children.Add(Section(Loc.T("规则", "Rules")));
        content.Children.Add(_rules);

        // 原生桌面图标：收进分组后，Windows 自己桌面上那个还在不在。
        content.Children.Add(Section(Loc.T("桌面图标", "Desktop icons")));
        content.Children.Add(_icons);

        scroll.Content = content;
        Children.Add(scroll);

        _status.Margin = new Thickness(0, 6, 0, 0);
        _status.TextWrapping = TextWrapping.Wrap;
        var bar = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(_status);
        Grid.SetRow(bar, 1);
        Children.Add(bar);
    }

    // 页面重画一次：分组、规则、图标三块都重新列。
    public void Refresh()
    {
        BuildList();
        BuildRules();
        BuildIcons();
    }

    // ---- 分组 ----

    private void BuildList()
    {
        _list.Children.Clear();
        if (_boxes.Boxes.Count == 0)
        {
            var empty = UI.Fixed(Loc.T("还没有分组。点「新建分组」在桌面上开一块地方，把文件拖进去。",
                "No boxes yet. Use New box to open a place on the desktop and drop files into it."),
                12, 18, UI.Tertiary);
            empty.TextWrapping = TextWrapping.Wrap;
            _list.Children.Add(empty);
            return;
        }
        foreach (var box in _boxes.Boxes)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var open = ActionButton(Loc.T("显示", "Show"), () => _owner.BoxesShow(box.Id), small: true);
            Grid.SetColumn(open, 0);
            grid.Children.Add(open);

            var name = UI.Fixed(box.Title, 12.5, 18, UI.Primary);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(8, 0, 8, 0);
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);

            var count = _boxes.ItemKeys(box.Id).Count;
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(UI.Fixed(count.ToString(), 11.5, 18, UI.Tertiary));
            actions.Children.Add(ActionButton(Loc.T("删除", "Delete"), () => _owner.BoxesDelete(box.Id), small: true));
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
            _list.Children.Add(grid);
        }
    }

    // ---- 规则 ----

    private void BuildRules()
    {
        _rules.Children.Clear();
        var settings = _boxes.Settings;

        // 拖进分组：复制、移动还是只留个引用。
        var drop = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        drop.Children.Add(UI.Fixed(Loc.T("拖进分组时", "Dropped into a box"), 12, 18, UI.Secondary));
        drop.Children.Add(Choice(Loc.T("复制文件", "Copy the files"), settings.Drop == BoxDropAction.Copy, () => _owner.BoxesSetPolicy("drop", BoxPolicyCodes.Copy)));
        drop.Children.Add(Choice(Loc.T("移动文件", "Move the files"), settings.Drop == BoxDropAction.Move, () => _owner.BoxesSetPolicy("drop", BoxPolicyCodes.Move)));
        drop.Children.Add(Choice(Loc.T("只留快捷方式（映射）", "Keep a shortcut (map)"), settings.Drop == BoxDropAction.Map, () => _owner.BoxesSetPolicy("drop", BoxPolicyCodes.Map)));
        _rules.Children.Add(drop);

        // 拖出分组：同样的三个。
        var outDrop = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        outDrop.Children.Add(UI.Fixed(Loc.T("拖出分组时", "Dragged out of a box"), 12, 18, UI.Secondary));
        outDrop.Children.Add(Choice(Loc.T("复制到桌面", "Copy to the desktop"), settings.DragOut == BoxDropAction.Copy, () => _owner.BoxesSetPolicy("dragOut", BoxPolicyCodes.Copy)));
        outDrop.Children.Add(Choice(Loc.T("移动到桌面", "Move to the desktop"), settings.DragOut == BoxDropAction.Move, () => _owner.BoxesSetPolicy("dragOut", BoxPolicyCodes.Move)));
        outDrop.Children.Add(Choice(Loc.T("留快捷方式", "Keep a shortcut"), settings.DragOut == BoxDropAction.Map, () => _owner.BoxesSetPolicy("dragOut", BoxPolicyCodes.Map)));
        _rules.Children.Add(outDrop);

        // 同名：改名、跳过还是替换。
        var conflict = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        conflict.Children.Add(UI.Fixed(Loc.T("遇到同名文件时", "When a file of the same name is there"), 12, 18, UI.Secondary));
        conflict.Children.Add(Choice(Loc.T("自动改名", "Rename it"), settings.Conflict == BoxConflictPolicy.Rename, () => _owner.BoxesSetPolicy("conflict", BoxPolicyCodes.Rename)));
        conflict.Children.Add(Choice(Loc.T("跳过", "Skip it"), settings.Conflict == BoxConflictPolicy.Skip, () => _owner.BoxesSetPolicy("conflict", BoxPolicyCodes.Skip)));
        conflict.Children.Add(Choice(Loc.T("替换旧的", "Replace the old one"), settings.Conflict == BoxConflictPolicy.Replace, () => _owner.BoxesSetPolicy("conflict", BoxPolicyCodes.Replace)));
        _rules.Children.Add(conflict);

        // 删分组时，真实文件夹里的文件去哪。
        var delete = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        delete.Children.Add(UI.Fixed(Loc.T("删除分组时", "When a box is deleted"), 12, 18, UI.Secondary));
        delete.Children.Add(Choice(Loc.T("内容移回桌面", "Move the contents to the desktop"), settings.Delete == BoxDeletePolicy.MoveContentsToDesktop, () => _owner.BoxesSetPolicy("delete", BoxPolicyCodes.MoveContentsToDesktop)));
        delete.Children.Add(Choice(Loc.T("保留文件夹", "Keep the folder"), settings.Delete == BoxDeletePolicy.KeepFolder, () => _owner.BoxesSetPolicy("delete", BoxPolicyCodes.KeepFolder)));
        delete.Children.Add(Choice(Loc.T("文件夹进回收站", "Send the folder to the recycle bin"), settings.Delete == BoxDeletePolicy.RecycleFolder, () => _owner.BoxesSetPolicy("delete", BoxPolicyCodes.RecycleFolder)));
        _rules.Children.Add(delete);

        // 名字怎么显示。
        var names = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        names.Children.Add(UI.Fixed(Loc.T("图标下的名字", "The name under an icon"), 12, 18, UI.Secondary));
        names.Children.Add(Choice(Loc.T("显示全部", "Show it in full"), settings.NameDisplay == BoxNameDisplay.Full, () => _owner.BoxesSetPolicy("names", BoxPolicyCodes.Full)));
        names.Children.Add(Choice(Loc.T("快捷方式不写 .lnk", "Hide .lnk on shortcuts"), settings.NameDisplay == BoxNameDisplay.HideShortcutExtension, () => _owner.BoxesSetPolicy("names", BoxPolicyCodes.HideShortcutExtension)));
        names.Children.Add(Choice(Loc.T("不写任何扩展名", "Hide every extension"), settings.NameDisplay == BoxNameDisplay.HideAllExtensions, () => _owner.BoxesSetPolicy("names", BoxPolicyCodes.HideAllExtensions)));
        _rules.Children.Add(names);

        // 外观：图标大小、间距、圆角、底色。
        var look = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
        look.Children.Add(UI.Fixed(Loc.T("外观", "Look"), 12, 18, UI.Secondary));
        look.Children.Add(Slider(Loc.T("图标大小", "Icon size"), settings.IconSize, 24, 96, value => _owner.BoxesSetLook("iconSize", value)));
        look.Children.Add(Slider(Loc.T("名字字号", "Label size"), settings.LabelTextSize, 9, 20, value => _owner.BoxesSetLook("labelTextSize", value)));
        look.Children.Add(Slider(Loc.T("图标间距", "Icon gap"), settings.IconGapX, 0, 24, value => _owner.BoxesSetLook("iconGapX", value)));
        look.Children.Add(Slider(Loc.T("圆角", "Corner radius"), settings.CornerRadius, 0, 24, value => _owner.BoxesSetLook("cornerRadius", value)));
        look.Children.Add(Slider(Loc.T("底色不透明度", "Background opacity"), settings.BackgroundOpacity, 0, 100, value => _owner.BoxesSetLook("backgroundOpacity", value)));
        _rules.Children.Add(look);

        // 开关：单击打开、显示名字、边缘吸附、隐藏原生图标。
        var switches = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
        switches.Children.Add(Switch(Loc.T("双击打开项目", "Open an item with a double click"), settings.DoubleClickOpen, value => _owner.BoxesSetFlag("doubleClickOpen", value)));
        switches.Children.Add(Switch(Loc.T("显示图标下的名字", "Show the name under an icon"), settings.ShowItemLabels, value => _owner.BoxesSetFlag("showItemLabels", value)));
        switches.Children.Add(Switch(Loc.T("快捷方式带小箭头", "Mark shortcuts with an arrow"), settings.ShowShortcutArrow, value => _owner.BoxesSetFlag("showShortcutArrow", value)));
        switches.Children.Add(Switch(Loc.T("分组吸附屏幕边缘", "Snap a box to the screen edge"), settings.SnapToEdges, value => _owner.BoxesSetFlag("snapToEdges", value)));
        switches.Children.Add(Switch(Loc.T("收进分组后隐藏原生桌面图标", "Hide the native icon once it is in a box"), settings.AutoHideShellIcons, value => _owner.BoxesSetFlag("autoHideShellIcons", value)));
        _rules.Children.Add(switches);
    }

    // ---- 原生桌面图标 ----

    private void BuildIcons()
    {
        _icons.Children.Clear();
        foreach (var item in BoxShellItems.All)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new ColumnDefinition().Width });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = UI.Fixed(item.Name, 12.5, 18, UI.Primary);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            row.Children.Add(name);

            // 只有桌面上真的有的图标才有开关；其它系统项没有自己的桌面图标。
            if (item.DesktopIconClsid is { } && BoxShellItems.FindDesktopIcon(item.Id) is not null)
            {
                bool visible = _boxes.ShellIconVisible(item.Id);
                // 开关用自己的默认样式：SegmentButton 是给 Button 的，套在 CheckBox 上会当场抛异常。
                var toggle = new CheckBox
                {
                    IsChecked = visible,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                AutomationProperties.SetName(toggle, item.Name);
                toggle.Checked += (_, _) => _owner.BoxesSetShellIcon(item.Id, true);
                toggle.Unchecked += (_, _) => _owner.BoxesSetShellIcon(item.Id, false);
                Grid.SetColumn(toggle, 2);
                row.Children.Add(toggle);
            }
            _icons.Children.Add(row);
        }
    }

    // ---- 零件 ----

    // 一个选项，左边是勾，选中的打勾。
    private static Grid Choice(string label, bool chosen, Action act)
    {
        var grid = new Grid { Margin = new Thickness(16, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var mark = new Path
        {
            Data = Geometry.Parse("M 1.5,5.2 L 4,7.7 L 8.5,2.5"),
            Stroke = UI.Brush(UI.Primary),
            StrokeThickness = 1.6,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 10,
            Height = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = chosen ? Visibility.Visible : Visibility.Hidden
        };
        grid.Children.Add(mark);
        var text = UI.Fixed(label, 12.5, 18, UI.Secondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var button = new Button { Background = Brushes.Transparent, Padding = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand, Focusable = false };
        button.Content = grid;
        button.Click += (_, _) => act();
        return Wrap(button);
    }

    // 一个开关：右边是滑块，左边是说明。
    private static Grid Switch(string label, bool on, Action<bool> act)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = UI.Fixed(label, 12.5, 18, UI.Secondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);
        var toggle = new CheckBox { IsChecked = on, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(toggle, label);
        toggle.Checked += (_, _) => act(true);
        toggle.Unchecked += (_, _) => act(false);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(toggle);
        return grid;
    }

    // 一根滑块：左边是名字，右边是滑块和它的数字。
    private static Grid Slider(string label, int value, int min, int max, Action<int> act)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        var text = UI.Fixed(label, 12.5, 18, UI.Secondary);
        text.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text);

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
            Width = 144
        };
        var readout = UI.Fixed(value.ToString(), 12, 18, UI.Tertiary);
        readout.VerticalAlignment = VerticalAlignment.Center;
        readout.TextAlignment = TextAlignment.Right;
        slider.ValueChanged += (_, _) =>
        {
            int now = (int)Math.Round(slider.Value);
            readout.Text = now.ToString();
            act(now);
        };
        Grid.SetColumn(slider, 1);
        grid.Children.Add(slider);
        Grid.SetColumn(readout, 2);
        grid.Children.Add(readout);
        return grid;
    }

    // 一个按钮，跟服务页的那些一样。
    private static Button ActionButton(string label, Action onClick, bool small = false)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.FindResource("SegmentButton"),
            Content = UI.Fixed(label, small ? 11.5 : 12.5, 18, UI.Secondary),
            Padding = new Thickness(small ? 8 : 10, 3, small ? 8 : 10, 3),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    private static TextBlock Section(string text) => UI.Fixed(text, 13, 20, UI.Tertiary, FontWeights.SemiBold);

    // 一个包住按钮的格子，好让整行都能点。
    private static Grid Wrap(FrameworkElement child)
    {
        var grid = new Grid();
        grid.Children.Add(child);
        return grid;
    }
}
