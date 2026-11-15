using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace AxDeck.Native;

// The icons the boxes show: the Explorer's icon for a file, and the icon of the Windows system entries, which have no file of
// their own and are drawn by the shell from their parsing name.

internal static class BoxIcons
{
    // The icons already read, by the path they were read for; a box shows the same files many times over.
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ImageSource?> ShellIcons = new(StringComparer.Ordinal);

    // The Explorer's icon for the file. A file that is gone still gets the icon of its type, as the file transfer page does.
    public static ImageSource? ForFile(string path)
    {
        if (Icons.TryGetValue(path, out var known)) return known;
        ImageSource? icon = null;
        var info = new ShellFileInfo();
        uint flags = 0x100u /* SHGFI_ICON */ | (File.Exists(path) || Directory.Exists(path) ? 0u : 0x10u /* SHGFI_USEFILEATTRIBUTES */);
        if (SHGetFileInfoW(path, 0x80 /* FILE_ATTRIBUTE_NORMAL */, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), flags) != IntPtr.Zero && info.Icon != IntPtr.Zero)
        {
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze(); icon = source;
            }
            catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException) { }
            finally { DestroyIcon(info.Icon); }
        }
        if (Icons.Count > 500) Icons.Clear();
        Icons[path] = icon;
        return icon;
    }

    // The icon of one of the Windows system entries, drawn by the shell from its parsing name.
    //
    // 系统项的解析名是 `::{GUID}` 这样的伪路径，背后没有文件。直接把它当路径递给 SHGetFileInfoW 时，
    // 参数里的 SHGFI_USEFILEATTRIBUTES 会让 shell 按「一个没有扩展名的普通文件」取图标——于是回收站、
    // 此电脑、网络、控制面板全都画成同一个通用文件图标（实测四者像素完全相同）。
    // 要拿到各自的真图标，得先把伪路径解析成 PIDL，再用 SHGFI_PIDL 问——原版就是这么做的。
    public static ImageSource? ForShellItem(string shellId)
    {
        if (ShellIcons.TryGetValue(shellId, out var known)) return known;
        ImageSource? icon = null;
        // 用户文件夹没有固定的解析名：它就是当前账户的 Profile 目录，按真实路径取图标。
        var name = shellId == Core.BoxShellItems.UserFolder
            ? ShellKnownFolder.UserFolderPath()
            : Core.BoxShellItems.ParsingName(shellId);
        if (name is { Length: > 0 }) icon = PidlIcon(name) ?? FileIcon(name);
        ShellIcons[shellId] = icon;
        return icon;
    }

    // 把 `::{GUID}` 解析成 PIDL 再取图标：命名空间里的对象只有这样才能拿到它自己的图标。
    private static ImageSource? PidlIcon(string parsingName)
    {
        IntPtr pidl = IntPtr.Zero;
        try
        {
            if (SHParseDisplayName(parsingName, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero) return null;
            var info = new ShellFileInfo();
            // 按 PIDL 的那几个标志里不能有 SHGFI_USEFILEATTRIBUTES：有了它 shell 又退回按文件取图标的路上去了。
            if (SHGetFileInfoPidl(pidl, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x0008u /* SHGFI_PIDL */ | 0x100u /* SHGFI_ICON */ | 0x0u /* SHGFI_LARGEICON */) == IntPtr.Zero
                || info.Icon == IntPtr.Zero) return null;
            return FromIcon(info.Icon);
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException) { return null; }
        finally { if (pidl != IntPtr.Zero) CoTaskMemFree(pidl); }
    }

    // 解析不出来时退回按路径取（某些 Shell 扩展把系统项也做成了真实路径）。
    private static ImageSource? FileIcon(string parsingName)
    {
        var info = new ShellFileInfo();
        if (SHGetFileInfoW(parsingName, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100u /* SHGFI_ICON */) == IntPtr.Zero || info.Icon == IntPtr.Zero) return null;
        return FromIcon(info.Icon);
    }

    // 一个 HICON 变成 WPF 用得上的位图；句柄无论成败都要归还。
    private static ImageSource? FromIcon(IntPtr handle)
    {
        try
        {
            var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException) { return null; }
        finally { DestroyIcon(handle); }
    }

    // The name the Explorer shows for the file: the shortcut's own name, without the folder it points at.
    public static string DisplayName(string path, Core.BoxNameDisplay mode)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) return path;
        return mode switch
        {
            Core.BoxNameDisplay.Full => name,
            Core.BoxNameDisplay.HideAllExtensions => Path.GetFileNameWithoutExtension(name),
            // A shortcut keeps its name; only the extension is hidden, so the arrow still marks it as one.
            _ => Core.BoxNaming.IsShortcut(name) ? Path.GetFileNameWithoutExtension(name) : name
        };
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SHGetFileInfoW(string path, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    // 按 PIDL 问的那一个：第一个参数是指针不是字符串，所以跟按路径的那个是两个声明。
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW")] private static extern IntPtr SHGetFileInfoPidl(IntPtr pidl, uint attributes, ref ShellFileInfo info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, IntPtr bindContext, out IntPtr pidl, uint attributesIn, out uint attributesOut);
    [DllImport("ole32.dll")] private static extern void CoTaskMemFree(IntPtr memory);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
