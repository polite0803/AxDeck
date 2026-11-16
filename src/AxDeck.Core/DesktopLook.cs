using System.Text.Json.Serialization;

namespace AxDeck.Core;

// 「桌面」页长什么样：图标多大、间距多少、名字怎么写、拖进来的文件遇到同名怎么办。
//
// 这些东西早年是给「桌面整理」的分组窗口用的（那个项目叫 Dasktop，类型名也跟着叫 Dasktop）；分组已经整套删掉了，
// 只剩这一页在用，所以类型也改了名。存档里的键没有动：DeckSettings.Boxes 和它里面的 Settings 都还是原来的名字，
// 老文件里的图标大小、间距照样读得回来。

// 「桌面」页的一项设置：图标下的名字写成什么样。
public enum BoxNameDisplay { Full, HideShortcutExtension, HideAllExtensions }

// 这几项设置写在存档里的样子，换一个版本也读得回来。
public static class DesktopLookCodes
{
    public const string Full = "full", HideShortcutExtension = "hideShortcutExtension", HideAllExtensions = "hideAllExtensions";
    public const string Rename = "rename", Skip = "skip", Replace = "replace";

    // 名字的写法：认不出来的值（别的版本写的、文件被人改过）按默认来，不能让一个坏值决定文件怎么显示。
    public static BoxNameDisplay Display(string? code) => code switch
    {
        Full => BoxNameDisplay.Full, HideAllExtensions => BoxNameDisplay.HideAllExtensions, _ => BoxNameDisplay.HideShortcutExtension
    };
    public static string Code(BoxNameDisplay display) => display switch
    {
        BoxNameDisplay.Full => Full, BoxNameDisplay.HideAllExtensions => HideAllExtensions, _ => HideShortcutExtension
    };

    // 同名文件的处理：同样认不出来的按「自动改名」，那一条不会覆盖用户的文件。
    public static BoxConflictPolicy Conflict(string? code) => code switch
    {
        Skip => BoxConflictPolicy.Skip, Replace => BoxConflictPolicy.Replace, _ => BoxConflictPolicy.Rename
    };
    public static string Code(BoxConflictPolicy policy) => policy switch
    {
        BoxConflictPolicy.Skip => Skip, BoxConflictPolicy.Replace => Replace, _ => Rename
    };
}

// 「桌面」页的全部外观设置，随 DeckSettings 一起保存。
//
// 存档里的这一层叫 Settings：老版本就是这样嵌套的，改名会让用户已经调过的图标大小和间距读不回来。
public sealed class DesktopLookSettings
{
    // 图标下的名字：全写、快捷方式不写 .lnk、什么都不写扩展名。
    public string NameDisplayMode { get; set; } = DesktopLookCodes.HideShortcutExtension;
    // 从别处拖一个文件到桌面上，桌面上已经有同名的：自动改名、跳过，还是替换。
    public string ConflictPolicy { get; set; } = DesktopLookCodes.Rename;
    // 画不画图标下的名字。
    public bool ShowItemLabels { get; set; } = true;
    // 快捷方式左下角带不带那个小箭头。
    public bool ShowShortcutArrow { get; set; } = true;
    // 每一项的圆角，选中时的边框也照它画。
    public int CornerRadius { get; set; } = 8;
    public int IconSize { get; set; } = 48;
    public int LabelTextSize { get; set; } = 12;
    // 两项之间的空当。它是格子与格子之间的空地，不算进格子的宽度里。
    public int IconGapX { get; set; } = 6;
    public int IconGapY { get; set; } = 4;
    // 名字最宽能到多少，超了就换行，两行还放不下就截断。
    public int FilenameWidth { get; set; } = 82;

    [JsonIgnore] public BoxConflictPolicy Conflict => DesktopLookCodes.Conflict(ConflictPolicy);
    [JsonIgnore] public BoxNameDisplay NameDisplay => DesktopLookCodes.Display(NameDisplayMode);
}

// 存在 DeckSettings.Boxes 里的那一份数据。
public sealed class DesktopLook
{
    public DesktopLookSettings Settings { get; set; } = new();

    // 老版本里分组把系统图标藏起来时记下的「原来是什么样」。分组已经删了，这份记录不再产生，但老存档里可能还有：
    // 启动时照它把图标放回去再清空，不然一张被上次运行藏起来的图标会一直藏着。
    public Dictionary<string, ShellIconRecord> IconRecords { get; set; } = [];
}

// 老存档里记着的一枚原生桌面图标：它原来是显示还是不显示。
public sealed class ShellIconRecord
{
    public bool PreviousVisible { get; set; } = true;
    public bool ManagedHidden { get; set; }
}
