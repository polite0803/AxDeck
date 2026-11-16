using AxDeck.Core;

internal static class DesktopTests
{
    public static void Run(Action<string, bool> check)
    {
        // 一个路径是什么东西：文件夹就是文件夹，快捷方式是名字以 .lnk 结尾的文件。
        check("文件夹算文件夹",
            DesktopItems.KindOf(@"C:\Users\me\Desktop\Work", isDirectory: true) == DesktopItems.Kind.Folder);
        check("以 .lnk 结尾的文件算快捷方式",
            DesktopItems.KindOf(@"C:\Users\me\Desktop\Git Bash.lnk", isDirectory: false) == DesktopItems.Kind.Shortcut);
        check("快捷方式的结尾不管大小写",
            DesktopItems.KindOf(@"C:\Users\me\Desktop\Git Bash.LNK", isDirectory: false) == DesktopItems.Kind.Shortcut);
        check("别的文件都是普通文件",
            DesktopItems.KindOf(@"C:\Users\me\Desktop\notes.txt", isDirectory: false) == DesktopItems.Kind.File);
        check("只是名字叫 lnk 的文件还是普通文件",
            DesktopItems.KindOf(@"C:\Users\me\Desktop\lnk", isDirectory: false) == DesktopItems.Kind.File);

        // 图标下面写的是这一项自己的名字，不是它指向的那个东西的名字。
        check("一项的名字就是它自己的文件名",
            DesktopItems.NameOf(@"C:\Users\me\Desktop\notes.txt") == "notes.txt");
        check("快捷方式写它自己的名字",
            DesktopItems.NameOf(@"C:\Users\me\Desktop\WeChat.lnk") == "WeChat.lnk");
        check("没有名字的路径就用整个路径",
            DesktopItems.NameOf("") == "");

        // 用户从桌面上拿掉的快捷方式不画：画出来等于把他拿掉的图标又放回去了。
        var hidden = new DesktopItems.Item(@"C:\D\hidden.lnk", "hidden.lnk", DesktopItems.Kind.Shortcut, OnDesktop: false);
        var shown = new DesktopItems.Item(@"C:\D\shown.lnk", "shown.lnk", DesktopItems.Kind.Shortcut, OnDesktop: true);
        var plain = new DesktopItems.Item(@"C:\D\notes.txt", "notes.txt", DesktopItems.Kind.File, OnDesktop: false);
        var folder = new DesktopItems.Item(@"C:\D\Work", "Work", DesktopItems.Kind.Folder, OnDesktop: false);
        var shell = new DesktopItems.Item("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "此电脑", DesktopItems.Kind.Shell, OnDesktop: false);
        check("不在桌面上的快捷方式不画", !hidden.Visible);
        check("在桌面上的快捷方式照画", shown.Visible);
        check("普通文件总是画", plain.Visible);
        check("文件夹总是画", folder.Visible);
        check("系统项总是画", shell.Visible);

        // 画的顺序：系统项最前，文件夹其次，最后是其余的文件；同一档里由名字定，跟资源管理器一样。
        var ordered = DesktopItems.Ordered([
            new(@"C:\D\notes.txt", "notes.txt", DesktopItems.Kind.File, true),
            new(@"C:\D\Work", "Work", DesktopItems.Kind.Folder, true),
            new(@"C:\D\apple.lnk", "apple.lnk", DesktopItems.Kind.Shortcut, true),
            new(@"C:\D\Banana.lnk", "Banana.lnk", DesktopItems.Kind.Shortcut, true),
            new(@"C:\D\Archive", "Archive", DesktopItems.Kind.Folder, true),
            new(@"C:\D\hidden.lnk", "hidden.lnk", DesktopItems.Kind.Shortcut, false),
            new("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "此电脑", DesktopItems.Kind.Shell, true),
            new("::{645FF040-5081-101B-9F08-00AA002F954E}", "回收站", DesktopItems.Kind.Shell, true)
        ]);
        check("不画的那些项被去掉了",
            ordered.All(item => item.Name != "hidden.lnk") && ordered.Count == 7);
        check("系统项排在最前",
            ordered.Take(2).All(item => item.Kind == DesktopItems.Kind.Shell));
        check("系统项之间也由名字定",
            ordered[0].Name == "回收站" && ordered[1].Name == "此电脑");
        check("文件夹接着系统项",
            ordered.Skip(2).Take(2).All(item => item.Kind == DesktopItems.Kind.Folder)
            && ordered[2].Name == "Archive" && ordered[3].Name == "Work");
        check("同一档里由名字定",
            ordered.Skip(4).Select(item => item.Name).SequenceEqual(["apple.lnk", "Banana.lnk", "notes.txt"]));
        check("名字不分大小写地比，不是按字符码",
            ordered.Skip(4).Select(item => item.Name).First() == "apple.lnk");

        // 桌面上什么都没有不是错误：这一页就是没东西可画。
        check("空桌面什么都不画", DesktopItems.Ordered([]).Count == 0);

        // 桌面上看到的是两个文件夹合起来的一份：用户自己的桌面，和所有账户共用的公共桌面。
        // 安装程序把快捷方式装在公共桌面上，只看用户桌面就会漏掉它们。
        var userSide = new List<DesktopItems.Item>
        {
            new(@"C:\Users\me\Desktop\notes.txt", "notes.txt", DesktopItems.Kind.File, true)
        };
        var publicSide = new List<DesktopItems.Item>
        {
            new(@"C:\Users\Public\Desktop\WeChat.lnk", "WeChat.lnk", DesktopItems.Kind.Shortcut, true),
            new(@"C:\Users\Public\Desktop\Work", "Work", DesktopItems.Kind.Folder, true)
        };
        var merged = DesktopItems.Merged(userSide, publicSide);
        check("公共桌面上的东西也画出来",
            merged.Count == 3 && merged.Any(item => item.Name == "WeChat.lnk" && item.Path.StartsWith(@"C:\Users\Public\")));
        check("合并后用户桌面上的那些还在", merged.Any(item => item.Name == "notes.txt"));

        // 两个桌面上有同一个名字时只画一个，画的用户自己桌面上的那一份：那是用户自己放上去的。
        var clashUser = new List<DesktopItems.Item>
        {
            new(@"C:\Users\me\Desktop\Teams.lnk", "Teams.lnk", DesktopItems.Kind.Shortcut, true)
        };
        var clashPublic = new List<DesktopItems.Item>
        {
            new(@"C:\Users\Public\Desktop\Teams.lnk", "Teams.lnk", DesktopItems.Kind.Shortcut, true)
        };
        var deduped = DesktopItems.Merged(clashUser, clashPublic);
        check("两个桌面上同名的项只画一个",
            deduped.Count == 1 && deduped[0].Path == @"C:\Users\me\Desktop\Teams.lnk");
        // Windows 的文件名不分大小写，Readme 和 readme 是同一个名字。
        check("同名的判断不分大小写",
            DesktopItems.Merged(
                [new(@"C:\Users\me\Desktop\Teams.lnk", "Teams.lnk", DesktopItems.Kind.Shortcut, true)],
                [new(@"C:\Users\Public\Desktop\teams.LNK", "teams.LNK", DesktopItems.Kind.Shortcut, true)]).Count == 1);
        check("没有公共桌面时照旧只画用户的", DesktopItems.Merged(userSide, []).Count == 1);
        check("用户桌面读不到时公共桌面上的照画", DesktopItems.Merged([], publicSide).Count == 2);

        // ---- 接管桌面 ----

        // 图标本来是显示的，用户要接管：记住"原来是显示的"，然后藏起来。
        var taken = DesktopTakeOver.Take(iconsVisible: true, wanted: true);
        check("接管时记下原来是显示的", taken.WasVisible && taken.Taken);
        check("接管时该去藏图标", DesktopTakeOver.ShouldHide(taken));
        check("接管过就该在退出时写回去", DesktopTakeOver.ShouldRestore(taken));
        check("写回去的是接管前那个样子", DesktopTakeOver.RestoreTo(taken));

        // 图标本来就藏着：那不是 AxDeck 藏起来的，退出时也不该由 AxDeck 去显示。
        var already = DesktopTakeOver.Take(iconsVisible: false, wanted: true);
        check("本来就没显示时不去动它", !already.Taken);
        check("本来就没显示时不藏图标", !DesktopTakeOver.ShouldHide(already));
        check("本来就没显示时退出不写回", !DesktopTakeOver.ShouldRestore(already));

        // 用户在设置里关了接管：真实桌面照旧，AxDeck 什么都不动。
        var declined = DesktopTakeOver.Take(iconsVisible: true, wanted: false);
        check("不要接管时什么都不做", !declined.Taken && !DesktopTakeOver.ShouldHide(declined) && !DesktopTakeOver.ShouldRestore(declined));

        // 没接管过的状态：退出时什么也不该写。
        var untouched = DesktopTakeOver.State.Untouched;
        check("从没接管过时退出不写回", !DesktopTakeOver.ShouldRestore(untouched));
        check("从没接管过时不藏图标", !DesktopTakeOver.ShouldHide(untouched));

        // 收回之后又是一张白纸：再接管一次照样从头记。
        check("收回之后不再写回",
            !DesktopTakeOver.ShouldRestore(DesktopTakeOver.State.Untouched)
            && DesktopTakeOver.Take(iconsVisible: true, wanted: true).Taken);

        // 被强制结束（任务管理器结束进程、崩溃、断电）：退出那一步没跑，桌面图标还藏着。
        // 记号就是这件事的证据：下次启动看到它，先把图标放回去。
        check("上一次没交还时下次启动要放回去", DesktopTakeOver.NeedsRecovery(markerExists: true));
        check("交还干净了就不用管", !DesktopTakeOver.NeedsRecovery(markerExists: false));
        check("真藏了图标才落记号", DesktopTakeOver.ShouldMark(taken));
        check("本来就没显示时不落记号", !DesktopTakeOver.ShouldMark(already));
        check("不要接管时不落记号", !DesktopTakeOver.ShouldMark(declined));
        check("没接管过时不落记号", !DesktopTakeOver.ShouldMark(DesktopTakeOver.State.Untouched));
    }
}
