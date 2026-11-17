using System.Collections.Specialized;
using System.Windows;

namespace AxDeck;

// 从桌面页起拖时交给系统的那一份数据。
//
// 一次拖动同时是两件事：落在这一页的图标上就是排序，落到页面外面就是把这些文件交给别的程序。
// 所以数据里装两份：本页自己认的私有格式（选中项的键，排序用），和给外部程序用的 FileDrop。
// 谁认哪一份谁接走——落点在本页时 Area_Drop 只认私有格式、绝不落盘，落点在外面时外部程序只看 FileDrop。
internal static class DesktopDrag
{
    // 私有格式的名字。只在 AxDeck 自己的进程里用得上，外面的程序不认识也不用认识。
    // 载荷是 string[]：每一项的键（文件是路径，系统项是解析名），顺序就是页面上选中的先后。
    public const string Format = "AxDeck.DesktopItems";

    // keys 是所有选中的项（系统项也在内，它们在桌面上同样有位置），files 是其中真有文件的那几个。
    // 只选了系统项时 files 是空的：拖出到别的程序没有东西可给，但排序照样成立。
    public static DataObject Build(IReadOnlyList<string> keys, IReadOnlyList<string> files)
    {
        var data = new DataObject();
        // 私有格式只在「排得了序」时才写。位置读不到、开了自动排列、或者只选了系统项时 keys 是空的：
        // 这时若仍带上它，Area_DragOver 会把它当成内部拖动、发现排不了就把落点全判为 None——
        // 连拖出去都做不到了。不带它，这一下就退化成原来的「交给别的程序」，与旧行为一致。
        if (keys.Count > 0) data.SetData(Format, keys.ToArray());
        if (files.Count == 0) return data;
        var list = new StringCollection();
        list.AddRange([.. files]);
        data.SetFileDropList(list);
        return data;
    }

    // 这一份数据是不是这一页自己起的拖动。
    public static bool IsInternal(IDataObject data) => data.GetDataPresent(Format);

    // 数据里带着哪些项的键。不是这一页起的拖动时是空的。
    public static string[] Keys(IDataObject data)
        => data.GetDataPresent(Format) && data.GetData(Format) is string[] keys ? keys : [];
}