namespace AxDeck.Core;

// AxDeck 自己画的桌面：桌面文件夹里的文件、快捷方式、文件夹，加上 Windows 自己画的系统项（此电脑、回收站），
// 按要显示的顺序排好，把 Windows 不会画的那些去掉。
//
// 这一层刻意不碰 Windows：程序画什么，只由摆明了的事实算出来（路径、名字、它是什么），
// 所以面前没有真实桌面也能验。Windows 那边的事（桌面是哪个文件夹、哪个快捷方式 Windows 会画、
// 哪些系统项现在是显示的）另外去问，问到了当作plain答案递进来。
// 唯一的例外是排序用的 StrCmpLogicalW：Explorer 排图标就用这一个函数，桌面页的顺序要跟它一致。

public static class DesktopItems
{
    // 桌面上能有的几种东西。
    public enum Kind
    {
        // 一个文件。
        File,
        // 一个文件夹。
        Folder,
        // 一个快捷方式（.lnk 文件）。
        Shortcut,
        // Windows 自己画的系统项：没有自己的文件，只有一个 shell 认识的名字。
        Shell
    }

    // 桌面上的一项。系统项的 Path 是它的解析名（"::{CLSID}"），不是文件路径。
    public readonly record struct Item(string Path, string Name, Kind Kind, bool OnDesktop)
    {
        // 这一项 AxDeck 画不画。用户从桌面上拿掉的快捷方式，Windows 不画，AxDeck 也不画：
        // 画出来等于把用户拿掉的图标又放回去了。
        public bool Visible => Kind != Kind.Shortcut || OnDesktop;
    }

    // 一个路径是什么。快捷方式是名字以 .lnk 结尾的文件，文件夹就是文件夹。
    public static Kind KindOf(string path, bool isDirectory)
    {
        if (isDirectory) return Kind.Folder;
        return System.IO.Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? Kind.Shortcut
            : Kind.File;
    }

    // 图标下面写的名字。快捷方式写它自己的名字，不写它指向的那个文件的名字：Explorer 也是这么写的。
    public static string NameOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    // 桌面的内容来自两个文件夹：当前用户自己的桌面，和所有账户共用的公共桌面。安装程序往往把快捷方式
    // 写在公共桌面上，所以只看用户桌面就会漏掉一半。Explorer 把两者合成一份画出来：同一个名字只画一个，
    // 用户桌面上的那个优先（那是用户自己放上去的那一份）。调用方先把用户桌面递进来。
    public static IReadOnlyList<Item> Merged(IEnumerable<Item> userDesktop, IEnumerable<Item> sharedDesktop)
    {
        List<Item> items = [];
        // Windows 的文件名不分大小写，两个桌面上叫 Readme 和 readme 的是同一个名字。
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (var item in userDesktop.Concat(sharedDesktop))
            if (names.Add(item.Name)) items.Add(item);
        return items;
    }

    // 要画的那些项，按画的顺序。顺序跟资源管理器一样：系统项、文件夹、其余的文件；同一档里按 Explorer
    // 的名字规则排——数字按大小算（"2" 在 "10" 前），大小写不分（见 NameOrder）。
    public static IReadOnlyList<Item> Ordered(IEnumerable<Item> items)
    {
        return [.. items
            .Where(item => item.Visible)
            .OrderBy(item => Rank(item.Kind))
            .ThenBy(item => item.Name, NameOrder)];
    }

    // Explorer 给文件名排序用的比较器。桌面上的顺序要跟它一致，不然用户在这里看到的排法跟真实桌面不一样。
    // Explorer 排图标用的就是 shlwapi 的 StrCmpLogicalW：数字段按数值比（file2 在 file10 前），名字段按
    // 系统区域比，大小写不分。Core 里其余部分刻意不碰 Windows，这一个入口是例外：排序要跟 Explorer
    // 一字不差，而 Explorer 的排序就是这一个函数，文化比较复刻不了它对数字的处理。
    public static readonly IComparer<string> NameOrder = Comparer<string>.Create((left, right) => LogicalCompare(left, right));

    private static int LogicalCompare(string left, string right)
    {
        // Windows 上问 shell 自己；万一入口给不出（理论上不会，shlwapi 从 Windows XP 起就有），
        // 退回当前文化的比较——数字段退回按字符比，总好过整页排不出去。
        if (OperatingSystem.IsWindows()) return StrCmpLogicalW(left, right);
        return System.Globalization.CompareInfo.GetCompareInfo(System.Globalization.CultureInfo.CurrentUICulture.Name)
            .Compare(left, right, System.Globalization.CompareOptions.IgnoreCase);
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string left, string right);

    // 一类东西排在哪一档：系统项 0，文件夹 1，其余 2。
    private static int Rank(Kind kind) => kind switch
    {
        Kind.Shell => 0,
        Kind.Folder => 1,
        _ => 2
    };
}