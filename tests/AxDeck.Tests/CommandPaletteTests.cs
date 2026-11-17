using AxDeck.Core;

// The command palette: the list of commands, and the way a typed text finds them. All of it is plain data, so it is checked here.
static class CommandPaletteTests
{
    public static void Run(Action<string, bool> check)
    {
        var settings = new DeckSettings();
        var commands = CommandPalette.Build(settings);

        check("Command palette: the shown pages are commands", commands.Any(command => command.Action == PaletteAction.Page && command.Argument == DeckPage.Music.ToString()));
        check("Command palette: the actions are there too",
            commands.Any(command => command.Action == PaletteAction.OpenSettings) &&
            commands.Any(command => command.Action == PaletteAction.RefreshUsage) &&
            commands.Any(command => command.Action == PaletteAction.ToggleCompactStyle));

        // A page that is switched off is not offered: the command would do nothing.
        var hidden = settings.Copy();
        hidden.MusicPage = false;
        var withoutMusic = CommandPalette.Build(hidden);
        check("Command palette: a page that is off is not offered", !withoutMusic.Any(command => command.Action == PaletteAction.Page && command.Argument == DeckPage.Music.ToString()));
        check("Command palette: the pages that are on are still offered", withoutMusic.Any(command => command.Action == PaletteAction.Page && command.Argument == DeckPage.Monitor.ToString()));

        // The focus timer's own commands.
        check("Command palette: the focus timer is offered", commands.Any(command => command.Action == PaletteAction.FocusOpen));
        check("Command palette: the focus timer has a command for each length",
            FocusTimer.Presets.All(minutes => commands.Any(command => command.Action == PaletteAction.FocusStart && command.Argument == minutes.ToString())));

        // The search.
        var found = CommandPalette.Search(commands, "音乐");
        check("Command palette: a Chinese word finds the music page", found.Count > 0 && found[0].Action == PaletteAction.Page && found[0].Argument == DeckPage.Music.ToString());
        var english = CommandPalette.Search(commands, "music");
        check("Command palette: an English word finds the music page too", english.Count > 0 && english[0].Action == PaletteAction.Page && english[0].Argument == DeckPage.Music.ToString());
        check("Command palette: a keyword finds the command too", CommandPalette.Search(commands, "quota").Any(command => command.Action == PaletteAction.Page && command.Argument == DeckPage.Usage.ToString()));
        check("Command palette: a word that fits nothing finds nothing", CommandPalette.Search(commands, "zzzznotaword").Count == 0);
        check("Command palette: an empty query gives the first commands", CommandPalette.Search(commands, "").Count == CommandPalette.Limit);

        // Every word of the query has to match.
        var both = CommandPalette.Search(commands, "音乐 播放");
        check("Command palette: both words have to match", both.Count > 0 && both[0].Action == PaletteAction.Page && both[0].Argument == DeckPage.Music.ToString());
        check("Command palette: a second word that fits nothing rules the command out", CommandPalette.Search(commands, "音乐 zzzznotaword").Count == 0);

        // The beginning of the title wins over a word inside it.
        var scored = CommandPalette.Search(commands, "设置");
        check("Command palette: the word that begins the title comes first",
            scored.Count > 0 && scored[0].Action == PaletteAction.OpenSettings);
        check("Command palette: the results are limited", CommandPalette.Search(commands, "a", 3).Count <= 3);
    }
}
