using AxDeck.Core;

// The explicit work context: registered projects, the file store, metadata-only session links, and the palette commands that
// return to them. None of these tests write into the real AxDeck data folder.
static class ProjectTests
{
    public static void Run(Action<string, bool> check)
    {
        var book = new ProjectBook();
        var project = book.Add(@"C:\Work\AxDeck", "  AxDeck \r\n desktop ");
        var duplicate = book.Add(@"c:\work\axdeck", "Duplicate");
        book.Projects.Add(new ProjectEntry { DisplayName = "No root", RootPath = "" });
        book.Normalize();
        check("Projects: a root is kept once, ignoring case, and a name is a clean single line",
            book.Projects.Count == 1 && book.Projects[0] == project && ReferenceEquals(project, duplicate)
            && project.DisplayName == "AxDeck desktop");
        check("Projects: a missing root never becomes an entry", book.Projects.All(item => item.RootPath.Length > 0));
        var invalid = new ProjectEntry { RootPath = @"C:\x", WorkSource = ProviderId.Antigravity };
        invalid.Normalize();
        check("Projects: a source is one the session reader can watch",
            project.WorkSource == ProviderId.Codex && invalid.WorkSource == ProviderId.Codex);

        var services = new ServicesConfig
        {
            Tunnel = new TunnelConfig
            {
                Sites =
                [
                    new TunnelSite { Host = "dev.example.com", Kind = "http", Label = "Docs", Path = "/" },
                    new TunnelSite { Host = "db.example.com", Kind = "tcp", Label = "Database", Port = 15432, Proto = "tcp" },
                    new TunnelSite { Host = "", Kind = "http", Label = "Broken" },
                    new TunnelSite { Host = "unknown.example.com", Kind = "backup" },
                ],
            },
        };
        var options = ProjectServices.Options(services);
        check("Projects: only configured sites become explicit project choices",
            options.Count == 2 && options[0].Host == "dev.example.com" && options[0].Label == "Docs"
            && options[1].Host == "db.example.com" && options[1].Command.StartsWith("cloudflared access tcp"));
        check("Projects: a site choice is matched by host, case insensitively",
            ProjectServices.Find(options, "  DB.EXAMPLE.COM ") == options[1]
            && ProjectServices.Find(options, "not.example.com") is null
            && ProjectServices.Find(options, " ") is null);
        var linked = new ProjectEntry { RootPath = @"C:\work\linked", ServiceHost = " db.example.com " };
        linked.Normalize();
        check("Projects: a link stores the cleaned host without touching the project root",
            linked.ServiceHost == "db.example.com" && linked.RootPath == @"C:\work\linked");

        var many = new ProjectBook();
        for (int i = 0; i < ProjectBook.MaximumProjects; i++) many.Add($@"C:\p\{i}", $"P{i}");
        bool refused = false;
        try { many.Add(@"C:\one-too-many", "Too many"); }
        catch (InvalidOperationException) { refused = true; }
        many.Normalize();
        check("Projects: the list is bounded and refuses one more", refused && many.Projects.Count == ProjectBook.MaximumProjects);

        var directory = Path.Combine(Path.GetTempPath(), "axdeck-projects-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProjectStore(directory);
            check("Projects: saving creates projects.json and the book reads back",
                store.Save(book) && File.Exists(Path.Combine(directory, "projects.json")) && store.Load().Projects.Single().DisplayName == project.DisplayName);
            var damaged = Path.Combine(directory, "projects.json");
            File.WriteAllText(damaged, "{broken");
            var bad = store.Load();
            check("Projects: a broken file reads as empty, reports it, and is not overwritten by loading",
                bad.Projects.Count == 0 && store.LoadWarning is { Length: > 0 } && File.ReadAllText(damaged) == "{broken");
            check("Projects: an empty or missing file is not reported as damaged",
                ProjectBook.TryParse([], out var empty) && empty.Projects.Count == 0
                && new ProjectStore(Path.Combine(directory, "missing")).LoadWarning is null);
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, true); } catch (IOException) { }
        }

        var data = new ShelfData();
        var group = Shelf.AddFileGroup(data, " Review ");
        check("File groups: a group starts with a clean name and no copied files",
            group.Name == "Review" && group.Paths.Count == 0 && data.FileGroups.Single() == group);
        check("File groups: paths are ordered, ignore case for duplicates, and are bounded",
            Shelf.AddGroupPaths(group, [@"C:\a.txt", @"c:\A.TXT", @"C:\b.txt"]) == 2
            && group.Paths.SequenceEqual([@"C:\a.txt", @"C:\b.txt"])
            && Shelf.AddGroupPaths(group, Enumerable.Range(0, ShelfFileGroup.MaximumPaths + 5).Select(i => $@"C:\f{i}.txt")) > 0
            && group.Paths.Count == ShelfFileGroup.MaximumPaths);
        var shelfRead = ShelfData.Deserialize(data.Serialize());
        check("File groups: the ordered paths survive the shelf file", shelfRead.FileGroups.Single().Paths.SequenceEqual(group.Paths));

        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(8));
        var timer = new FocusTimer();
        timer.Start(now, 25, "Review", [ProviderId.Codex], project.Id);
        timer.Finish(now.AddMinutes(25));
        var read = FocusTimer.Parse(timer.Serialize(), now.AddMinutes(30));
        check("Focus timer: a project link is written with the session and read back",
            timer.ProjectId == project.Id && read.History.Single().ProjectId == project.Id);

        var context = new CommandPaletteContext(
            [new FocusResumeOption(timer.History[0], project)],
            [project],
            [new ClipEntry { Text = "curl https://example.com", Copied = now }],
            [group],
            ProviderId.Codex);
        var commands = CommandPalette.Build(new DeckSettings(), context);
        check("Command palette: a project has open and restore commands with unique IDs",
            commands.Any(command => command.Action == PaletteAction.OpenProject && command.Argument == project.Id.ToString())
            && commands.Any(command => command.Action == PaletteAction.RestoreProject && command.Argument == project.Id.ToString())
            && commands.Any(command => command.Action == PaletteAction.FocusStartProject && command.Argument == project.Id.ToString())
            && commands.Select(command => command.Id).Distinct().Count() == commands.Count);
        check("Command palette: a project's focus command carries the project name",
            CommandPalette.Search(commands, "专注 AxDeck").Any(command => command.Action == PaletteAction.FocusStartProject));
        check("Command palette: recent work can be resumed and pasted",
            commands.Any(command => command.Action == PaletteAction.FocusResumeProject && command.Argument == now.ToUnixTimeSeconds().ToString())
            && commands.Any(command => command.Action == PaletteAction.PasteRecentClip)
            && commands.Any(command => command.Action == PaletteAction.PasteFileGroup && command.Argument == group.Id.ToString()));
        check("Command palette: project commands can be found by their name and kind",
            CommandPalette.Search(commands, "AxDeck").Any(command => command.Action == PaletteAction.OpenProject)
            && CommandPalette.Search(commands, "恢复").Any(command => command.Action == PaletteAction.RestoreProject));
    }
}
