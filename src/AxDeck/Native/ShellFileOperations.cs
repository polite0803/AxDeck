using System.IO;
using System.Runtime.InteropServices;

namespace AxDeck.Native;

// The real file operations behind the desktop page. Moving and deleting go through the Windows shell, so Explorer sees the
// change at once and the recycle bin keeps what it deletes. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT).

internal static class ShellFileOperations
{
    // The shell's own file operation codes.
    private const uint FunctionMove = 1, FunctionDelete = 3;
    // The flags: the shell may undo a delete, and it asks nothing and reports nothing, as the panel has already asked.
    private const ushort AllowUndo = 0x0040, NoConfirmation = 0x0010, Silent = 0x0004, NoErrorUi = 0x0400;
    // The shell may make the folder it moves into, without asking.
    private const ushort NoConfirmMkDir = 0x0200;

    // Moves the paths to their targets through the shell, so the desktop sees them right away. The moves are handed over in one
    // call when they all go into the same folder under the same name, which keeps the desktop from redrawing for each file.
    public static void Move(IEnumerable<(string Source, string Target)> moves)
    {
        var list = moves.ToList();
        if (list.Count == 0) return;
        var shared = SharedDestination(list);
        if (shared is { } folder)
        {
            Run(FunctionMove, DoubleNull(list.Select(pair => pair.Source).ToList()), DoubleNull([folder]), NoConfirmMkDir);
            return;
        }
        foreach (var (source, target) in list) Run(FunctionMove, DoubleNull([source]), DoubleNull([target]), NoConfirmMkDir);
    }

    // Sends the paths to the recycle bin, so they can be brought back.
    public static void Recycle(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        Run(FunctionDelete, DoubleNull(list), IntPtr.Zero, AllowUndo | NoConfirmation | Silent | NoErrorUi);
    }

    // The folder all the moves share, when every move keeps the name of its file and they all go into one folder.
    private static string? SharedDestination(List<(string Source, string Target)> moves)
    {
        var first = moves[0].Target;
        var folder = Path.GetDirectoryName(first);
        if (string.IsNullOrEmpty(folder)) return null;
        foreach (var (source, target) in moves)
        {
            if (!string.Equals(Path.GetDirectoryName(target), folder, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(Path.GetFileName(source), Path.GetFileName(target), StringComparison.OrdinalIgnoreCase)) return null;
        }
        return folder;
    }

    // One shell file operation. The operation structure is filled in and handed over; the shell keeps its own copy of the paths
    // for as long as the call lasts.
    //
    // The answer is checked: a zero return means it worked, and anything else means it did not — a full disk, a path that
    // could not be read, a file that vanished. The aborted flag is the other failure: the user pressed Cancel in the
    // shell's own dialog. Both were once swallowed, and then a half-finished move looked like a success to the caller,
    // which went on believing the files had arrived.
    private static void Run(uint function, IntPtr from, IntPtr to, ushort flags)
    {
        try
        {
            var operation = new ShellFileOperation
            {
                Function = function,
                From = from,
                To = to,
                Flags = flags
            };
            var result = SHFileOperationW(ref operation);
            if (result != 0)
                throw new IOException(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The shell file operation failed with error 0x{0:X8}.", result));
            if (operation.Aborted)
                throw new OperationCanceledException("The shell file operation was cancelled.");
        }
        finally
        {
            if (from != IntPtr.Zero) Marshal.FreeHGlobal(from);
            if (to != IntPtr.Zero) Marshal.FreeHGlobal(to);
        }
    }

    // The list of paths the shell takes: each one ends with a null, and the list ends with another.
    private static IntPtr DoubleNull(List<string> paths)
    {
        var text = string.Join("\0", paths) + "\0\0";
        return Marshal.StringToHGlobalUni(text);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileOperation
    {
        public IntPtr Window;
        public uint Function;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool Aborted;
        public IntPtr NameMappings;
        public IntPtr Title;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref ShellFileOperation operation);
}
