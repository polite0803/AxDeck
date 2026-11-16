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
            var buffer = new StringBuilder(260);
            link.GetPath(buffer, buffer.Capacity, out _, 0);
            return buffer.ToString();
        }
        catch (Exception ex) when (ex is COMException or FileNotFoundException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    // Whether a shortcut is shown on the desktop. The user can turn that off for one shortcut ("Show on desktop" in the
    // menu), and then the desktop does not draw it even though the file lies in the desktop folder.
    //
    // The answer is in the shortcut's own header: the flags sit at a fixed place near the front of the file, and the first
    // of them says whether the shortcut belongs on the desktop. This is read straight out of the file rather than asked
    // of the shell: the shell's own answer needs an interface that is a trouble to call, and this one cannot go wrong.
    public static bool OnDesktop(string shortcutPath)
    {
        try
        {
            using var stream = new FileStream(shortcutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            // The header's size, then the class id (16 bytes), then the flags.
            if (stream.ReadByte() != 0x4C || stream.ReadByte() != 0x00) return true;   // "L\0": a shell link starts so.
            stream.Position = 0x14;
            var flags = reader.ReadUInt32();
            return (flags & OnDesktopFlag) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A file that cannot be read this way is left in: it is better to show one icon too many than to lose one.
            return true;
        }
    }

    // The flag that says the shortcut is shown on the desktop.
    private const uint OnDesktopFlag = 0x00000001;

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
