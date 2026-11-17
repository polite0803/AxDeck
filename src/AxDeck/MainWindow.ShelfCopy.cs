using System.IO;
using System.Windows;
using AxDeck.Core;

namespace AxDeck;

// Copying the transfer page's files somewhere else on this PC — the desktop, or a folder the user picks — without taking them off
// the page. A copy of a big folder takes as long as it takes, so it runs off the UI thread and the rows say how it went afterwards.
public partial class MainWindow
{
    // The two menu entries, on a row and on several at once, in the same place on both menus. The desktop entry is left out when
    // Windows will not name that folder.
    private IEnumerable<object> CopyPlaces(IReadOnlyList<TransferFile> files)
    {
        var present = files.Where(file => ShelfFiles.Exists(file.Path)).ToList();
        if (present.Count == 0) yield break;
        if (DesktopFolder() is { } desktop)
            yield return Item(Loc.T("复制到桌面", "Copy to desktop"), () => CopyFiles(present, desktop), MenuIcons.Desktop);
        yield return Item(Loc.T("复制到文件夹…", "Copy to folder…"), () => ChooseFolder(present), MenuIcons.Folder);
    }

    // Null when Windows will not name the desktop folder, which the entry then leaves out.
    private static string? DesktopFolder()
    {
        try { return Native.ShellDesktop.FolderPath() is { Length: > 0 } folder ? folder : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private void ChooseFolder(IReadOnlyList<TransferFile> files)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("复制到文件夹", "Copy to folder"), Multiselect = false };
        if (dialog.ShowDialog(this) == true) CopyFiles(files, dialog.FolderName);
    }

    // The copies are made in the background and the rows say what became of them: the original files stay on the page, and a file
    // of the same name in the target folder is never replaced.
    private void CopyFiles(IReadOnlyList<TransferFile> files, string? folder)
    {
        if (folder is not { Length: > 0 }) return;
        var paths = files.Where(file => ShelfFiles.Exists(file.Path)).Select(file => file.Path).ToList();
        if (paths.Count == 0) return;
        Task.Run(() => ShelfCopy.Copy(paths, folder)).ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
        {
            // A row says whether its own file made it, so one unreadable file in a batch does not pass for a copy that worked.
            var failed = task.IsCompletedSuccessfully
                ? task.Result.Failed.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
                Flash(file.Id, failed.Contains(file.Path) ? Loc.T("未复制", "Not copied") : Loc.T("已复制", "Copied"));
        })), TaskScheduler.Default);
    }
}