using AxDeck.Core;

namespace AxDeck;

// 桌面上那些分组窗口的管事：谁开着、谁关了、退出时把 Windows 自己的桌面图标放回去。
// 移植自 Dasktop（https://github.com/AugSakura/Dasktop, MIT），那里也是每个分组一个独立窗口。
internal static class BoxWindows
{
    // 现在开着的窗口，按分组的编号。
    private static readonly Dictionary<string, BoxWindow> Open = new(StringComparer.Ordinal);
    // 管事自己：所有的分组窗口都由它开着，所以它们一起关掉。
    private static readonly List<BoxWindow> Windows = [];

    // 打开一个分组。已经有了就把它的窗口叫到前面来。
    public static void Show(BoxService boxes, DesktopBox box)
    {
        if (Open.TryGetValue(box.Id, out var existing))
        {
            Native.WindowsHost.RaiseOnce(existing);
            return;
        }
        var window = new BoxWindow(boxes, box);
        Open[box.Id] = window;
        Windows.Add(window);
        window.Closed += (_, _) =>
        {
            Open.Remove(box.Id);
            Windows.Remove(window);
        };
        window.Show();
    }

    // 关掉一个分组的窗口。
    public static void Close(string boxId)
    {
        if (!Open.TryGetValue(boxId, out var window)) return;
        Open.Remove(boxId);
        window.Close();
    }

    // 把一个分组的图标重画一遍：它里面的东西变了。
    public static void Refresh(string boxId)
    {
        if (Open.TryGetValue(boxId, out var window)) window.Rebuild();
    }

    // 全部关掉：退出程序时用。
    public static void CloseAll()
    {
        foreach (var window in Windows.ToList())
        {
            try { window.Close(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }
        Open.Clear();
        Windows.Clear();
    }

    // 现在有几个分组开着。
    public static int Count => Open.Count;

    // 有没有分组开着。
    public static bool Any => Open.Count > 0;
}
