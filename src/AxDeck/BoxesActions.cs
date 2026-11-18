using AxDeck.Core;

namespace AxDeck;

// 设置窗口里「桌面页」那一页的动作：改外观、改名字怎么写、改同名怎么办、开关 Windows 自己的桌面图标。
// 这些动作都改 BoxService 里的数据，改完保存一次，页面自己重画。
//
// 分组的那些动作（新建、显示、删除、显示所有）随分组窗口一起删掉了。
public partial class MainWindow
{
    // 改一条规则。which 是 conflict（同名文件怎么办）或 names（图标下的名字怎么写）。
    internal void BoxesSetPolicy(string which, string code)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "conflict": settings.ConflictPolicy = code; break;
            case "names": settings.NameDisplayMode = code; break;
        }
        BoxesChanged();
    }

    // 改一项外观：图标大小、名字字号、横竖两种间距、圆角。
    internal void BoxesSetLook(string which, int value)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "iconSize": settings.IconSize = Math.Clamp(value, 24, 96); break;
            case "labelTextSize": settings.LabelTextSize = Math.Clamp(value, 9, 20); break;
            case "iconGapX": settings.IconGapX = Math.Clamp(value, 0, 24); break;
            case "iconGapY": settings.IconGapY = Math.Clamp(value, 0, 24); break;
            case "cornerRadius": settings.CornerRadius = Math.Clamp(value, 0, 24); break;
        }
        BoxesChanged();
    }

    // 开或关一个开关。
    internal void BoxesSetFlag(string which, bool value)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "showItemLabels": settings.ShowItemLabels = value; break;
            case "showShortcutArrow": settings.ShowShortcutArrow = value; break;
        }
        BoxesChanged();
    }

    // 显不显示 Windows 自己桌面上那个系统图标。
    internal void BoxesSetShellIcon(string shellId, bool visible)
    {
        Boxes.SetShellIconVisible(shellId, visible);
        BoxesChanged();
    }

    // 打开「桌面」页的设置页（沿用原来的页号 OrganizerSettingsPage）：桌面页的右键菜单点它的时候用。
    internal void BoxesOpenPage() => _app.OpenSettingsPage(SettingsWindow.OrganizerSettingsPage);

    // 把 Windows 自己的桌面图标按记录放回去。退出时程序会调用它：不留下一张不完整的桌面。
    internal void BoxesRestoreIcons(bool save = true)
    {
        Boxes.RestoreShellIcons();
        // 图标都放回去了，记录也就没用了。写出去，下次启动才不会照着一条「藏起来了」的旧记录再藏一遍。
        Settings.Boxes = Boxes.Snapshot();
        if (save) _app.QueueSave();
        _app.OrganizerChanged();
    }
}
