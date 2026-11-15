namespace AxDeck.Core;

// AxDeck 自己画的桌面：桌面文件夹里的文件、快捷方式、文件夹，加上 Windows 自己画的系统项（此电脑、回收站），
// 按要显示的顺序排好，把 Windows 不会画的那些去掉。
//
// 这一层刻意不碰 Windows：程序画什么，只由摆明了的事实算出来（路径、名字、它是什么），
// 所以面前没有真实桌面也能验。Windows 那边的事（桌面是哪个文件夹、哪个快捷方式 Windows 会画、
// 哪些系统项现在是显示的）另外去问，问到了当作plain答案递进来。

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

    // 要画的那些项，按画的顺序。顺序跟资源管理器一样：文件夹在前，文件夹之间、其余之间都由名字定。
    // 系统项（此电脑、回收站）排在最前：Windows 的桌面也是把它们摆在文件前面的。
    public static IReadOnlyList<Item> Ordered(IEnumerable<Item> items)
    {
        return [.. items
            .Where(item => item.Visible)
            .OrderBy(item => Rank(item.Kind))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)];
    }

    // 一类东西排在哪一档：系统项 0，文件夹 1，其余 2。
    private static int Rank(Kind kind) => kind switch
    {
        Kind.Shell => 0,
        Kind.Folder => 1,
        _ => 2
    };
}