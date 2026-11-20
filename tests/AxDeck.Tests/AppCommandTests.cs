using AxDeck.Core;

// The commands an address or a command line asks for, and the file a second copy leaves for the running one.
static class AppCommandTests
{
    public static void Run(Action<string, bool> check)
    {
        // The addresses.
        check("Commands: an address names its command", AppCommand.FromUrl("axdeck://settings") == (AppCommandKind.OpenSettings, (string?)null));
        check("Commands: an address carries its argument", AppCommand.FromUrl("axdeck://page/music") == (AppCommandKind.OpenPage, "music"));
        check("Commands: an address opens a project", AppCommand.FromUrl("axdeck://project/D%3A%5Cwork%5CAxDeck") == (AppCommandKind.Project, @"D:\work\AxDeck"));
        check("Commands: an address restores a project", AppCommand.FromUrl("axdeck://restore-project/%2Fwork%2Faxdeck") == (AppCommandKind.RestoreProject, "/work/axdeck"));
        check("Commands: an address starts focus for a project id", AppCommand.FromUrl("axdeck://focus-project/3F2504E0-4F89-11D3-9A0C-0305E82C3301") == (AppCommandKind.FocusProject, "3F2504E0-4F89-11D3-9A0C-0305E82C3301"));
        check("Commands: an address is not case sensitive", AppCommand.FromUrl("AxDeck://PAGE/Music") == (AppCommandKind.OpenPage, "Music"));
        check("Commands: an escaped argument is read", AppCommand.FromUrl("axdeck://search/%E9%9F%B3%E4%B9%90") == (AppCommandKind.Search, "音乐"));
        check("Commands: another scheme is not an address of the app", AppCommand.FromUrl("https://example.com") == (AppCommandKind.None, (string?)null));
        check("Commands: an unknown word is nothing", AppCommand.FromUrl("axdeck://nonsense") == (AppCommandKind.None, (string?)null));

        // The command line.
        check("Commands: a command line names its command", AppCommand.FromArguments(["--settings"]) == (AppCommandKind.OpenSettings, (string?)null));
        check("Commands: a command line carries its argument", AppCommand.FromArguments(["--page", "monitor"]) == (AppCommandKind.OpenPage, "monitor"));
        check("Commands: a project path can be joined with =", AppCommand.FromArguments([@"--project=C:\work\AxDeck"]) == (AppCommandKind.Project, @"C:\work\AxDeck"));
        check("Commands: a project can be opened by a separate argument", AppCommand.FromArguments(["--project", @"D:\OneManager\AxDeck"]) == (AppCommandKind.Project, @"D:\OneManager\AxDeck"));
        check("Commands: a project focus can be started by a separate argument", AppCommand.FromArguments(["--focus-project", "3F2504E0-4F89-11D3-9A0C-0305E82C3301"]) == (AppCommandKind.FocusProject, "3F2504E0-4F89-11D3-9A0C-0305E82C3301"));
        check("Commands: an argument can be joined with =", AppCommand.FromArguments(["--focus=45"]) == (AppCommandKind.Focus, "45"));
        check("Commands: other arguments are passed over", AppCommand.FromArguments(["C:\\some path\\x.txt", "--refresh"]) == (AppCommandKind.Refresh, (string?)null));
        check("Commands: a command line that names none is nothing", AppCommand.FromArguments(["--unknown"]) == (AppCommandKind.None, (string?)null));
        check("Commands: no command line at all is nothing", AppCommand.FromArguments(null) == (AppCommandKind.None, (string?)null));

        // The names.
        check("Commands: a page is named by its own name", AppCommand.Page("music") == DeckPage.Music && AppCommand.Page("CLIPBOARD") == DeckPage.Clipboard);
        check("Commands: a page that does not exist is none", AppCommand.Page("boxes") is null && AppCommand.Page("nonsense") is null && AppCommand.Page(null) is null);
        check("Commands: the minutes are a positive whole number", AppCommand.Minutes("25") == 25 && AppCommand.Minutes("0") is null && AppCommand.Minutes("-5") is null && AppCommand.Minutes("text") is null);

        // The file a second copy leaves.
        var directory = Path.Combine(Path.GetTempPath(), "axdeck-command-" + Guid.NewGuid().ToString("N"));
        try
        {
            check("Commands: nothing is waiting before anything is written", AppCommandInbox.Read(directory) is null);
            AppCommandInbox.Write(directory, AppCommandKind.OpenPage, "music");
            var read = AppCommandInbox.Read(directory);
            check("Commands: the command survives the file", read is { Kind: AppCommandKind.OpenPage, Argument: "music" });
            check("Commands: the command is taken away once it is read", AppCommandInbox.Read(directory) is null);
            AppCommandInbox.Write(directory, AppCommandKind.Refresh);
            check("Commands: a command without an argument reads back without one", AppCommandInbox.Read(directory) is { Kind: AppCommandKind.Refresh, Argument: null });
            AppCommandInbox.Write(directory, AppCommandKind.None);
            check("Commands: nothing is written for no command", !File.Exists(Path.Combine(directory, AppCommandInbox.FileName)));
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
        }
    }
}
