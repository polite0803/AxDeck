using System.Windows;
using System.Windows.Controls;
using System.IO;
using AxDeck.Core;

namespace AxDeck;

// 分组里那一项的右键菜单：打开、重命名、拖出到桌面、移出分组、删除。
internal static class BoxItemMenu
{
    // 分组里某一项的菜单。key 是那一项：真的文件是它的路径，系统项是 "shell::" 加它的编号。
    public static void Show(Window owner, BoxService boxes, DesktopBox box, string key, Action rebuild)
    {
        var menu = BoxMenu.Build();
        var isShell = BoxItemKey.IsShell(key);
        var shellId = isShell ? BoxItemKey.ShellId(key) : "";
        var path = isShell ? "" : key;

        if (!isShell)
        {
            menu.Items.Add(BoxMenu.Item(Loc.T("打开", "Open"), () =>
            {
                try { Native.BoxShell.Open(path); }
                catch (Exception ex) { BoxReport.Error(Loc.T("无法打开：", "It could not be opened: ") + ex.Message); }
            }, BoxMenu.MenuIcons.Open));
            menu.Items.Add(BoxMenu.Item(Loc.T("重命名", "Rename"), () => Rename(owner, boxes, box, path, rebuild), BoxMenu.MenuIcons.Edit));
            menu.Items.Add(BoxMenu.Item(Loc.T("拖出到桌面", "Put on the desktop"), () => DragOut(boxes, box, key), BoxMenu.MenuIcons.Desktop));
            menu.Items.Add(BoxMenu.Separator());
        }
        else
        {
            menu.Items.Add(BoxMenu.Item(Loc.T("打开", "Open"), () =>
            {
                try { Native.BoxShell.OpenShellItem(shellId); }
                catch (Exception ex) { BoxReport.Error(Loc.T("无法打开：", "It could not be opened: ") + ex.Message); }
            }, BoxMenu.MenuIcons.Open));
            // 系统项没有自己的文件，它只是被记在分组里；移出去就是把它的桌面图标放回去。
            menu.Items.Add(BoxMenu.Item(Loc.T("移出分组", "Take out of the box"), () =>
            {
                boxes.RemoveVirtualItem(box.Id, shellId);
                rebuild();
            }, BoxMenu.MenuIcons.Remove));
            menu.Items.Add(BoxMenu.Separator());
        }

        menu.Items.Add(BoxMenu.Item(isShell ? Loc.T("从分组移除", "Remove from the box") : Loc.T("删除", "Delete"),
            () => Delete(boxes, box, path, shellId, isShell, rebuild), BoxMenu.MenuIcons.Delete, danger: true));
        BoxMenu.Open(owner, menu);
    }

    // 改一个文件的名字：它在自己的文件夹里改名，分组里的顺序跟着换。
    private static void Rename(Window owner, BoxService boxes, DesktopBox box, string path, Action rebuild)
    {
        var dialog = new BoxNameDialog(Loc.T("重命名", "Rename"), Loc.T("新名字", "New name"), Path.GetFileName(path));
        if (!dialog.Accepted) return;
        if (boxes.Rename(box.Id, path, dialog.NewName)) rebuild();
        else BoxReport.Error(Loc.T("这个名字用不了：Windows 不收这个名字，或者已经有同名的东西了。",
            "This name cannot be used: Windows does not take it, or something of that name is already there."));
    }

    // 把一项挪到桌面上：按拖出的规则，移动、复制或留一个快捷方式。
    private static void DragOut(BoxService boxes, DesktopBox box, string key)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        try
        {
            boxes.DragOut(box.Id, [key], desktop);
        }
        catch (Exception ex)
        {
            BoxReport.Error(Loc.T("拖出失败：", "It could not be put on the desktop: ") + ex.Message);
        }
    }

    // 删掉一项：真文件进回收站，系统项只是把桌面图标放回去。
    private static void Delete(BoxService boxes, DesktopBox box, string path, string shellId, bool isShell, Action rebuild)
    {
        if (isShell)
        {
            boxes.RemoveVirtualItem(box.Id, shellId);
            rebuild();
            return;
        }
        boxes.RemoveItem(box.Id, path);
        rebuild();
    }
}
