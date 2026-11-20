namespace AxDeck.Core;

// The command palette: the list of things AxDeck can be told to do, and the way a typed text finds them. The list itself is
// plain data, so it can be checked here; the panel that shows it lives in the app.
//
// The commands are what the panel can do at once: the pages it shows, the ways it can be looked at, the actions the tray and
// the hotkeys offer, and the focus timer. A command that does not apply right now (a page that is switched off) is not in the
// list at all.
public enum PaletteAction
{
    // The argument names the page (DeckPage) or the minutes of a focus countdown.
    Page, ToggleCompactStyle, ToggleLineStyle, RefreshUsage, OpenSettings, ToggleHidden, ToggleClickThrough,
    FocusOpen, FocusStart, FocusPause, FocusReset,
    FocusResume, FocusResumeProject, OpenProject, RestoreProject, PasteRecentClip, PasteFileGroup,
    FocusStartProject,
}

// One thing the panel can be told to do. The title is what is shown; the keywords are what the text is matched against, in
// both languages, so that a Chinese query finds the English title and the other way round.
public sealed record PaletteCommand(string Id, string Title, string Hint, PaletteAction Action, string? Argument = null)
{
    // The texts the query is matched against: the title, the hint and the keywords.
    public string[] Keywords { get; init; } = [];
}

// A completed focus session and the project it named. The palette only carries the metadata already saved by FocusTimer;
// it does not read a conversation or inspect the project folder.
public sealed record FocusResumeOption(FocusSession Session, ProjectEntry? Project = null);

// The local work context the palette can offer besides its static commands. Every list is optional, so callers that only
// know the settings keep the original palette.
public sealed record CommandPaletteContext(
    IReadOnlyList<FocusResumeOption>? RecentSessions = null,
    IReadOnlyList<ProjectEntry>? Projects = null,
    IReadOnlyList<ClipEntry>? RecentClips = null,
    IReadOnlyList<ShelfFileGroup>? FileGroups = null,
    ProviderId? CurrentWorkSource = null);

// How well a command answers a query: the higher, the better. A word that begins the title beats a word inside it, which beats
// a word only the keywords hold.
public static class PaletteMatch
{
    public const int Prefix = 100, Contains = 60, Keyword = 40;

    // The score of one word against one command: 0 when it does not match at all.
    public static int Score(PaletteCommand command, string word)
    {
        if (word.Length == 0) return 0;
        string title = command.Title.ToLowerInvariant();
        if (title.StartsWith(word, StringComparison.Ordinal)) return Prefix;
        if (title.Contains(word, StringComparison.Ordinal)) return Contains;
        return command.Keywords.Any(key => key.Contains(word, StringComparison.Ordinal)) ? Keyword : 0;
    }
}

// The command list and the search over it.
public static class CommandPalette
{
    // The most commands shown at once; a long list would push the rest off the panel.
    public const int Limit = 8;

    // The commands of the current state: the pages that are shown, the actions that are always there, and the focus timer's.
    public static List<PaletteCommand> Build(DeckSettings settings) => Build(settings, null);

    public static List<PaletteCommand> Build(DeckSettings settings, CommandPaletteContext? context)
    {
        var commands = new List<PaletteCommand>();

        // Work first when there is a context: the reason to open the palette is often to return to a project, not to visit a
        // settings page. The entries come from explicit records and carry stable IDs for the window to execute.
        foreach (var project in (context?.Projects ?? []).Take(ProjectBook.MaximumProjects))
        {
            commands.Add(new PaletteCommand($"project:open:{project.Id}", Loc.T($"打开项目：{project.DisplayName}", $"Open project: {project.DisplayName}"),
                project.RootPath, PaletteAction.OpenProject, project.Id.ToString())
            { Keywords = ["project", "folder", "项目", "目录", "打开", project.DisplayName, project.RootPath] });
            commands.Add(new PaletteCommand($"project:restore:{project.Id}", Loc.T($"恢复工作：{project.DisplayName}", $"Restore work: {project.DisplayName}"),
                Loc.T("打开项目程序，并准备默认文件组", "Open the project app and prepare its default file group"), PaletteAction.RestoreProject, project.Id.ToString())
            { Keywords = ["restore", "resume", "work", "恢复", "继续", "工作", project.DisplayName, project.RootPath] });
            commands.Add(new PaletteCommand($"project:focus:{project.Id}", Loc.T($"开始项目专注：{project.DisplayName}", $"Focus on project: {project.DisplayName}"),
                Loc.T("使用项目的名称和 AI 工作来源开始 25 分钟计时", "Starts 25 minutes with the project's name and AI work source"),
                PaletteAction.FocusStartProject, project.Id.ToString())
            { Keywords = ["focus", "timer", "project", "专注", "计时", "项目", project.DisplayName, project.RootPath] });
        }

        foreach (var option in (context?.RecentSessions ?? []).OrderByDescending(item => item.Session.EndedAt).Take(6))
        {
            var session = option.Session;
            string label = session.Label.Length > 0 ? session.Label : Loc.T("最近的专注会话", "Recent focus session");
            string argument = session.StartedAt.ToUnixTimeSeconds().ToString();
            var action = session.ProjectId is null ? PaletteAction.FocusResume : PaletteAction.FocusResumeProject;
            commands.Add(new PaletteCommand($"focus:resume:{argument}", Loc.T($"继续专注：{label}", $"Resume focus: {label}"),
                Loc.T($"{Math.Max(1, session.TargetSeconds / 60)} 分钟 · {Shelf.When(session.EndedAt, DateTimeOffset.Now)}",
                    $"{Math.Max(1, session.TargetSeconds / 60)} min · {Shelf.When(session.EndedAt, DateTimeOffset.Now)}"),
                action, argument)
            { Keywords = ["focus", "resume", "session", "专注", "继续", "会话", label, option.Project?.DisplayName ?? ""] });
        }

        foreach (var clip in (context?.RecentClips ?? []).Where(clip => clip.Kind != ClipKind.Image).Take(6))
        {
            var (first, _) = Shelf.FirstLine(clip.Text);
            if (first.Length == 0) continue;
            string title = first.Length > 42 ? first[..42] : first;
            commands.Add(new PaletteCommand($"clip:paste:{clip.Id}", Loc.T($"粘贴最近内容：{title}", $"Paste recent: {title}"),
                Loc.T("粘贴到打开命令面板前的窗口", "Paste into the window that was active before the palette"),
                PaletteAction.PasteRecentClip, clip.Id.ToString())
            { Keywords = ["clipboard", "paste", "recent", "剪贴板", "粘贴", "最近", clip.Text] });
        }

        foreach (var group in (context?.FileGroups ?? []).Take(ShelfFileGroup.MaximumGroups))
        {
            commands.Add(new PaletteCommand($"files:paste:{group.Id}", Loc.T($"粘贴文件组：{group.ShownName}", $"Paste file group: {group.ShownName}"),
                Loc.T($"{group.Paths.Count} 个文件", $"{Loc.Count(group.Paths.Count, "file", "files")}"),
                PaletteAction.PasteFileGroup, group.Id.ToString())
            { Keywords = ["files", "group", "paste", "文件", "文件组", "粘贴", group.ShownName] });
        }

        // The pages that are shown, in the order of the page switch.
        foreach (var page in DeckPages.Visible(settings))
        {
            string title = PageTitle(page);
            commands.Add(new PaletteCommand("page:" + page, title, Loc.T("切换到这一页", "Go to this page"), PaletteAction.Page, page.ToString())
            { Keywords = PageKeywords(page) });
        }

        commands.Add(new PaletteCommand("style:cycle", Loc.T("切换收起样式", "Switch the collapsed style"),
            Loc.T("在刘海、胶囊与指示条之间切换", "Cycle between the notch, the capsule and the bar"), PaletteAction.ToggleCompactStyle)
        { Keywords = ["style", "compact", "收起", "样式", "刘海", "胶囊", "指示条"] });
        commands.Add(new PaletteCommand("style:line", Loc.T("指示条与刘海互换", "Turn the bar into the notch or capsule"),
            Loc.T("在指示条与刘海或胶囊之间切换", "Swap between the bar and the notch or capsule"), PaletteAction.ToggleLineStyle)
        { Keywords = ["line", "bar", "指示条", "刘海", "胶囊"] });
        commands.Add(new PaletteCommand("usage:refresh", Loc.T("立即同步用量", "Sync usage now"),
            Loc.T("马上读取一次 AI 用量", "Reads the AI usage once, now"), PaletteAction.RefreshUsage)
        { Keywords = ["refresh", "sync", "刷新", "同步", "用量"] });
        commands.Add(new PaletteCommand("app:settings", Loc.T("打开设置", "Open the settings"),
            Loc.T("打开设置窗口", "Opens the settings window"), PaletteAction.OpenSettings)
        { Keywords = ["settings", "设置", "偏好", "options"] });
        commands.Add(new PaletteCommand("app:hidden", Loc.T("隐藏或显示面板", "Hide or show the panels"),
            Loc.T("把所有面板收进通知区域", "Hides every panel into the notification area"), PaletteAction.ToggleHidden)
        { Keywords = ["hide", "show", "隐藏", "显示"] });
        commands.Add(new PaletteCommand("app:clickthrough", Loc.T("鼠标穿透开关", "Mouse pass-through on or off"),
            Loc.T("让鼠标穿过面板", "Lets the mouse pass through the panel"), PaletteAction.ToggleClickThrough)
        { Keywords = ["click", "through", "穿透", "鼠标"] });

        // The focus timer: the window, and the three things that can be done to the countdown.
        commands.Add(new PaletteCommand("focus:open", Loc.T("专注计时", "Focus timer"),
            Loc.T("打开专注计时窗口", "Opens the focus timer window"), PaletteAction.FocusOpen)
        { Keywords = ["focus", "timer", "专注", "计时"] });
        foreach (var minutes in FocusTimer.Presets)
        {
            int value = minutes;
            commands.Add(new PaletteCommand($"focus:start:{value}", Loc.T($"专注 {value} 分钟", $"Focus for {value} minutes"),
                Loc.T("开始计时", "Starts the countdown"), PaletteAction.FocusStart, value.ToString())
            { Keywords = ["focus", "timer", "专注", "计时", "start"] });
        }
        commands.Add(new PaletteCommand("focus:pause", Loc.T("暂停计时", "Pause the countdown"),
            Loc.T("暂停正在走的计时", "Pauses the running countdown"), PaletteAction.FocusPause)
        { Keywords = ["focus", "timer", "专注", "计时", "pause"] });
        commands.Add(new PaletteCommand("focus:reset", Loc.T("重置计时", "Reset the countdown"),
            Loc.T("把计时放回开头", "Puts the countdown back to the start"), PaletteAction.FocusReset)
        { Keywords = ["focus", "timer", "专注", "计时", "reset"] });

        return commands;
    }

    // The page's name, in the language in use.
    public static string PageTitle(DeckPage page) => page switch
    {
        DeckPage.Work => Loc.T("开发工作", "Development work"),
        DeckPage.Usage => Loc.T("AI 用量", "AI usage"),
        DeckPage.Music => Loc.T("音乐", "Music"),
        DeckPage.Monitor => Loc.T("系统监控", "System monitor"),
        DeckPage.Clipboard => Loc.T("剪贴板与文件中转", "Clipboard and file transfer"),
        DeckPage.Devices => Loc.T("设备电量", "Device batteries"),
        DeckPage.Services => Loc.T("服务与工具", "Services and tools"),
        DeckPage.Desktop => Loc.T("桌面", "Desktop"),
        _ => page.ToString(),
    };

    // The words that find a page, in both languages.
    private static string[] PageKeywords(DeckPage page) => page switch
    {
        DeckPage.Work => ["work", "project", "focus", "工作", "项目", "专注", "开发"],
        DeckPage.Usage => ["usage", "quota", "用量", "额度", "积分"],
        DeckPage.Music => ["music", "player", "音乐", "播放器", "歌曲"],
        DeckPage.Monitor => ["monitor", "system", "cpu", "gpu", "监控", "系统", "硬件"],
        DeckPage.Clipboard => ["clipboard", "shelf", "transfer", "剪贴板", "文件中转", "中转"],
        DeckPage.Devices => ["devices", "battery", "设备", "电量", "耳机", "键盘", "鼠标"],
        DeckPage.Services => ["services", "tunnel", "bge", "服务", "隧道", "工具"],
        DeckPage.Desktop => ["desktop", "icons", "桌面", "图标"],
        _ => [],
    };

    // The commands a query answers, best first. An empty query gives the first commands of the list. A word is one part of
    // the query, separated by spaces; every word must match, so "音乐 页面" narrows the music page down.
    public static List<PaletteCommand> Search(IEnumerable<PaletteCommand> commands, string? query, int limit = Limit)
    {
        var list = commands.ToList();
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => word.ToLowerInvariant()).Where(word => word.Length > 0).ToList();
        if (words.Count == 0) return list.Take(limit).ToList();

        var scored = new List<(PaletteCommand Command, int Score, int Length)>();
        foreach (var command in list)
        {
            int total = 0;
            bool all = true;
            foreach (var word in words)
            {
                int score = PaletteMatch.Score(command, word);
                if (score == 0) { all = false; break; }
                total += score;
            }
            if (all) scored.Add((command, total, command.Title.Length));
        }
        // The better score first; the shorter title first among equals, so the plain command comes before the long one.
        return scored.OrderByDescending(entry => entry.Score).ThenBy(entry => entry.Length).ThenBy(entry => entry.Command.Id)
            .Take(limit).Select(entry => entry.Command).ToList();
    }
}
