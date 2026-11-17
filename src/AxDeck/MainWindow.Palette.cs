using System.Windows;
using AxDeck.Core;

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
            case PaletteAction.FocusPause:
                _app.FocusPause();
                break;
            case PaletteAction.FocusReset:
                _app.FocusReset();
                break;
        }
    }
}
