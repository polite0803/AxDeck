using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace AxDeck.Native;

// The Windows shortcuts a mapped desktop item is: a shortcut that points at a file that stays where it is, so the page holds a
// reference rather than a copy. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), which does the same.

internal static class ShellShortcuts
{
    // Writes a shortcut to the target at the shortcut's path.
    public static void Create(string target, string shortcutPath)
    {
        var link = (IShellLinkW)new ShellLink();
        link.SetPath(target);
        var folder = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(folder)) link.SetWorkingDirectory(folder);
        ((IPersistFile)link).Save(shortcutPath, true);
    }

    // The target of a shortcut, or an empty string when the file is not one or cannot be read.
    public static string ReadTarget(string shortcutPath)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            // The object starts empty: without loading the file first, GetPath reads a shortcut that was never opened
            // and always answers with an empty string.
            ((IPersistFile)link).Load(shortcutPath, 0);
            var buffer = new StringBuilder(260);
            link.GetPath(buffer, buffer.Capacity, out _, 0);
            return buffer.ToString();
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // A shortcut that lies in the desktop folder is shown on the desktop. There is no per-shortcut "show on desktop"
    // switch to read: the header flag this once answered from (bit 0 of the flags at 0x14) is HasLinkInfo, which every
    // real .lnk carries, so the "user removed it from the desktop" filter never filtered anything. The user removes an
    // icon from the desktop by deleting the file or moving it away, and Explorer draws what is in the folder — so this
    // page does too.
    public static bool OnDesktop(string shortcutPath) => true;

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, out WIN32_FIND_DATAW findData, int flags);
        void GetIDList(out IntPtr idList);
        void SetIDList(IntPtr idList);
        void GetDescription([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out] [MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int iconPathLength, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pathRel, int reserved);
        void Resolve(IntPtr hwnd, int flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATAW
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        public uint FileType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string AlternateFileName;
    }
}
