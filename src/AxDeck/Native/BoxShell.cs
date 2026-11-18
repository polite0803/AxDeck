using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AxDeck.Native;

// Opening what the desktop page shows: a real file with the program Windows gives it, and one of the Windows system entries with the shell.

internal static class BoxShell
{
    // Opens the path the way Windows opens it: a file with its own program, a folder in Explorer.
    public static void Open(string path)
    {
        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    // Opens one of the Windows system entries: the shell opens it by its parsing name, as the desktop does.
    public static void OpenShellItem(string shellId)
    {
        if (shellId == Core.BoxShellItems.UserFolder)
        {
            Process.Start(new ProcessStartInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) { UseShellExecute = true });
            return;
        }
        var parsingName = Core.BoxShellItems.ParsingName(shellId);
        if (parsingName is not { Length: > 0 }) return;
        Process.Start(new ProcessStartInfo(parsingName) { UseShellExecute = true });
    }

    // The path the user folder is at, which is also the path its icon is taken from.
    public static string UserFolderPath => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Asks the shell to show the desktop's own icons again after the organizer is done with them.
    public static void RefreshExplorer()
    {
        SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0x0000 /* SHCNF_IDLIST */, IntPtr.Zero, IntPtr.Zero);
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
