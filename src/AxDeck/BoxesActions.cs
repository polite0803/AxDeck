using System.Windows;
using AxDeck.Core;

namespace AxDeck;

// 「桌面整理」页面上的动作：新建分组、显示、删除、改规则、改外观、开关原生图标。
// 这些动作都改整理器里的数据，改完保存一次，页面自己重画。
public partial class MainWindow
{
    // 新建一个分组，并把它显示出来。
    internal void BoxesCreate()
    {
        var box = Boxes.Create(Loc.T("新建分组", "New box"));
        BoxesChanged();
        BoxWindows.Show(Boxes, box);
        _boxesView?.Refresh();
    }

    // 显示一个分组的窗口。
    internal void BoxesShow(string boxId)
    {
        var box = Boxes.Find(boxId);
        if (box is null) return;
        BoxWindows.Show(Boxes, box);
    }

    // 删掉一个分组：它的真实文件夹按规则处理，它收着的原生图标放回桌面。
    internal void BoxesDelete(string boxId)
    {
        var box = Boxes.Find(boxId);
        if (box is null) return;
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Boxes.Delete(box, desktop);
        BoxWindows.Close(boxId);
        BoxesChanged();
    }

    // 改一条规则。which 是 drop、dragOut、conflict、delete 或 names。
    internal void BoxesSetPolicy(string which, string code)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "drop": settings.DropAction = code; break;
            case "dragOut": settings.DragOutAction = code; break;
            case "conflict": settings.ConflictPolicy = code; break;
            case "delete": settings.DeletePolicy = code; break;
            case "names": settings.NameDisplayMode = code; break;
        }
        BoxesChanged();
    }

    // 改一项外观：图标大小、名字字号、间距、圆角、底色。
    internal void BoxesSetLook(string which, int value)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "iconSize": settings.IconSize = Math.Clamp(value, 24, 96); break;
            case "labelTextSize": settings.LabelTextSize = Math.Clamp(value, 9, 20); break;
            case "iconGapX": settings.IconGapX = Math.Clamp(value, 0, 24); break;
            case "cornerRadius": settings.CornerRadius = Math.Clamp(value, 0, 24); break;
            case "backgroundOpacity": settings.BackgroundOpacity = Math.Clamp(value, 0, 100); break;
        }
        BoxesChanged();
    }

    // 开或关一个开关。
    internal void BoxesSetFlag(string which, bool value)
    {
        var settings = Boxes.Settings;
        switch (which)
        {
            case "doubleClickOpen": settings.DoubleClickOpen = value; break;
            case "showItemLabels": settings.ShowItemLabels = value; break;
            case "showShortcutArrow": settings.ShowShortcutArrow = value; break;
            case "snapToEdges": settings.SnapToEdges = value; break;
            case "autoHideShellIcons": settings.AutoHideShellIcons = value; break;
        }
        BoxesChanged();
    }

    // 显不显示 Windows 自己桌面上那个系统图标。
    internal void BoxesSetShellIcon(string shellId, bool visible)
    {
        Boxes.SetShellIconVisible(shellId, visible);
        BoxesChanged();
    }

    // 打开「桌面整理」这一页：托盘里点它的时候用。
    internal void BoxesOpenPage()
    {
        if (!Settings.BoxesPage) return;
        SelectPage(DeckPage.Boxes);
    }

    // 把所有分组都显示出来。
    internal void BoxesShowAll()
    {
        foreach (var box in Boxes.Boxes.ToList()) BoxWindows.Show(Boxes, box);
    }

    // 把 Windows 自己的桌面图标放回去：收进分组时藏起来的那些。
    // 退出时程序会调用它：图标回到桌面，Windows 才不会留下一个空桌面。
    internal void BoxesRestoreIcons(bool save = true)
    {
        Boxes.RestoreShellIcons();
        // The icons are back, so the record of them is no longer needed. It is written out, so the next start does not hide
        // them again behind a record that says they were hidden.
        Settings.Boxes = Boxes.Snapshot();
        if (save) _app.QueueSave();
        _boxesView?.Refresh();
    }
}
