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
    public static ImageSource? ForShellItem(string shellId)
    {
        if (ShellIcons.TryGetValue(shellId, out var known)) return known;
        ImageSource? icon = null;
        var parsingName = Core.BoxShellItems.ParsingName(shellId);
        if (parsingName is { Length: > 0 })
        {
            var info = new ShellFileInfo();
            // SHGFI_ICON | SHGFI_USEFILEATTRIBUTES: the entry has no file, so the shell is asked for the icon of its kind.
            if (SHGetFileInfoW(parsingName, 0, ref info, (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100u | 0x10u) != IntPtr.Zero && info.Icon != IntPtr.Zero)
            {
                try
                {
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze(); icon = source;
                }
                catch (Exception error) when (error is COMException or ArgumentException or InvalidOperationException) { }
                finally { DestroyIcon(info.Icon); }
            }
        }
        ShellIcons[shellId] = icon;
        return icon;
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
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
