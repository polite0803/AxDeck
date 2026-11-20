using System.Windows;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The command palette in the panel: a button in the toolbar opens it, and the command that is chosen is run here. The palette
// itself is a window of its own (CommandPaletteWindow); this file holds the way in and the actions.
public partial class MainWindow
{
    private CommandPaletteWindow? _palette;

    // Opens the palette, with text in it when a command asks for one. Only one is open at a time: the button opens it again,
    // which closes it.
    internal void PaletteOpen(string? query = null)
    {
        if (_palette is { } open && open.IsVisible) { open.Close(); return; }
        var foreground = PasteInput.Foreground;
        if (PasteInput.IsOther(foreground)) _app.Shelf.TypingTarget = foreground;
        var window = new CommandPaletteWindow(_app, this, query) { Owner = this };
        _palette = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_palette, window)) _palette = null; };
        window.Show();
        window.Activate();
    }

    private void Palette_Click(object sender, RoutedEventArgs e) => PaletteOpen();

    // A command from an address or a command line asks for a page: the panel opens on it, and stays open under the keyboard
    // pin the hotkeys use, so it is not closed again before it is looked at.
    internal void CommandShow(DeckPage page)
    {
        if (_expanded) SelectPage(page);
        else OpenFromHotkey(page);
    }

    // Runs the command that was chosen in the palette.
    internal void PaletteRun(PaletteCommand command)
    {
        switch (command.Action)
        {
            case PaletteAction.Page when Enum.TryParse<DeckPage>(command.Argument, out var page):
                SelectPage(page);
                break;
            case PaletteAction.ToggleCompactStyle:
                _app.CycleCompactStyle();
                break;
            case PaletteAction.ToggleLineStyle:
                _app.ToggleLineStyle();
                break;
            case PaletteAction.RefreshUsage:
                SyncNow();
                break;
            case PaletteAction.OpenSettings:
                _app.OpenSettings();
                break;
            case PaletteAction.ToggleHidden:
                _app.ToggleHidden();
                break;
            case PaletteAction.ToggleClickThrough:
                _app.ToggleClickThrough();
                break;
            case PaletteAction.FocusOpen:
                FocusOpen();
                break;
            case PaletteAction.FocusStart when int.TryParse(command.Argument, out var minutes):
                _app.FocusStart(minutes);
                break;
            case PaletteAction.FocusStartProject when Guid.TryParse(command.Argument, out var focusProjectId):
                _app.FocusStartProject(focusProjectId);
                break;
            case PaletteAction.OpenProject when Guid.TryParse(command.Argument, out var projectId):
                _app.OpenProject(projectId);
                break;
            case PaletteAction.RestoreProject when Guid.TryParse(command.Argument, out var restoreId):
                _app.RestoreProject(restoreId, sent => _app.SystemNotice(
                    sent ? Loc.T("工作已准备", "Work is ready") : Loc.T("项目已恢复", "Project restored"),
                    sent
                        ? Loc.T("项目已恢复，默认文件组已投递到打开命令面板前的窗口。", "The project is restored and its default file group was sent to the window that was active before the palette.")
                        : Loc.T("项目已恢复，默认文件组已放到剪贴板。", "The project is restored and its default file group is on the clipboard."),
                    sent ? 0 : 1));
                break;
            case PaletteAction.FocusResume:
            case PaletteAction.FocusResumeProject:
                if (!long.TryParse(command.Argument, out var startedAt)) break;
                _app.FocusResume(startedAt);
                break;
            case PaletteAction.PasteRecentClip when Guid.TryParse(command.Argument, out var clipId)
                && _app.Shelf.Data.Clips.FirstOrDefault(clip => clip.Id == clipId) is { } clip:
                _app.Shelf.Paste(_app.Shelf.ClipData(clip), Pasted);
                break;
            case PaletteAction.PasteFileGroup when Guid.TryParse(command.Argument, out var groupId)
                && _app.Shelf.Data.FileGroups.FirstOrDefault(group => group.Id == groupId) is { } group:
                PasteGroup(group);
                break;
            case PaletteAction.FocusPause:
                _app.FocusPause();
                break;
            case PaletteAction.FocusReset:
                _app.FocusReset();
                break;
        }
    }

    private void Pasted(bool sent)
    {
        if (sent) return;
        _app.SystemNotice(Loc.T("已复制，但未粘贴", "Copied, but not pasted"),
            Loc.T("没有找到可粘贴的窗口。内容已在剪贴板中。", "No window was available to paste into. The content is on the clipboard."), 1);
    }

    private void PasteGroup(ShelfFileGroup group)
    {
        var present = group.Paths.Where(ShelfFiles.Exists).ToList();
        int missing = group.Paths.Count - present.Count;
        if (present.Count == 0)
        {
            _app.SystemNotice(Loc.T("文件组为空", "File group is empty"),
                Loc.T("这个文件组没有仍然存在的路径。", "This file group has no paths that still exist."), 1);
            return;
        }
        _app.Shelf.Paste(ShelfService.FileData(present), sent =>
        {
            if (!sent)
                Pasted(false);
            else if (missing > 0)
                _app.SystemNotice(Loc.T("部分文件不存在", "Some files are missing"),
                    Loc.T($"已粘贴现有的 {present.Count} 个路径；另有 {missing} 个路径不存在。",
                        $"Pasted {present.Count} existing paths; {missing} paths no longer exist."), 1);
        });
    }
}
