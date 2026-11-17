using System.Runtime.InteropServices;

namespace AxDeck.Native;

// 用户的文件夹（Profile）。它的路径在不同账户和重定向配置下都不一样，所以每次现问 Windows 要，
// 不把它写死。移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），
// 那里也是用 SHGetKnownFolderPath 取用户文件夹（shell_virtual_item/user_folder.rs）。

internal static class ShellKnownFolder
{
    // 用户的文件夹在系统里的编号。
    private static readonly Guid ProfileFolderId = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");

    // 用户的文件夹现在在哪，空的时候说明 Windows 不给。
    public static string? UserFolderPath()
    {
        try
        {
            var pointer = IntPtr.Zero;
            try
            {
                // 这一段是本地变量：SHGetKnownFolderPath 要的是引用，不能拿只读的静态字段去传。
                var folderId = ProfileFolderId;
                var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pointer);
                if (result != 0 || pointer == IntPtr.Zero) return null;
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                // 这块内存是系统给的，用完要还回去。
                if (pointer != IntPtr.Zero) Marshal.FreeCoTaskMem(pointer);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return null;
        }
    }

    // 桌面上给用户文件夹画的名字。它不是 Profile 目录的名字：Profile 目录是 C:\Users\polit，
    // 资源管理器却按账户的显示名写成「刘小平」。拿目录名去比，这一项就永远认不出来。
    // 名字问 shell 要（SIGDN_NORMALDISPLAY），它给的正是资源管理器画在桌面上的那个。
    public static string? UserFolderName()
    {
        if (UserFolderPath() is not { Length: > 0 } path) return null;
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var item) != 0) return null;
            try
            {
                if (item.GetDisplayName(SigdnNormalDisplay, out var text) != 0 || text == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringUni(text) is { Length: > 0 } name ? name : null;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(text);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            return null;
        }
    }

    // SIGDN_NORMALDISPLAY：资源管理器在桌面上显示的那个名字。
    private const uint SigdnNormalDisplay = 0;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context,
        [MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr context, ref Guid handlerId, ref Guid iid, out IntPtr result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint form, out IntPtr name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
}
