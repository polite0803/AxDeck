using AxDeck.Core;

namespace AxDeck;

// 「桌面」页的设置控制器：外观设置和 Windows 桌面图标的开关，都归 BoxService 管。
public partial class MainWindow
{
    private BoxService? _boxesService;

    // 桌面页的设置：所有页面共用一份，改了大家立刻看得见。
    internal BoxService Boxes => _boxesService ??= new BoxService();

    // 程序启动时把存档读进来。force 在设置里把它恢复默认之后用：数据整个换掉了，得重新读一遍。
    internal void BoxesLoad(bool force = false)
    {
        if (_boxesService is not null && !force) return;
        // 换掉的那一份里可能还记着被藏起来的 Windows 桌面图标：先按记录放回去，再换数据，
        // 否则那些记录跟着旧数据一起丢掉，图标就再也不会复原了。
        _boxesService?.RestoreShellIcons();
        _boxesService = new BoxService();
        _boxesService.Load(Settings.Boxes ?? new DesktopLook());
        Settings.Boxes = _boxesService.Snapshot();
    }

    // 设置里的东西变了：存一次，并让设置窗口里的这一页（若开着）重画一遍。
    internal void BoxesChanged()
    {
        if (_boxesService is null) return;
        Settings.Boxes = _boxesService.Snapshot();
        _app.QueueSave();
        _app.OrganizerChanged();
    }
}
