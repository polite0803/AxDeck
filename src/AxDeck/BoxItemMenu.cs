using System.Windows;
using AxDeck.Core;

namespace AxDeck;

// 分组里那一项的右键菜单：用的是 Windows 自己那个菜单，和在资源管理器里按右键看到的是同一个。
// 移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），那里也是弹系统的原生菜单
// （src-tauri/src/infrastructure/windows/shell_context/menu.rs）：菜单里的每一项都由系统给出，
// 菜单里的动作也由系统自己去做，所以「压缩」「发送到」「用别的程序打开」这些都天然可用。
internal static class BoxItemMenu
{
    // 分组里某一项的菜单。key 是那一项：真的文件是它的路径，系统项是 "shell::" 加它的编号。
    // position 是鼠标在窗口里的位置，菜单就开在那里。
    public static void Show(Window owner, BoxService boxes, DesktopBox box, string key, Action rebuild, Point position)
    {
        var isShell = BoxItemKey.IsShell(key);
        var shellId = isShell ? BoxItemKey.ShellId(key) : "";
        var path = isShell ? "" : key;

        // 鼠标在屏幕上的位置：系统的菜单要在屏幕的坐标里弹出来。
        var screen = owner.PointToScreen(position);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 系统项没有路径，它只有 shell 认识的名字；真文件给它的路径。
        var parsingName = isShell ? ShellParsingName(shellId) : path;
        if (parsingName is not { Length: > 0 })
        {
            BoxReport.Error(Loc.T("这一项找不到可以打开它的方式。", "There is no way to open this item."));
            return;
        }

        try
        {
            // 菜单是阻塞的：它开在桌面上，直到用户选一项或把它关掉。
            var ran = Native.ShellContextMenu.Show(hwnd, parsingName, (int)screen.X, (int)screen.Y);
            // 菜单里做的事可能改了文件（改名、删除），所以重画一遍。
            if (ran) rebuild();
        }
        catch (Exception ex)
        {
            // 菜单打不开的原因写进日志：这句话本身不告诉人哪儿错了。
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "axdeck-boxmenu.log"), $"{DateTime.Now:O} {parsingName} => {ex}\r\n"); }
            catch { }
            BoxReport.Error(Loc.T("系统菜单打不开：", "The system menu could not be opened: ") + ex.Message);
        }
    }

    // 把系统菜单建起来，但不弹出来，只报它有几项。
    // 这一格是为了能自动验一验：菜单到底有没有被系统认出来。
    // 走的每一步都跟 Show 走的是同一条路，只是最后不弹——弹出来的菜单是阻塞的，
    // 自动测试里不能让它等在那儿。
    public static int CountItems(string key)
    {
        var isShell = BoxItemKey.IsShell(key);
        var shellId = isShell ? BoxItemKey.ShellId(key) : "";
        var path = isShell ? "" : key;

        var parsingName = isShell ? ShellParsingName(shellId) : path;
        if (parsingName is not { Length: > 0 }) return -1;
        return Native.ShellContextMenu.CountItems(parsingName);
    }

    // 系统项系统认得的名字。用户文件夹没有固定的名字，它的路径每个账户不一样，要现问 Windows 要。
    private static string? ShellParsingName(string shellId)
    {
        if (shellId == Core.BoxShellItems.UserFolder) return Native.ShellKnownFolder.UserFolderPath();
        return Core.BoxShellItems.ParsingName(shellId);
    }
}
