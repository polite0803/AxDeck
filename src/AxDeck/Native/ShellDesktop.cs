using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AxDeck.Native;

// The desktop itself: the folder the user's files sit in, what is in it, and whether Windows draws the icons of it.
//
// The icons on the Windows desktop are drawn by Explorer out of two places: the files in the desktop folder, and the system
// entries (This PC, the Recycle Bin). Turning the icons off is a single switch under Explorer\Advanced (HideIcons), and
// that is what "a clean desktop" means here: the real desktop is emptied of its icons while AxDeck shows its own picture
// of the same things, and the icons come back when AxDeck goes.

internal static class ShellDesktop
{
    // The desktop folder of the user who is running now. It is not the same on every machine: OneDrive may have taken it
    // over, and then the files sit somewhere else entirely.
    public static string FolderPath()
    {
        // The desktop known folder is asked for first: Windows knows best where it is, including when OneDrive moved it.
        var known = KnownFolderPath(new Guid("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"));
        if (known is { Length: > 0 } && Directory.Exists(known)) return known;
        // Without it, the ordinary place: the user's profile folder with Desktop on it.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0) return "";
        var desktop = Path.Combine(profile, "Desktop");
        return Directory.Exists(desktop) ? desktop : "";
    }

    // 桌面上有哪些东西：桌面文件夹里的文件、文件夹和快捷方式，加上 Windows 自己画的那几个系统项。
    // 排不排序、藏不藏哪一项，都交给 Core.DesktopItems 去算，这里只负责把事实问出来。
    public static IReadOnlyList<Core.DesktopItems.Item> Items()
    {
        List<Core.DesktopItems.Item> items = [];
        var folder = FolderPath();
        if (folder.Length > 0)
        {
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    // desktop.ini 之类被标成隐藏的文件，桌面上本来就看不见，这里也不画。
                    if (HasHiddenAttribute(path)) continue;
                    var isDirectory = IsDirectory(path);
                    var kind = Core.DesktopItems.KindOf(path, isDirectory);
                    // 快捷方式自己带着一个"显示在桌面上"的标志：用户拿掉的那一个，Windows 不画。
                    var onDesktop = kind != Core.DesktopItems.Kind.Shortcut || ShellShortcuts.OnDesktop(path);
                    items.Add(new Core.DesktopItems.Item(path, Core.DesktopItems.NameOf(path), kind, onDesktop));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // 桌面读不了不是错误：这一页就当桌面上什么也没有。
            }
        }
        // Windows 自己画的系统项：此电脑、回收站、网络、控制面板、用户文件夹。用户关掉了哪一个，这里就不画哪一个。
        foreach (var entry in Core.BoxShellItems.All)
        {
            var clsid = entry.DesktopIconClsid;
            if (clsid is null) continue;
            if (!ShellDesktopIcons.IsVisible(clsid, entry.DefaultVisible)) continue;
            var target = entry.Id == Core.BoxShellItems.UserFolder
                ? ShellKnownFolder.UserFolderPath() ?? ""
                : entry.ParsingNames.FirstOrDefault() ?? "";
            if (target.Length == 0) continue;
            items.Add(new Core.DesktopItems.Item(target, entry.Name, Core.DesktopItems.Kind.Shell, true));
        }
        return Core.DesktopItems.Ordered(items);
    }

    // 这一项是不是文件夹。问不出来就当它不是：一个读不到的项，宁可当文件画出来。
    private static bool IsDirectory(string path)
    {
        try { return Directory.Exists(path); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
    }

    // 这一项有没有被标成"隐藏"。Windows 的桌面上，带这个标记的文件是不画的（desktop.ini 就是）。
    private static bool HasHiddenAttribute(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    // ---- Turning the icons off ----

    private const string AdvancedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    // Whether the desktop icons are drawn now. The switch is absent when Explorer has never written it, and then the
    // icons are shown.
    public static bool IconsVisible()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedPath);
            if (key?.GetValue("HideIcons") is not int value) return true;
            return value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return true;
        }
    }

    // Turns the desktop icons off or on, and asks Explorer to draw the desktop again.
    //
    // 只写注册表是不够的：Explorer 的桌面视图在它自己启动时读一次 HideIcons，之后这个开关只跟着
    // 「查看 > 显示桌面图标」那条命令走。想让已经在跑的桌面当场响应，得把那条命令发给桌面的
    // 窗口（SHELLDLL_DefView 里的 WM_COMMAND 0x7402）。那条命令是「切换」不是「设成」，
    // 所以先读写注册表之前的状态当 Explorer 的现状：现状和目标不一样才发一下，一样就什么都不用做。
    // 找不到桌面窗口时退回 SHChangeNotify，至少重画一遍——Explorer 重启后的第一次就落到这条退路上。
    public static void SetIconsVisible(bool visible)
    {
        var explorerHolds = IconsVisible();
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(AdvancedPath, true))
                key?.SetValue("HideIcons", visible ? 0 : 1, RegistryValueKind.DWord);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        if (explorerHolds == visible || !ToggleDesktopIcons()) RefreshExplorer();
    }

    // 把「显示桌面图标」那条切换命令发给正在跑的桌面。找到桌面窗口并送出命令时返回真。
    private static bool ToggleDesktopIcons()
    {
        var desktop = FindDesktopWindow();
        if (desktop == IntPtr.Zero) return false;
        SendMessage(desktop, 0x0111 /* WM_COMMAND */, new IntPtr(0x7402), IntPtr.Zero);
        return true;
    }

    // 桌面的窗口：类名叫 SHELLDLL_DefView 的那个，一般就在 Progman 或 WorkerW 下面。
    private static IntPtr FindDesktopWindow()
    {
        var progman = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
        if (progman != IntPtr.Zero)
        {
            var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        // 多显示器或某些 Shell 扩展会让 DefView 挂在 WorkerW 下面，一层一层找过去。
        IntPtr worker = IntPtr.Zero;
        while ((worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            var defView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero) return defView;
        }
        return IntPtr.Zero;
    }

    // Tells Explorer that the desktop changed, so the icons are drawn again.
    public static void RefreshExplorer()
    {
        // SHCNE_ASSOCCHANGED with SHCNF_IDLIST asks the shell to refresh the icons it shows.
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    // The path of a known folder, or null when Windows does not give it.
    private static string? KnownFolderPath(Guid folderId)
    {
        try
        {
            var pointer = IntPtr.Zero;
            try
            {
                var id = folderId;
                var result = SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out pointer);
                if (result != 0 || pointer == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return null;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
