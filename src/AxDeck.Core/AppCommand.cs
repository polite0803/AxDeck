namespace AxDeck.Core;

// What a command line or an axdeck:// address asks AxDeck to do. The parsing is plain data, so it is checked on its own.
//
// The command line of the app itself takes the same words as the address: "--settings" opens the settings, "--page music"
// opens the music page, "--focus 25" starts a countdown of 25 minutes, "--search 音乐" opens the command palette with that
// text in it. The address form is the same, written as a link: "axdeck://page/music".
public enum AppCommandKind { None, OpenSettings, OpenPage, Focus, Search, Refresh, Show, Hide }

// The scheme of the addresses AxDeck answers to.
public static class AppCommand
{
    public const string Scheme = "axdeck";

    // The command a command line or an address asks for. None when there is nothing to do.
    public static AppCommandKind Kind(string? kind) => kind switch
    {
        "settings" => AppCommandKind.OpenSettings,
        "page" => AppCommandKind.OpenPage,
        "focus" => AppCommandKind.Focus,
        "search" => AppCommandKind.Search,
        "refresh" => AppCommandKind.Refresh,
        "show" => AppCommandKind.Show,
        "hide" => AppCommandKind.Hide,
        _ => AppCommandKind.None,
    };

    // The word a command is written with in a file (AppCommandInbox), which is the word Kind reads back.
    public static string Name(AppCommandKind kind) => kind switch
    {
        AppCommandKind.OpenSettings => "settings",
        AppCommandKind.OpenPage => "page",
        AppCommandKind.Focus => "focus",
        AppCommandKind.Search => "search",
        AppCommandKind.Refresh => "refresh",
        AppCommandKind.Show => "show",
        AppCommandKind.Hide => "hide",
        _ => "",
    };

    // The page an address or a command line names, by the page's own English name ("music", "clipboard"). A page that does
    // not exist is none; the retired Boxes page cannot be named.
    public static DeckPage? Page(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        foreach (var page in Enum.GetValues<DeckPage>())
        {
            if (page == DeckPage.Boxes) continue;
            if (string.Equals(page.ToString(), text, StringComparison.OrdinalIgnoreCase)) return page;
        }
        return null;
    }

    // The minutes of a countdown: a positive whole number, or nothing.
    public static int? Minutes(string? text)
        => int.TryParse(text, out int value) && value > 0 && value <= 600 ? value : null;

    // Reads the command line of the app. The first argument that names a command decides; the ones before it are ignored.
    public static (AppCommandKind Kind, string? Argument) FromArguments(IEnumerable<string>? arguments)
    {
        var args = (arguments ?? []).Where(a => a is not null).ToList();
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i].Trim();
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string name = arg[2..];
                string? value = null;
                int equals = name.IndexOf('=');
                if (equals >= 0) { value = name[(equals + 1)..]; name = name[..equals]; }
                var kind = Kind(name.ToLowerInvariant());
                if (kind == AppCommandKind.None) continue;
                if (value is null && kind is AppCommandKind.OpenPage or AppCommandKind.Focus or AppCommandKind.Search && i + 1 < args.Count)
                {
                    value = args[++i];
                }
                return (kind, value);
            }
        }
        return (AppCommandKind.None, null);
    }

    // Reads an axdeck:// address: "axdeck://page/music" is the page music, "axdeck://focus/25" a countdown of 25 minutes.
    // The host names the command, the path names its argument; "axdeck://settings" has neither.
    public static (AppCommandKind Kind, string? Argument) FromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return (AppCommandKind.None, null);
        string text = url.Trim();
        if (!text.StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase)) return (AppCommandKind.None, null);
        string rest = text[(Scheme.Length + 3)..];
        int slash = rest.IndexOf('/');
        string host = slash < 0 ? rest : rest[..slash];
        string? argument = slash < 0 ? null : rest[(slash + 1)..].Trim();
        if (argument is not null) argument = Uri.UnescapeDataString(argument);
        var kind = Kind(host.ToLowerInvariant());
        return (kind, argument);
    }
}
