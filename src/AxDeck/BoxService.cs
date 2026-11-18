using AxDeck.Core;

namespace AxDeck;

// 「桌面」页的数据：它长什么样（图标大小、间距、名字怎么写、拖进来遇到同名怎么办），以及 Windows 自己画的那几枚
// 桌面图标现在显示还是藏着。
//
// 早先这里的「桌面整理」是每个分组一扇独立的小窗口，各自收着一批文件；那套已经整套删掉，只剩桌面页。
// 数据仍然存在 DeckSettings.Boxes 里，键名没改，老存档照样读得回来。
internal sealed class BoxService
{
    // The data as it is saved.
    public DesktopLook Data { get; private set; } = new();
    // 「桌面」页的外观设置。
    public DesktopLookSettings Settings => Data.Settings;

    // Reads the saved data, and puts back any native icon an older version hid: the boxes that held them are gone, so nothing
    // should keep them hidden now.
    public void Load(DesktopLook data)
    {
        Data = data;
        RestoreShellIcons();
    }

    // The data as it is saved.
    public DesktopLook Snapshot() => Data;

    // ---- The native desktop icons ----

    // The state of the native icons, for the page to show.
    public bool ShellIconVisible(string shellId)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return true;
        var item = Core.BoxShellItems.FindDesktopIcon(shellId);
        return Native.ShellDesktopIcons.IsVisible(clsid, item?.DefaultVisible ?? true);
    }

    // Shows or hides the native icon of a system entry by hand.
    public void SetShellIconVisible(string shellId, bool visible)
    {
        var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
        if (clsid.Length == 0) return;
        Native.ShellDesktopIcons.SetVisible(clsid, visible);
        if (visible) Data.IconRecords.Remove(shellId);
    }

    // Puts every native icon an older version hid back the way it was found, and forgets the record of it. Called when the data
    // is read and when the program goes, so Windows is not left without an icon nobody is showing now.
    public void RestoreShellIcons()
    {
        foreach (var shellId in Data.IconRecords.Keys.ToList())
        {
            var clsid = Native.ShellDesktopIcons.ClsidOf(shellId);
            if (clsid.Length == 0) { Data.IconRecords.Remove(shellId); continue; }
            Native.ShellDesktopIcons.SetVisible(clsid, Data.IconRecords[shellId].PreviousVisible);
            Data.IconRecords.Remove(shellId);
        }
    }
}
