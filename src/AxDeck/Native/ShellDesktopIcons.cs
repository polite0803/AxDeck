using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace AxDeck.Native;

// The native desktop icons of the Windows system entries, and whether they are shown. Windows keeps this in two places under
// the current user's key, one for the new start menu and one for the classic one; both are written so the icon is hidden on
// either. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), which does the same.

internal static class ShellDesktopIcons
{
    // The two places Explorer reads the hidden desktop icons from.
    private static readonly string[] VisibilityPaths =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel",
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu"
    ];

    // Whether the system entry's native icon is shown now. A key or a value that is not there means Windows shows it:
    // Explorer only writes a value when the user has touched the switch, and an entry that has never been touched
    // keeps the icon Windows itself put there (This PC and Network ship with no value and are shown).
    //
    // 之前这里把「注册表里没有值」交给 defaultVisible 回答，而此电脑、网络的 default 是 false——于是
    // 从没动过那个开关的机器上，AxDeck 会把 Windows 明明画着的两个系统项当成藏着的，桌面页少画它们。
    // Windows 的规矩是「无值即显示」，这里跟它走：defaultVisible 只在注册表读不出来（权限、IO）时兜底。
    public static bool IsVisible(string clsid, bool defaultVisible)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(VisibilityPaths[0]);
            if (key is null) return true;
            return key.GetValue(clsid) is not int value || value == 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            return defaultVisible;
        }
    }

    // Shows or hides the system entry's native icon and asks Explorer to redraw the desktop.
    public static void SetVisible(string clsid, bool visible)
    {
        var hidden = visible ? 0 : 1;
        foreach (var path in VisibilityPaths)
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true);
            key?.SetValue(clsid, hidden, RegistryValueKind.DWord);
        }
        RefreshExplorer();
    }

    // The icon's own class id is the name of the value Explorer reads; the braces around it are part of the name.
    public static string ClsidOf(string shellId)
    {
        var item = Core.BoxShellItems.FindDesktopIcon(shellId);
        return item?.DesktopIconClsid ?? "";
    }

    // Tells Explorer that the desktop changed, so the icons are drawn again.
    private static void RefreshExplorer()
    {
        // SHCNE_ASSOCCHANGED with SHCNF_IDLIST asks the shell to refresh the icons it shows.
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
