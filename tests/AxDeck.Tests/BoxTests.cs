using AxDeck.Core;

internal static class BoxTests
{
    // 桌面页搬动真实文件的那一套：Windows 不接受的名字，以及一个文件拖进来会变成什么、落在哪里。
    public static void Run(string root, Action<string, bool> check)
    {
        // The names Windows will not take.
        check("Windows keeps its own names for itself",
            BoxNaming.SanitizeUserFileName("CON") is null && BoxNaming.SanitizeUserFileName("con.txt") is null
            && BoxNaming.SanitizeUserFileName("LPT1") is null && BoxNaming.SanitizeUserFileName("..") is null
            && BoxNaming.SanitizeUserFileName("") is null);
        check("a name with a character Windows does not take is refused",
            BoxNaming.SanitizeUserFileName("a:b") is null && BoxNaming.SanitizeUserFileName("a/b") is null
            && BoxNaming.SanitizeUserFileName("a*b") is null && BoxNaming.SanitizeUserFileName("a\"b") is null);
        check("a name Windows does take is kept, without its trailing dots and spaces",
            BoxNaming.SanitizeUserFileName("  a.txt  ") == "a.txt" && BoxNaming.SanitizeUserFileName("a.txt...") == "a.txt");

        // The plan for a file dragged onto the desktop page.
        var folder = Path.Combine(root, "box-target");
        Directory.CreateDirectory(folder);
        var source = Path.Combine(root, "note.txt");
        File.WriteAllText(source, "hello");
        var copy = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        check("a file dragged in is copied to the folder under its own name",
            copy.Plans.Count == 1 && copy.Plans[0].Kind == BoxTransferKind.Copy
            && Path.GetFileName(copy.Plans[0].Destination) == "note.txt");
        var mapped = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Map, BoxConflictPolicy.Rename);
        check("a mapped file becomes a shortcut to where it is",
            mapped.Plans.Count == 1 && mapped.Plans[0].Kind == BoxTransferKind.Shortcut
            && Path.GetFileName(mapped.Plans[0].Destination) == "note.lnk");
        var moved = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Move, BoxConflictPolicy.Rename);
        check("a file dragged in is moved to the folder",
            moved.Plans.Count == 1 && moved.Plans[0].Kind == BoxTransferKind.Move);
        // 已经在目标文件夹里的文件再拖回这一页：移动它什么也不变，复制它只会多出一份 "note (2)" 副本——都不做。
        var inside = Path.Combine(folder, "note.txt");
        File.WriteAllText(inside, "already there");
        check("a file that is already in the folder is left alone, whatever the drag means",
            BoxTransferPlanner.Plan([inside], folder, BoxDropAction.Move, BoxConflictPolicy.Rename).Plans.Count == 0
            && BoxTransferPlanner.Plan([inside], folder, BoxDropAction.Copy, BoxConflictPolicy.Rename).Plans.Count == 0);

        // The same name twice: the rule decides what happens.
        File.WriteAllText(Path.Combine(folder, "note.txt"), "old");
        var skip = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Skip);
        check("a file of the same name is skipped when the rule says so", skip.Plans.Count == 0);
        var replace = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Replace);
        check("a file of the same name replaces the old one when the rule says so",
            replace.Plans.Count == 1 && replace.Plans[0].ReplacesExisting);
        var renamed = BoxTransferPlanner.Plan([source], folder, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        check("a file of the same name is renamed when the rule says so",
            renamed.Plans.Count == 1 && Path.GetFileName(renamed.Plans[0].Destination) == "note (2).txt");

        // The system entries the desktop page draws.
        check("the system entries a box can hold are the five Windows shows",
            BoxShellItems.All.Count == 5 && BoxShellItems.Find("this-pc") is not null
            && BoxShellItems.FindDesktopIcon("this-pc")?.DesktopIconClsid == "{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
        check("a parsing name is told apart from the entry it stands for",
            BoxShellItems.FromParsingName("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}") == "this-pc"
            && BoxShellItems.FromParsingName(@"C:\Windows\System32") is null);
        // 用户文件夹在桌面上是一项，但它的路径是当前账户的 Profile 目录，不是 ::{GUID}。
        // 按解析名认不出来它，桌面页就取不到它的图标——所以 FromPath 还要拿 Profile 路径比一次。
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        check("the user folder is told apart by its profile path, which has no parsing name",
            profile.Length > 0 && BoxShellItems.ParsingName(BoxShellItems.UserFolder) is null
            && BoxShellItems.FromPath(profile) == BoxShellItems.UserFolder
            && BoxShellItems.FromPath(profile + @"\") == BoxShellItems.UserFolder
            && BoxShellItems.FromPath(@"C:\Windows\System32") is null);

        // The space between two icons: the two sliders set it, and it is room between the places, not part of them.
        check("the gap between two icons of a row is the one the user set",
            BoxLayout.ColumnGap(new DesktopLookSettings { IconGapX = 12 }) == 12
            && BoxLayout.ColumnGap(new DesktopLookSettings { IconGapX = 6 }) == 6);
        check("the gap between two rows is the one the user set",
            BoxLayout.RowGap(new DesktopLookSettings { IconGapY = 10 }) == 10);
        check("a place an icon takes does not grow with the gap",
            BoxLayout.ColumnWidth(new DesktopLookSettings { IconGapX = 30, IconSize = 48, FilenameWidth = 82 })
            == BoxLayout.ColumnWidth(new DesktopLookSettings { IconGapX = 0, IconSize = 48, FilenameWidth = 82 }));
        check("the step of one column is the place plus the gap",
            BoxLayout.ColumnStride(new DesktopLookSettings { IconGapX = 6 }) == BoxLayout.ColumnWidth(new DesktopLookSettings { IconGapX = 6 }) + 6);
        check("the step of one row is the height of a row plus the gap",
            BoxLayout.RowStride(new DesktopLookSettings { IconGapY = 4 }) == BoxLayout.RowHeight(new DesktopLookSettings { IconGapY = 4 }) + 4);

        SettingsFromNewerVersion(root, check);
    }

    // 设置文件来自更新的版本时，一步迁移都不跑，读的是默认值。那不是丢设置的理由——文件里带着这一版
    // 上次自己保存的快照（Snapshots[这一版]），读的时候要拿它，而不是从头来过。
    private static void SettingsFromNewerVersion(string root, Action<string, bool> check)
    {
        var store = new SettingsStore(System.IO.Path.Combine(root, "settings-from-the-future"), "1.3.1");
        var settings = store.Load();
        settings.DesktopTakeOver = true;
        settings.DesktopWidth = 1033;
        settings.Language = Loc.Chinese;
        store.Save(settings);
        var file = System.IO.Path.Combine(store.DirectoryPath, "settings.json");
        var own = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        own["AppVersion"] = "1.3.1";
        // 高版本多出来的键：这一版不认识，不该因为读了一次就丢掉。
        var newer = new System.Text.Json.Nodes.JsonObject
        {
            ["AppVersion"] = "1.4.0",
            ["NotchWidth"] = 300,
            ["Snapshots"] = new System.Text.Json.Nodes.JsonObject { ["1.3.1"] = own }
        };
        File.WriteAllText(file, newer.ToJsonString());

        var read = new SettingsStore(store.DirectoryPath, "1.3.1").Load();
        check("a settings file from a newer version reads the settings this version saved in it last",
            read.DesktopTakeOver && read.DesktopWidth == 1033 && read.Language == Loc.Chinese);
    }
}
