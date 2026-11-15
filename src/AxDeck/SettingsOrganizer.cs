using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// 设置窗口里的「桌面整理」页：规则、外观、系统项开关。
// 分组本身不再放在展开面板上（面板上的「桌面」页画的是桌面的等价物），这一页是整理器唯一摆到
// 用户面前的入口：托盘菜单、桌面页的右键菜单都从这里进。
// 数据来自 Deck 的 BoxService（存在 settings.json 的 DeckSettings.Boxes 里），改完让 Deck 保存一次。
public sealed partial class SettingsWindow
{
    // 页号：_pages 的最后一项之后。
    internal const int OrganizerSettingsPage = 16;

    // 整理器里的东西变了（Deck.BoxesChanged 里调）：这一页正开着就把三块重新列一遍。
    internal void OrganizerChanged()
    {
        if (_page == OrganizerSettingsPage) ShowPage(OrganizerSettingsPage);
    }

    private void OrganizerSettings()
    {
        var boxes = _app.Deck.Boxes;
        var settings = boxes.Settings;

        // ---- 分组 ----
        SectionTitle(Loc.T("分组", "Boxes"));
        var boxRows = new List<FrameworkElement>();
        foreach (var box in boxes.Boxes)
        {
            var row = new Grid { MinHeight = 44 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = SettingLabel(box.Title, () => Loc.T("位置：", "Location: ") + boxes.CollectionRoot);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 0);
            row.Children.Add(name);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var count = UI.Fixed(boxes.ItemKeys(box.Id).Count.ToString(), 12, 18, UI.Tertiary);
            count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(0, 0, 10, 0);
            actions.Children.Add(count);
            actions.Children.Add(ActionButtonRow(Loc.T("显示", "Show"), () => _app.Deck.BoxesShow(box.Id)));
            actions.Children.Add(ActionButtonRow(Loc.T("删除", "Delete"), () => _app.Deck.BoxesDelete(box.Id)));
            Grid.SetColumn(actions, 1);
            row.Children.Add(actions);
            boxRows.Add(row);
        }
        if (boxRows.Count == 0)
            boxRows.Add(Hint(Loc.T("还没有分组。从桌面页把文件拖进面板上的「新建分组」，或用托盘菜单建一个。",
                "No boxes yet. Drag files into a new box from the desktop page, or make one from the tray menu.")));
        boxRows.Add(ActionButtonRow(Loc.T("新建分组", "New box"), () => _app.Deck.BoxesCreate()));
        boxRows.Add(ActionButtonRow(Loc.T("显示所有分组", "Show all boxes"), () => _app.Deck.BoxesShowAll()));
        Group([.. boxRows]);

        // ---- 规则 ----
        SectionTitle(Loc.T("规则", "Rules"));
        Group(
            MusicChoice(Loc.T("拖进分组时", "Dropped into a box"), settings.Drop,
                [(BoxDropAction.Copy, Loc.T("复制文件", "Copy the files")), (BoxDropAction.Move, Loc.T("移动文件", "Move the files")), (BoxDropAction.Map, Loc.T("只留快捷方式（映射）", "Keep a shortcut (map)"))],
                value => DeckBoxesSetPolicy("drop", BoxPolicy.Code(value))),
            MusicChoice(Loc.T("拖出分组时", "Dragged out of a box"), settings.DragOut,
                [(BoxDropAction.Copy, Loc.T("复制到桌面", "Copy to the desktop")), (BoxDropAction.Move, Loc.T("移动到桌面", "Move to the desktop")), (BoxDropAction.Map, Loc.T("留快捷方式", "Keep a shortcut"))],
                value => DeckBoxesSetPolicy("dragOut", BoxPolicy.Code(value))),
            MusicChoice(Loc.T("遇到同名文件时", "When a file of the same name is there"), settings.Conflict,
                [(BoxConflictPolicy.Rename, Loc.T("自动改名", "Rename it")), (BoxConflictPolicy.Skip, Loc.T("跳过", "Skip it")), (BoxConflictPolicy.Replace, Loc.T("替换旧的", "Replace the old one"))],
                value => DeckBoxesSetPolicy("conflict", BoxPolicy.Code(value))),
            MusicChoice(Loc.T("删除分组时", "When a box is deleted"), settings.Delete,
                [(BoxDeletePolicy.MoveContentsToDesktop, Loc.T("内容移回桌面", "Move the contents to the desktop")), (BoxDeletePolicy.KeepFolder, Loc.T("保留文件夹", "Keep the folder")), (BoxDeletePolicy.RecycleFolder, Loc.T("文件夹进回收站", "Send the folder to the recycle bin"))],
                value => DeckBoxesSetPolicy("delete", BoxPolicy.Code(value))),
            MusicChoice(Loc.T("图标下的名字", "The name under an icon"), settings.NameDisplay,
                [(BoxNameDisplay.Full, Loc.T("显示全部", "Show it in full")), (BoxNameDisplay.HideShortcutExtension, Loc.T("快捷方式不写 .lnk", "Hide .lnk on shortcuts")), (BoxNameDisplay.HideAllExtensions, Loc.T("不写任何扩展名", "Hide every extension"))],
                value => DeckBoxesSetPolicy("names", BoxPolicy.Code(value)))
        );

        // ---- 外观 ----
        SectionTitle(Loc.T("外观", "Look"));
        Group(
            SliderRow(Loc.T("图标大小", "Icon size"), settings.IconSize, 24, 96, 1, "px", value => DeckBoxesSetLook("iconSize", (int)value)),
            SliderRow(Loc.T("名字字号", "Label size"), settings.LabelTextSize, 9, 20, 1, "px", value => DeckBoxesSetLook("labelTextSize", (int)value)),
            SliderRow(Loc.T("图标横向间距", "Icon gap across"), settings.IconGapX, 0, 24, 1, "px", value => DeckBoxesSetLook("iconGapX", (int)value)),
            SliderRow(Loc.T("图标纵向间距", "Icon gap down"), settings.IconGapY, 0, 24, 1, "px", value => DeckBoxesSetLook("iconGapY", (int)value)),
            SliderRow(Loc.T("圆角", "Corner radius"), settings.CornerRadius, 0, 24, 1, "px", value => DeckBoxesSetLook("cornerRadius", (int)value)),
            SliderRow(Loc.T("底色不透明度", "Background opacity"), settings.BackgroundOpacity, 0, 100, 1, "%", value => DeckBoxesSetLook("backgroundOpacity", (int)value))
        );

        // ---- 开关 ----
        SectionTitle(Loc.T("开关", "Switches"));
        Group(
            Toggle(Loc.T("双击打开项目", "Open an item with a double click"), settings.DoubleClickOpen, value => DeckBoxesSetFlag("doubleClickOpen", value)),
            Toggle(Loc.T("显示图标下的名字", "Show the name under an icon"), settings.ShowItemLabels, value => DeckBoxesSetFlag("showItemLabels", value)),
            Toggle(Loc.T("快捷方式带小箭头", "Mark shortcuts with an arrow"), settings.ShowShortcutArrow, value => DeckBoxesSetFlag("showShortcutArrow", value)),
            Toggle(Loc.T("分组吸附屏幕边缘", "Snap a box to the screen edge"), settings.SnapToEdges, value => DeckBoxesSetFlag("snapToEdges", value)),
            Toggle(Loc.T("收进分组后隐藏原生桌面图标", "Hide the native icon once it is in a box"), settings.AutoHideShellIcons, value => DeckBoxesSetFlag("autoHideShellIcons", value))
        );

        // ---- Windows 自己画的桌面图标 ----
        SectionTitle(Loc.T("桌面图标", "Desktop icons"));
        var iconRows = new List<FrameworkElement>();
        foreach (var item in Core.BoxShellItems.All)
        {
            // 只有桌面上真的有的图标才有开关；其它系统项没有自己的桌面图标。
            if (item.DesktopIconClsid is null || Core.BoxShellItems.FindDesktopIcon(item.Id) is null) continue;
            iconRows.Add(Toggle(item.Name, boxes.ShellIconVisible(item.Id), value => DeckBoxesSetShellIcon(item.Id, value)));
        }
        if (iconRows.Count > 0) Group([.. iconRows]);
    }

    // 改一条规则：整理器的数据在 Deck 那边，改完由 BoxesChanged 保存并让这一页重画。
    private void DeckBoxesSetPolicy(string which, string code) => _app.Deck.BoxesSetPolicy(which, code);
    private void DeckBoxesSetLook(string which, int value) => _app.Deck.BoxesSetLook(which, value);
    private void DeckBoxesSetFlag(string which, bool value) => _app.Deck.BoxesSetFlag(which, value);
    private void DeckBoxesSetShellIcon(string shellId, bool visible) => _app.Deck.BoxesSetShellIcon(shellId, visible);

    // 设置窗口里的小按钮，跟面板页的那些一个样子。
    private Button ActionButtonRow(string label, Action onClick)
    {
        var button = new Button
        {
            Style = (Style)FindResource("SegmentButton"),
            Content = UI.Fixed(label, 11.5, 18, UI.Secondary),
            Padding = new Thickness(8, 3, 8, 3)
        };
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) => onClick();
        return button;
    }

    // 一行说明文字。
    private TextBlock Hint(string text)
    {
        var hint = UI.Fixed(text, 12, 18, UI.Tertiary);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 4, 0, 4);
        return hint;
    }
}
