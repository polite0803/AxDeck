namespace AxDeck.Core;

// The Windows system entries that can be kept in a box: the ones the desktop shows as icons but that have no folder of their own.
// They are named by their parsing name, which Windows gives them, and each is recognized by the class id the desktop shows it
// under. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT).

// One of the system entries a box can hold.
public sealed record ShellVirtualItem(string Id, string Chinese, string English, IReadOnlyList<string> ParsingNames, string? DesktopIconClsid, bool DefaultVisible)
{
    // The name in the language in use now: the entry is named when it is shown, not when the program starts.
    public string Name => Loc.T(Chinese, English);
}

// The system entries a box can hold. The user folder is resolved for the running user, so its parsing name is not fixed here.
public static class BoxShellItems
{
    // The id of the user folder, which is a known folder and has no fixed parsing name.
    public const string UserFolder = "user-folder";

    // Every entry a box can hold. The first parsing name is the one Windows opens it by.
    public static readonly IReadOnlyList<ShellVirtualItem> All =
    [
        new("this-pc", "此电脑", "This PC", ["::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"], "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", false),
        new("recycle-bin", "回收站", "Recycle Bin", ["::{645FF040-5081-101B-9F08-00AA002F954E}"], "{645FF040-5081-101B-9F08-00AA002F954E}", true),
        new("network", "网络", "Network", ["::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}"], "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", false),
        new("control-panel", "控制面板", "Control Panel", ["::{26EE0668-A00A-44D7-9371-BEB064C98683}", "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}"], "{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", false),
        new(UserFolder, "用户文件夹", "User folder", ["::{59031A47-3F72-44A7-89C5-5595FE6B30EE}"], "{59031A47-3F72-44A7-89C5-5595FE6B30EE}", false)
    ];

    // The entry with this id, or null when the id is not one of them.
    public static ShellVirtualItem? Find(string? id) => All.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));

    // The entry the desktop shows the icon of; the ones without a class id are not on the desktop.
    public static ShellVirtualItem? FindDesktopIcon(string? id) => Find(id) is { DesktopIconClsid: { } } item ? item : null;

    // The parsing name Windows opens the entry by. The user folder is opened by its own path, which the caller resolves.
    public static string? ParsingName(string? id) => id == UserFolder ? null : Find(id)?.ParsingNames.FirstOrDefault();

    // The name Windows reports for the entry; the user folder is named by its folder.
    public static string DisplayName(string id) => Find(id)?.Name ?? id;

    // The id of the entry the given parsing name stands for, or null when it is not one of them. Windows may report an absolute
    // parsing name such as "{DesktopGuid}::{ItemGuid}", so the tail is compared as well.
    public static string? FromParsingName(string? parsingName)
    {
        if (string.IsNullOrWhiteSpace(parsingName)) return null;
        var normalized = Normalize(parsingName);
        foreach (var item in All)
            foreach (var name in item.ParsingNames)
                if (Matches(normalized, Normalize(name))) return item.Id;
        return null;
    }

    // The id of the entry the given path stands for: a system entry's parsing name is a path of its own.
    //
    // 用户文件夹是例外：它没有固定的解析名，桌面上它是一项，路径就是当前账户的 Profile 目录。所以除了
    // 按解析名认，还要拿 Profile 路径比一次——不然它在桌面页上认不出来，图标是空的。
    // 原版也是这么认的（shell_virtual_item/identity.rs：先查目录，再拿用户文件夹的 Profile 路径比）。
    public static string? FromPath(string? path)
    {
        if (FromParsingName(path) is { } known) return known;
        if (string.IsNullOrWhiteSpace(path)) return null;
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length == 0) return null;
        return Normalize(path) == Normalize(profile) ? UserFolder : null;
    }

    // The parsing name is compared without its case, its separators and its trailing backslash.
    private static string Normalize(string value) => value.Trim().TrimEnd('\\').ToLowerInvariant().Replace('/', '\\');

    // A name matches when it is the same or when it ends with it, which covers the absolute parsing names Windows hands out.
    private static bool Matches(string normalized, string expected)
        => normalized == expected || normalized.EndsWith("\\" + expected, StringComparison.Ordinal);
}
