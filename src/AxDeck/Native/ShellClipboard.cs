using System.Runtime.InteropServices;
using System.Text;

namespace AxDeck.Native;

// The file clipboard, the one Explorer uses: a list of paths (CF_HDROP) plus the intent of the last operation
// (copy or cut). With it the desktop page's copy and paste speak the same language as the rest of Windows, so files can be
// pasted into Explorer and out of it. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), which writes
// the same clipboard (shell_clipboard.rs).

internal static class ShellClipboard
{
    // The format the paths are written in.
    private const uint FileDropFormat = 15;
    // The format that says what the user meant: copy or cut.
    private const string PreferredDropEffectFormat = "Preferred DropEffect";
    // The intent of the operation: the file is to be copied.
    private const uint DropEffectCopy = 1;
    // The intent of the operation: the file is to be moved.
    private const uint DropEffectMove = 2;
    // The memory the clipboard takes: it may be moved, and it starts empty.
    private const uint MovableMemory = 0x0002 | 0x0040;
    // How many characters one path may take. Windows allows a path of 32767 characters at most.
    private const int PathBufferChars = 32768;
    // The index that asks how many files there are, rather than for one of them.
    private const uint FileCountIndex = 0xFFFFFFFF;
    // The size of the DROPFILES header that has to come before the paths.
    private const int DropFilesSize = 20;

    // The header the shell reads before the paths. Its shape is fixed by Windows.
    [StructLayout(LayoutKind.Sequential)]
    private struct DropFiles
    {
        // Where the paths start, counted from the beginning of the block.
        public uint FilesOffset;
        // Where the mouse was when the drag began.
        public PointData Point;
        // Whether the coordinates are the ones of the client area.
        public uint NoClientRect;
        // Whether the paths are wide characters.
        public uint Wide;
    }

    // A point in the DROPFILES header.
    [StructLayout(LayoutKind.Sequential)]
    private struct PointData
    {
        public int X;
        public int Y;
    }

    // What the user meant: copy the files, or move them.
    public enum Operation { Copy, Move }

    // Writes the paths to the clipboard, with the intent of the operation. True when it was written.
    public static bool Write(IReadOnlyList<string> paths, Operation operation)
    {
        if (paths.Count == 0) return false;
        if (!Open()) return false;
        try
        {
            EmptyClipboard();
            if (!SetFileList(paths)) return false;
            return SetDropEffect(operation);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        finally { CloseClipboard(); }
    }

    // The paths on the clipboard and what the user meant. The paths are empty when the clipboard holds no files.
    public static (IReadOnlyList<string> Paths, Operation Operation) Read()
    {
        if (!Open()) return ([], Operation.Copy);
        try
        {
            if (!IsClipboardFormatAvailable(FileDropFormat)) return ([], Operation.Copy);
            var handle = GetClipboardData(FileDropFormat);
            if (handle == IntPtr.Zero) return ([], Operation.Copy);

            var paths = new List<string>();
            // 问文件有几个：0xFFFFFFFF 加上空缓冲区，问回来的就是个数（不是总长度）。
            var count = DragQueryFile(handle, FileCountIndex, IntPtr.Zero, 0);
            // The buffer is a plain piece of memory, not a StringBuilder: the framework's marshaller for those
            // writes back into them and trips over the very small ones. A path is never longer than Windows allows,
            // so one buffer that big holds every one of them.
            var buffer = Marshal.AllocHGlobal(PathBufferChars * 2);
            try
            {
                for (uint index = 0; index < count; index++)
                {
                    var length = DragQueryFile(handle, index, buffer, PathBufferChars);
                    if (length == 0) break;
                    paths.Add(Marshal.PtrToStringUni(buffer) ?? "");
                    // The clipboard holds a fixed list of paths: a hundred thousand is far past anything real, and it
                    // keeps a broken answer from looping forever.
                    if (paths.Count > 10000) break;
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
            return (paths, ReadDropEffect());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return ([], Operation.Copy); }
        finally { CloseClipboard(); }
    }

    // The paths, written in the format the shell reads. The block starts with a DROPFILES header, and the paths
    // follow it: the header says where the first path starts. Without the header the shell reads the first
    // character of the first path as that place, and walks off into memory it has no business touching.
    private static bool SetFileList(IReadOnlyList<string> paths)
    {
        // The header: where the paths start, where the mouse was, and the width of the paths.
        var header = new DropFiles
        {
            // The paths start right after the header, which is 20 bytes long.
            FilesOffset = DropFilesSize,
            Point = new PointData { X = 0, Y = 0 },
            NoClientRect = 0,
            Wide = 1
        };

        // Every path ends with a null, and the whole list ends with another one.
        var text = string.Join("\0", paths) + "\0\0";
        var textBytes = Encoding.Unicode.GetBytes(text);
        var total = DropFilesSize + textBytes.Length;
        var memory = GlobalAlloc(MovableMemory, total);
        if (memory == IntPtr.Zero) return false;
        // The clipboard takes the memory over once the data is written; until then it is ours to free.
        var handedOver = false;
        try
        {
            var target = GlobalLock(memory);
            if (target == IntPtr.Zero) return false;
            try
            {
                var headerBytes = new byte[DropFilesSize];
                System.Runtime.InteropServices.MemoryMarshal.Write(headerBytes, in header);
                Marshal.Copy(headerBytes, 0, target, headerBytes.Length);
                Marshal.Copy(textBytes, 0, target + DropFilesSize, textBytes.Length);
            }
            finally { GlobalUnlock(memory); }

            handedOver = SetClipboardData(FileDropFormat, memory) != IntPtr.Zero;
            return handedOver;
        }
        finally
        {
            if (!handedOver) GlobalFree(memory);
        }
    }

    // The intent of the operation, in the format the shell reads.
    private static bool SetDropEffect(Operation operation)
    {
        var format = RegisterClipboardFormat(PreferredDropEffectFormat);
        if (format == 0) return false;
        var memory = GlobalAlloc(MovableMemory, sizeof(uint));
        if (memory == IntPtr.Zero) return false;
        var handedOver = false;
        try
        {
            var target = GlobalLock(memory);
            if (target == IntPtr.Zero) return false;
            try { Marshal.WriteInt32(target, 0, operation == Operation.Move ? (int)DropEffectMove : (int)DropEffectCopy); }
            finally { GlobalUnlock(memory); }

            handedOver = SetClipboardData(format, memory) != IntPtr.Zero;
            return handedOver;
        }
        finally
        {
            if (!handedOver) GlobalFree(memory);
        }
    }

    // What the user meant when the files were put on the clipboard.
    private static Operation ReadDropEffect()
    {
        var format = RegisterClipboardFormat(PreferredDropEffectFormat);
        if (format == 0) return Operation.Copy;
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return Operation.Copy;
        var target = GlobalLock(handle);
        if (target == IntPtr.Zero) return Operation.Copy;
        try
        {
            var value = (uint)Marshal.ReadInt32(target, 0);
            return value == DropEffectMove ? Operation.Move : Operation.Copy;
        }
        finally { GlobalUnlock(handle); }
    }

    private static bool Open()
    {
        try { return OpenClipboard(IntPtr.Zero); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterClipboardFormat(string name);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint DragQueryFile(IntPtr drop, uint index, IntPtr file, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, int bytes);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
