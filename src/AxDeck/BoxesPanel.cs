using System.Windows;
using AxDeck.Core;

namespace AxDeck;

// 「桌面整理」的控制器：分组、规则、原生桌面图标这些数据在整理器里（BoxService），
// 它不再是一个展开面板的页面，而是设置窗口里的一个页面（OrganizerSettingsPage），
// 从桌面页的右键菜单、托盘菜单和设置窗口进去。
public partial class MainWindow
{
    private BoxService? _boxesService;

    // 整理器本身：所有面板共用一份，改动大家都看得见。
    internal BoxService Boxes => _boxesService ??= new BoxService();

    // 程序启动时把保存的数据读进来：分组里收着的系统图标，在收进分组的那一刻就被藏起来了，
    // 所以数据必须比退出早一步到位，退出时才放得回来。force 在设置里把整理器恢复默认之后用：
    // 数据整个换掉了，已经读进来的那一份要扔掉重读。
    internal void BoxesLoad(bool force = false)
    {
        if (_boxesService is not null && !force) return;
        if (force)
        {
            // 换掉的那一份整理器里可能还收着系统图标：它们先放回桌面，再换数据。
            _boxesService?.RestoreShellIcons();
            BoxWindows.CloseAll();
        }
        _boxesService = new BoxService();
        _boxesService.Load(Settings.Boxes ?? new DasktopData());
        // 整理器里的东西变了：保存一次。
        Settings.Boxes = _boxesService.Snapshot();
    }

    // 整理器里的东西变了：保存一次，并让设置窗口里的整理页（若开着）重画一遍。
    internal void BoxesChanged()
    {
        if (_boxesService is null) return;
        Settings.Boxes = _boxesService.Snapshot();
        _app.QueueSave();
        _app.OrganizerChanged();
    }
}
