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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
