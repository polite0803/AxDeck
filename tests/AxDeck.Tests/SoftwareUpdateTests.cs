using AxDeck.Core;

internal static class SoftwareUpdateTests
{
    // 本机 winget v1.30.310 的真实输出，原样存在 Fixtures 下（英文、中文、零更新各一份）。
    // 逐条核对而不是只数条数：名称与 ID 之间的空格数在真实数据里并不统一，只靠条数测不出切错位。
    private static readonly (string Name, string Id, string Version, string Available)[] Real =
    [
        ("CMake", "Kitware.CMake", "4.4.0", "4.4.4"),
        ("DBeaver 26.2.1 (current user)", "DBeaver.DBeaver.Community", "26.2.1", "26.2.5"),
        ("GitHub CLI", "GitHub.cli", "2.92.0", "2.105.0"),
        ("Java(TM) SE Development Kit 27.0.1.0.3 (64-bit)", "Oracle.JDK.27", "27.0.1.0", "27.0.1.1"),
        ("llama.cpp", "ggml.llamacpp", "b10107", "b12602"),
        ("LLVM", "LLVM.LLVM", "19.1.7", "23.1.6"),
        ("Microsoft .NET SDK 10.0.204 (x64)", "Microsoft.DotNet.SDK.10", "10.0.204", "10.0.404"),
        // 名称内部有双空格，不能按空白整行切分。
        ("Microsoft Build of OpenJDK  17.0.16+8 (x64)", "Microsoft.OpenJDK.17", "17.0.16.8", "17.0.21.102"),
        ("Microsoft ODBC Driver 18 for SQL Server", "Microsoft.msodbcsql.18", "18.6.1.1", "18.7.1.1"),
        // 表里最长的 ID，与版本列之间只剩一个空格。
        ("Microsoft OLE DB Driver 19 for SQL Server", "Microsoft.SQLServer.OLEDBDriver.19", "19.4.1.0", "19.4.3.0"),
        // 名称与 ID 之间只有 1 个空格，且名称自身内含「- 12.0.30501」。
        ("Microsoft Visual C++ 2013 Redistributable (x64) - 12.0.30501", "Microsoft.VCRedist.2013.x64", "12.0.30501.0", "12.0.40664.0"),
        ("Microsoft Visual C++ v14 Redistributable (x64) - 14.50.35719", "Microsoft.VCRedist.2015+.x64", "14.50.35719.0", "14.52.37029.0"),
        ("Microsoft Visual C++ v14 Redistributable (x86) - 14.50.35719", "Microsoft.VCRedist.2015+.x86", "14.50.35719.0", "14.52.37029.0"),
        ("Outlook for Windows", "Microsoft.Outlook", "1.2026.623.400", "1.2026.812.100"),
        ("PostgreSQL 18", "PostgreSQL.PostgreSQL.18", "18.4-2", "18.7-2"),
        ("PowerShell 7.6.10.0-x64", "Microsoft.PowerShell", "7.6.10.0", "7.7.0.0"),
        ("Python Launcher", "Python.Launcher", "3.12.9", "3.15.2"),
        ("WinFsp 2025", "WinFsp.WinFsp", "2.1.25156", "2.2.26323"),
    ];

    public static void Run(Action<string, bool> check)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        WingetScan Scan(string file) => WingetTable.Parse(File.ReadAllText(Path.Combine(folder, file)));

        var english = Scan("winget-upgrade-en.txt");
        check("winget table parses every row of the English output", Matches(english, Real));
        check("a fully read table is marked complete", english.Complete && english.Unparsed.Count == 0 && english.Message is null);

        var chinese = Scan("winget-upgrade-zh.txt");
        check("winget table parses the Chinese output identically", Matches(chinese, Real));
        check("the Chinese header is recognised as a table", chinese.Complete && chinese.Found);

        var none = Scan("winget-upgrade-none.txt");
        check("no matching packages is a complete empty scan", none.Complete && !none.Found && none.Unparsed.Count == 0);
        check("the no-match sentence is kept as the message", none.Message?.Contains("找不到与输入条件匹配") == true);

        var empty = WingetTable.Parse("");
        check("empty output is not reported as complete", !empty.Complete && !empty.Found);

        var headerOnly = WingetTable.Parse("Name   Id   Version   Available\n" + new string('-', 60) + "\n");
        check("a header without rows is a complete empty scan", headerOnly.Complete && !headerOnly.Found && headerOnly.Message is null);

        var broken = WingetTable.Parse("Name   Id   Version   Available\n" + new string('-', 60) + "\nBroken.Row 1.0\n");
        check("an unreadable row makes the scan incomplete", !broken.Complete && broken.Unparsed.Count == 1);

        var miscounted = WingetTable.Parse("Name   Id   Version   Available\n" + new string('-', 60) + "\nA  B.C  1.0  2.0\n5 upgrades available.\n");
        check("a row count disagreeing with the summary makes the scan incomplete", !miscounted.Complete && miscounted.Found);

        // 版本比较：winget 报出的版本五花八门，不能只认 x.y.z。
        check("letter-suffixed build numbers compare numerically", SoftwareVersion.IsNewer("b12602", "b10107"));
        check("dashed versions compare segment by segment", SoftwareVersion.IsNewer("18.7-2", "18.4-2"));
        check("four-segment versions keep their last segment", SoftwareVersion.IsNewer("7.7.0.0", "7.6.10.0"));
        check("a missing trailing segment counts as zero", SoftwareVersion.Parse("1.15.1").CompareTo(SoftwareVersion.Parse("1.15.1.0")) == 0);
        check("an equal version is not an update", !SoftwareVersion.IsNewer("1.0", "1.0"));
        check("a lower available version is not an update", !SoftwareVersion.IsNewer("1.0", "2.0"));
        check("an unknown version never counts as an update", !SoftwareVersion.IsNewer("< 未知 >", "1.0") && !SoftwareVersion.IsNewer("1.0", "< 未知 >"));
        check("an unknown version is not comparable", !SoftwareVersion.Parse("< 未知 >").Known && !SoftwareVersion.Parse("").Known);

        RunCatalog(check);
        RunInstalledApps(check);
        RunMerge(check);
        RunStore(check);
    }

    private static void RunCatalog(Action<string, bool> check)
    {
        const string json = """
        [
          { "name": "App A", "match": { "exe": "a.exe" }, "channel": { "type": "github-release", "repo": "o/a" }, "page": "https://a.example/" },
          { "name": "App B", "match": { "arp": "B Name", "winget": "B.Id" }, "channel": { "type": "url-json", "url": "https://b.example/v.json", "versionField": "version" } },
          { "name": "No match", "channel": { "type": "github-release", "repo": "o/n" } },
          { "name": "Bad channel", "match": { "exe": "c.exe" }, "channel": { "type": "vendor-api" } },
          { "name": "No channel", "match": { "exe": "d.exe" } },
          { "match": { "exe": "e.exe" }, "channel": { "type": "github-release", "repo": "o/e" } },
          { "name": "Missing repo", "match": { "exe": "f.exe" }, "channel": { "type": "github-release" } },
          { "name": "Missing url", "match": { "exe": "g.exe" }, "channel": { "type": "url-json" } }
        ]
        """;

        var parsed = SoftwareCatalog.Parse(json);
        // 只有前两条可用：第三条没有匹配条件，后五条分别缺 channel、用了未知类型、缺 name 或缺通道参数。
        check("the catalog skips entries it cannot use", parsed.Count == 2);
        check("a github channel keeps its repo and page",
            parsed.Count == 2 && parsed[0] is { Name: "App A", Channel.Kind: SoftwareChannelKind.GitHubRelease, Channel.Repo: "o/a", Page: "https://a.example/" });
        check("a json channel keeps its url and version field",
            parsed.Count == 2 && parsed[1].Channel is { Kind: SoftwareChannelKind.UrlJson, Url: "https://b.example/v.json", VersionField: "version" });
        check("both match keys of an entry are kept",
            parsed.Count == 2 && parsed[1].Winget == "B.Id" && parsed[1].Arp == "B Name" && parsed[1].Exe is null);

        check("a broken catalog is an empty table",
            SoftwareCatalog.Parse("{]").Count == 0 && SoftwareCatalog.Parse("{}").Count == 0 &&
            SoftwareCatalog.Parse("[]").Count == 0 && SoftwareCatalog.Parse(null).Count == 0);

        // 内置表是嵌入资源：这一条同时守着 csproj 里的 EmbeddedResource 注册，漏了就是空表。
        var builtin = SoftwareCatalog.Sources;
        check("the built-in catalog is embedded", builtin.Count >= 4);
        check("the built-in catalog matches an app by its exe file name",
            SoftwareCatalog.Match(builtin, @"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe", null) is { Name: "Visual Studio Code" });
        check("the built-in catalog matches an app by its winget id",
            SoftwareCatalog.Match(builtin, null, null, "GitHub.cli") is { Name: "GitHub CLI" });

        // 匹配优先级：winget ID 精确 → exe 文件名 → ARP 显示名子串。
        var priority = SoftwareCatalog.Parse("""
        [
          { "name": "By id", "match": { "winget": "X.Y" }, "channel": { "type": "github-release", "repo": "o/i" } },
          { "name": "By exe", "match": { "exe": "x.exe" }, "channel": { "type": "github-release", "repo": "o/e" } },
          { "name": "By arp", "match": { "arp": "Shared" }, "channel": { "type": "github-release", "repo": "o/a" } }
        ]
        """);
        check("a winget id is matched first", SoftwareCatalog.Match(priority, @"C:\p\x.exe", "Shared App", "X.Y") is { Name: "By id" });
        check("an exe name is matched before the arp substring", SoftwareCatalog.Match(priority, @"C:\p\x.exe", "Shared App") is { Name: "By exe" });
        check("an arp substring is the last resort", SoftwareCatalog.Match(priority, @"C:\p\y.exe", "Shared App") is { Name: "By arp" });
        check("an app the catalog does not know has no match", SoftwareCatalog.Match(priority, @"C:\p\y.exe", "Other App") is null);

        check("a file name drops the folder, the quotes and the case",
            SoftwareCatalog.FileName("\"C:\\Program Files\\App\\App.EXE\"") == "app.exe");
        check("a file name of blank input is null",
            SoftwareCatalog.FileName("  ") is null && SoftwareCatalog.FileName(null) is null);
    }

    private static void RunInstalledApps(Action<string, bool> check)
    {
        check("a blank display name is not an installed app",
            InstalledApps.Create(null, "1.0", null, null, null) is null &&
            InstalledApps.Create("   ", "1.0", null, null, null) is null);
        check("a system component is skipped",
            InstalledApps.Create("Runtime", "1.0", null, null, null, systemComponent: true) is null);
        check("a nested entry is skipped",
            InstalledApps.Create("Nested", "1.0", null, null, null, parentKeyName: "Parent") is null);
        check("a patch is skipped by its name",
            InstalledApps.Create("Security Update for Windows 11 (KB5077181)", "", null, null, null) is null);
        check("a patch is skipped by its release type",
            InstalledApps.Create("Cumulative Update", "", null, null, null, releaseType: "Security Update") is null);

        var app = InstalledApps.Create(" DBeaver 26.2.1 ", " 26.2.1 ", " DBeaver Corp ", @"C:\Program Files\DBeaver\dbeaver.exe,0", null);
        check("a real entry keeps its trimmed fields",
            app is { Name: "DBeaver 26.2.1", Version: "26.2.1", Publisher: "DBeaver Corp" });
        check("the executable comes from the icon path", app?.Executable == "dbeaver.exe");
        check("a blank publisher becomes null", InstalledApps.Create("X", "1", "   ", null, null)?.Publisher is null);

        check("a quoted icon path still yields its executable",
            InstalledApps.ExecutableFromIcon("\"C:\\App\\App.exe\",0") == "app.exe");
        check("an icon file or a missing icon is not an executable",
            InstalledApps.ExecutableFromIcon(@"C:\App\app.ico") is null && InstalledApps.ExecutableFromIcon(null) is null);

        var distinct = InstalledApps.Distinct([
            new InstalledApp("Same App", "1.0", null, null, null),
            new InstalledApp("same app", "2.0", null, null, null),
            new InstalledApp("Another", "1.0", null, null, null),
        ]);
        check("duplicate names keep the higher version",
            distinct.Count == 2 && distinct.Any(kept => kept.Name.Equals("Same App", StringComparison.OrdinalIgnoreCase) && kept.Version == "2.0"));

        check("the alignment key drops the parenthesised suffix and the version",
            InstalledApps.Key("DBeaver 26.2.1 (current user)") == InstalledApps.Key("DBeaver"));
        check("the alignment key keeps a version-like tail on a multi-word name",
            InstalledApps.Key("Microsoft .NET SDK 10.0.204 (x64)") == "microsoft .net sdk");
        check("the alignment key of a blank name is empty",
            InstalledApps.Key(null) == "" && InstalledApps.Key("   ") == "");
    }

    private static void RunMerge(Action<string, bool> check)
    {
        var scan = new WingetScan(
            [new WingetEntry("GitHub CLI", "GitHub.cli", "2.92.0", "2.105.0")], [], true, null);
        var fromWinget = SoftwareUpdates.FromWinget(scan);
        check("winget rows become update entries",
            fromWinget.Count == 1 && fromWinget[0] is { Name: "GitHub CLI", Current: "2.92.0", Available: "2.105.0", Origin: SoftwareUpdateOrigin.Winget, Id: "GitHub.cli" });
        check("a winget row can be updated silently", SoftwareUpdates.CanUpdate(fromWinget[0]));
        check("a missing scan yields no rows", SoftwareUpdates.FromWinget(null).Count == 0);

        var app = new InstalledApp("GitHub CLI", "2.92.0", null, @"C:\Program Files\GitHub CLI\gh.exe", null);
        var source = SoftwareCatalog.Parse("""
        [{ "name": "GitHub CLI", "match": { "exe": "gh.exe" }, "channel": { "type": "github-release", "repo": "cli/cli" }, "page": "https://github.com/cli/cli/releases" }]
        """)[0];

        var catalog = SoftwareUpdates.FromCatalog(source, app, "2.105.0");
        check("a catalog hit becomes an update entry",
            catalog is { Name: "GitHub CLI", Current: "2.92.0", Available: "2.105.0", Origin: SoftwareUpdateOrigin.Catalog, Page: "https://github.com/cli/cli/releases" } &&
            catalog.Id is null && catalog.Icon == app.IconPath);
        check("a catalog hit updates through its download page", catalog is not null && SoftwareUpdates.CanUpdate(catalog));
        check("an equal catalog version is not an update", SoftwareUpdates.FromCatalog(source, app, "2.92.0") is null);
        check("a blank catalog version is not an update", SoftwareUpdates.FromCatalog(source, app, "   ") is null);

        var merged = SoftwareUpdates.Merge(fromWinget,
        [
            catalog!,
            new SoftwareUpdate("Obsidian", "1.9.10", "1.9.14", SoftwareUpdateOrigin.Catalog, Page: "https://obsidian.md/download"),
        ]);
        check("merged rows keep the winget entry for the same app and sort by name",
            merged.Count == 2 && merged[0] is { Name: "GitHub CLI", Origin: SoftwareUpdateOrigin.Winget, Id: "GitHub.cli" } && merged[1].Name == "Obsidian");
        check("the winget entry borrows the catalog's download page", merged[0].Page == "https://github.com/cli/cli/releases");
        check("a row with neither id nor page cannot be updated",
            !SoftwareUpdates.CanUpdate(new SoftwareUpdate("X", "1", "2", SoftwareUpdateOrigin.Catalog)));
    }

    private static void RunStore(Action<string, bool> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "axdeck-software-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SoftwareUpdateStore(folder);
            check("a missing cache file loads as an empty state",
                store.Load() is { CheckedAt: null, Failure: null, Updates.Count: 0 });

            var now = DateTimeOffset.Now;
            var state = new SoftwareUpdateState
            {
                CheckedAt = now,
                Installed = 42,
                Updates = [new SoftwareUpdate("DBeaver", "26.2.1", "26.2.5", SoftwareUpdateOrigin.Winget, "DBeaver.DBeaver.Community", null, @"C:\x\icon.ico")],
            };
            check("saving creates the cache file", store.Save(state) && File.Exists(store.FilePath));

            var loaded = store.Load();
            check("a saved state round-trips",
                loaded.CheckedAt == now && loaded.Installed == 42 && loaded.Complete && loaded.Updates.Count == 1);
            check("an update entry round-trips",
                loaded.Updates.Count == 1 && loaded.Updates[0] is
                {
                    Name: "DBeaver", Current: "26.2.1", Available: "26.2.5",
                    Origin: SoftwareUpdateOrigin.Winget, Id: "DBeaver.DBeaver.Community", Icon: @"C:\x\icon.ico",
                });

            check("a result younger than 12 hours is fresh", SoftwareUpdateStore.IsFresh(loaded, now + TimeSpan.FromHours(11)));
            check("a result older than 12 hours is stale", !SoftwareUpdateStore.IsFresh(loaded, now + TimeSpan.FromHours(13)));
            check("a clock turned backwards keeps the result fresh", SoftwareUpdateStore.IsFresh(loaded, now - TimeSpan.FromHours(2)));
            check("a never-checked state is not fresh", !SoftwareUpdateStore.IsFresh(new SoftwareUpdateState(), now));
            check("only a checked state has a result",
                SoftwareUpdateStore.HasResult(loaded) && !SoftwareUpdateStore.HasResult(new SoftwareUpdateState()));

            File.WriteAllText(store.FilePath, "{ not json");
            check("a corrupted cache file loads as an empty state", store.Load() is { CheckedAt: null, Updates.Count: 0 });
        }
        finally
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static bool Matches(WingetScan scan, (string Name, string Id, string Version, string Available)[] expected)
    {
        if (scan.Entries.Count != expected.Length) return false;
        for (var i = 0; i < expected.Length; i++)
        {
            var entry = scan.Entries[i];
            if (entry.Name != expected[i].Name || entry.Id != expected[i].Id ||
                entry.Version != expected[i].Version || entry.Available != expected[i].Available) return false;
        }
        return true;
    }
}
