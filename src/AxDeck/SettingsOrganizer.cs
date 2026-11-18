using System.Windows;
using System.Windows.Controls;
using AxDeck.Core;

namespace AxDeck;

// 设置窗口里的「桌面页」：只留下桌面页真在用的那些设置——图标下的名字怎么写、拖进来遇到同名怎么办、
// 图标多大、间距多少，以及 Windows 自己那几枚桌面图标的显示开关。
//
// 早先这一页底下还有「分组」「规则」「开关」三块，都是给独立的平铺分组窗口用的。分组整套删掉之后，
// 那三块里的多数项已经没有去处了，只剩这里留下的这些。
// 数据来自 Deck 的 BoxService（存在 settings.json 的 DeckSettings.Boxes 里），改完让 Deck 保存一次。
public sealed partial class SettingsWindow
{
    // 页号：_pages 的最后一项。它排在 PageOrder 里 About 的前面。
    internal const int OrganizerSettingsPage = 16;

    // 数据变了（Deck.BoxesChanged 里调）：这一页正开着就重新列一遍。
    internal void OrganizerChanged()
    {
        if (_page == OrganizerSettingsPage) ShowPage(OrganizerSettingsPage);
    }

    private void OrganizerSettings()
    {
        var boxes = _app.Deck.Boxes;
        var settings = boxes.Settings;

        // ---- 规则 ----
        SectionTitle(Loc.T("规则", "Rules"));
        Group(
            MusicChoice(Loc.T("遇到同名文件时", "When a file of the same name is there"), settings.Conflict,
                [(BoxConflictPolicy.Rename, Loc.T("自动改名", "Rename it")), (BoxConflictPolicy.Skip, Loc.T("跳过", "Skip it")), (BoxConflictPolicy.Replace, Loc.T("替换旧的", "Replace the old one"))],
                value => DeckBoxesSetPolicy("conflict", DesktopLookCodes.Code(value))),
            MusicChoice(Loc.T("图标下的名字", "The name under an icon"), settings.NameDisplay,
                [(BoxNameDisplay.Full, Loc.T("显示全部", "Show it in full")), (BoxNameDisplay.HideShortcutExtension, Loc.T("快捷方式不写 .lnk", "Hide .lnk on shortcuts")), (BoxNameDisplay.HideAllExtensions, Loc.T("不写任何扩展名", "Hide every extension"))],
                value => DeckBoxesSetPolicy("names", DesktopLookCodes.Code(value)))
        );

        // ---- 外观 ----
        SectionTitle(Loc.T("外观", "Look"));
        Group(
            SliderRow(Loc.T("图标大小", "Icon size"), settings.IconSize, 24, 96, 1, "px", value => DeckBoxesSetLook("iconSize", (int)value)),
            SliderRow(Loc.T("名字字号", "Label size"), settings.LabelTextSize, 9, 20, 1, "px", value => DeckBoxesSetLook("labelTextSize", (int)value)),
            SliderRow(Loc.T("图标横向间距", "Icon gap across"), settings.IconGapX, 0, 24, 1, "px", value => DeckBoxesSetLook("iconGapX", (int)value)),
            SliderRow(Loc.T("图标纵向间距", "Icon gap down"), settings.IconGapY, 0, 24, 1, "px", value => DeckBoxesSetLook("iconGapY", (int)value)),
            SliderRow(Loc.T("圆角", "Corner radius"), settings.CornerRadius, 0, 24, 1, "px", value => DeckBoxesSetLook("cornerRadius", (int)value))
        );

        // ---- 开关 ----
        SectionTitle(Loc.T("开关", "Switches"));
        Group(
            Toggle(Loc.T("显示图标下的名字", "Show the name under an icon"), settings.ShowItemLabels, value => DeckBoxesSetFlag("showItemLabels", value)),
            Toggle(Loc.T("快捷方式带小箭头", "Mark shortcuts with an arrow"), settings.ShowShortcutArrow, value => DeckBoxesSetFlag("showShortcutArrow", value))
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

    // 改一条规则：数据在 Deck 那边，改完由 BoxesChanged 保存并让这一页重画。
    private void DeckBoxesSetPolicy(string which, string code) => _app.Deck.BoxesSetPolicy(which, code);
    private void DeckBoxesSetLook(string which, int value) => _app.Deck.BoxesSetLook(which, value);
    private void DeckBoxesSetFlag(string which, bool value) => _app.Deck.BoxesSetFlag(which, value);
    private void DeckBoxesSetShellIcon(string shellId, bool visible) => _app.Deck.BoxesSetShellIcon(shellId, visible);
}
