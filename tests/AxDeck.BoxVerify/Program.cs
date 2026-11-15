using System.IO;
using AxDeck;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck.BoxVerify;

// 桌面整理的端到端验证：真的注册表、真的文件系统、真的 BoxService。
// 每一条断言都检查一个真实的副作用，而不是内存里的模型。
internal static class Program
{
    private static int _failed;
    // 被这个程序动过的注册表值，最后要原样放回去。
    private static readonly List<(string Path, string Name, int? Before)> _registryTouched = [];

    private static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) { Console.WriteLine($"PASS: {name}"); return; }
        _failed++;
        Console.WriteLine($"FAIL: {name}{(detail is null ? "" : " -- " + detail)}");
    }

    private static int Main()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "axdeck-boxverify-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        try { Run(sandbox); }
        catch (Exception ex) { _failed++; Console.WriteLine($"FAIL: an exception escaped -- {ex}"); }
        finally
        {
            RestoreRegistry();
            try { Directory.Delete(sandbox, true); } catch { /* 沙盒留在临时目录也无妨 */ }
        }
        Console.WriteLine(_failed == 0 ? "ALL CHECKS PASSED" : $"{_failed} CHECKS FAILED");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string sandbox)
    {
        var root = Path.Combine(sandbox, "boxes");
        var desktop = Path.Combine(sandbox, "desktop");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(desktop);

        BoxFolders(sandbox, root);
        TransferIn(sandbox, root);
        Conflict(sandbox);
        MixedOrder(sandbox, root);
        TransferOut(sandbox, root, desktop);
        BetweenBoxes(sandbox, root);
        DeletePolicy(sandbox, root, desktop);
        ShellIcons(sandbox, root);
    }

    // 一个新的整理器，沙盒里的分组根目录。
    private static BoxService NewService(string root, Action<DasktopSettings>? setup = null)
    {
        var settings = new DasktopSettings { CollectionRoot = root };
        setup?.Invoke(settings);
        var service = new BoxService();
        service.Load(new DasktopData { Settings = settings });
        return service;
    }

    // ---- 分组的真实文件夹 ----
    private static void BoxFolders(string sandbox, string root)
    {
        var boxes = NewService(root);
        var box = boxes.Create("验证分组");
        Check("新建的分组有真的文件夹", Directory.Exists(box.FolderPath), box.FolderPath);
        Check("分组文件夹的名字是按编号来的，不是按标题",
            Path.GetFileName(box.FolderPath).StartsWith("box_") && !Path.GetFileName(box.FolderPath).Contains("验证分组"),
            Path.GetFileName(box.FolderPath));
        Check("分组文件夹在设定的根目录下面", box.FolderPath.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    // ---- 拖入：复制 / 移动 / 映射 ----
    private static void TransferIn(string sandbox, string root)
    {
        var source = Path.Combine(sandbox, "note.txt");
        File.WriteAllText(source, "hello");

        var copyBox = NewService(root, s => s.DropAction = BoxPolicyCodes.Copy).Create("复制组");
        var copied = BoxTransferExecutor.Execute([source], copyBox.FolderPath, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        Check("拖入为复制：分组里有了文件，源文件还在",
            copied.Count == 1 && File.Exists(copied[0]) && File.Exists(source), string.Join(",", copied));
        Check("拖入为复制：源文件的内容没有变", File.ReadAllText(source) == "hello");

        var moveBox = NewService(root, s => s.DropAction = BoxPolicyCodes.Move).Create("移动组");
        var movedSource = Path.Combine(sandbox, "moving.txt");
        File.WriteAllText(movedSource, "move me");
        var moved = BoxTransferExecutor.Execute([movedSource], moveBox.FolderPath, BoxDropAction.Move, BoxConflictPolicy.Rename);
        Check("拖入为移动：文件到了分组里，源位置空了",
            moved.Count == 1 && File.Exists(moved[0]) && !File.Exists(movedSource), string.Join(",", moved));

        var mapBox = NewService(root, s => s.DropAction = BoxPolicyCodes.Map).Create("映射组");
        var mapSource = Path.Combine(sandbox, "mapped.txt");
        File.WriteAllText(mapSource, "map me");
        var mapped = BoxTransferExecutor.Execute([mapSource], mapBox.FolderPath, BoxDropAction.Map, BoxConflictPolicy.Rename);
        Check("拖入为映射：分组里是快捷方式，源文件还在",
            mapped.Count == 1 && File.Exists(mapped[0]) && File.Exists(mapSource)
            && Path.GetExtension(mapped[0]).Equals(".lnk", StringComparison.OrdinalIgnoreCase), string.Join(",", mapped));
    }

    // ---- 同名处理：改名 / 跳过 / 替换 ----
    private static void Conflict(string sandbox)
    {
        var conflictRoot = Path.Combine(sandbox, "conflict");
        Directory.CreateDirectory(conflictRoot);
        var target = Path.Combine(conflictRoot, "same.txt");
        File.WriteAllText(target, "old");
        var incoming = Path.Combine(sandbox, "same.txt");
        File.WriteAllText(incoming, "new");

        var renamed = BoxTransferExecutor.Execute([incoming], conflictRoot, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        Check("同名且规则是改名：新文件另起一个名字，旧文件还在",
            renamed.Count == 1 && !Path.GetFileName(renamed[0]).Equals("same.txt", StringComparison.OrdinalIgnoreCase)
            && File.ReadAllText(target) == "old", string.Join(",", renamed));

        var skipped = BoxTransferExecutor.Execute([incoming], conflictRoot, BoxDropAction.Copy, BoxConflictPolicy.Skip);
        Check("同名且规则是跳过：什么也没做，旧文件没变",
            skipped.Count == 0 && File.ReadAllText(target) == "old");

        var replaced = BoxTransferExecutor.Execute([incoming], conflictRoot, BoxDropAction.Copy, BoxConflictPolicy.Replace);
        Check("同名且规则是替换：旧文件被新的盖上",
            replaced.Count == 1 && File.ReadAllText(target) == "new", string.Join(",", replaced));
    }

    // ---- 混合顺序：真文件和系统项在同一张表里 ----
    private static void MixedOrder(string sandbox, string root)
    {
        // 这一个整理器不开隐藏图标，免得动到真的注册表。
        var mixed = NewService(root, s => s.AutoHideShellIcons = false);
        var mixedBox = mixed.Create("混合组");
        var realFile = Path.Combine(mixedBox.FolderPath, "real.txt");
        File.WriteAllText(realFile, "x");
        mixed.AddVirtualItem(mixedBox.Id, "this-pc");

        var mixedKeys = mixed.ItemKeys(mixedBox.Id);
        Check("系统项和真文件在同一张顺序表里",
            mixedKeys.Contains(BoxItemKey.ForShell("this-pc")) && mixedKeys.Contains(realFile),
            string.Join(" | ", mixedKeys));
        Check("真文件和系统项分别问得出来",
            mixed.ItemPaths(mixedBox.Id).Count == 1 && mixed.VirtualItems(mixedBox.Id).Single() == "this-pc");

        // 改顺序：顺序表变了，真文件没动。
        var order = mixedKeys.ToList();
        mixed.SaveItemOrder(mixedBox.Id, [realFile, .. order.Where(key => key != realFile)]);
        Check("改了顺序，顺序表变了，真文件还在原处",
            File.Exists(realFile) && mixed.ItemKeys(mixedBox.Id).ToList().IndexOf(realFile) == 0);

        // 组内拖动：只改顺序，不动真实文件。
        var second = Path.Combine(mixedBox.FolderPath, "second.txt");
        File.WriteAllText(second, "y");
        mixed.SaveItemOrder(mixedBox.Id, [realFile, second, BoxItemKey.ForShell("this-pc")]);
        mixed.MoveItemTo(mixedBox.Id, second, 0);
        Check("组内拖动：顺序变了，真文件没动",
            mixed.ItemKeys(mixedBox.Id).ToList().IndexOf(second) == 0
            && File.Exists(second) && File.Exists(realFile), string.Join(" | ", mixed.ItemKeys(mixedBox.Id)));

        // 重命名：文件真的改了名，顺序表跟着改。
        var renamed = mixed.Rename(mixedBox.Id, second, "renamed.txt");
        Check("重命名：文件真的改了名", renamed && File.Exists(Path.Combine(mixedBox.FolderPath, "renamed.txt")) && !File.Exists(second));
        Check("重命名：顺序表跟着改了名",
            mixed.ItemKeys(mixedBox.Id).Any(key => key.EndsWith("renamed.txt", StringComparison.OrdinalIgnoreCase)));

        // 重命名成 Windows 不收的名字：拒绝，文件不动。
        var before = mixed.ItemKeys(mixedBox.Id).ToList();
        Check("重命名成 Windows 不收的名字：被拒绝，文件没动",
            !mixed.Rename(mixedBox.Id, Path.Combine(mixedBox.FolderPath, "renamed.txt"), "CON")
            && mixed.ItemKeys(mixedBox.Id).SequenceEqual(before));
    }

    // ---- 拖出到桌面 ----
    private static void TransferOut(string sandbox, string root, string desktop)
    {
        // 拖出为移动：文件离开分组，落到桌面上。
        var service = NewService(root, s => { s.DragOutAction = BoxPolicyCodes.Move; s.AutoHideShellIcons = false; });
        var box = service.Create("拖出组");
        var inside = Path.Combine(box.FolderPath, "out.txt");
        File.WriteAllText(inside, "out");
        var landed = service.DragOut(box.Id, [inside], desktop);
        Check("拖出为移动：文件到了桌面上，分组里没了",
            landed.Count == 1 && File.Exists(landed[0]) && !File.Exists(inside), string.Join(",", landed));
        Check("拖出为移动：桌面上的文件在桌面的目录里",
            Path.GetDirectoryName(landed[0])!.Equals(desktop, StringComparison.OrdinalIgnoreCase));

        // 拖出为复制：文件留在分组里，桌面上也有一份。
        var copyService = NewService(root, s => { s.DragOutAction = BoxPolicyCodes.Copy; s.AutoHideShellIcons = false; });
        var copyBox = copyService.Create("拖出复制组");
        var kept = Path.Combine(copyBox.FolderPath, "kept.txt");
        File.WriteAllText(kept, "kept");
        var copied = copyService.DragOut(copyBox.Id, [kept], desktop);
        Check("拖出为复制：桌面上有了一份，分组里的还在",
            copied.Count == 1 && File.Exists(copied[0]) && File.Exists(kept), string.Join(",", copied));

        // 拖出为映射：桌面上是快捷方式，分组里的还在。
        var mapService = NewService(root, s => { s.DragOutAction = BoxPolicyCodes.Map; s.AutoHideShellIcons = false; });
        var mapBox = mapService.Create("拖出映射组");
        var mapped = Path.Combine(mapBox.FolderPath, "mapped.txt");
        File.WriteAllText(mapped, "mapped");
        var mapLanded = mapService.DragOut(mapBox.Id, [mapped], desktop);
        Check("拖出为映射：桌面上是快捷方式，分组里的还在",
            mapLanded.Count == 1 && File.Exists(mapLanded[0]) && File.Exists(mapped)
            && Path.GetExtension(mapLanded[0]).Equals(".lnk", StringComparison.OrdinalIgnoreCase), string.Join(",", mapLanded));
    }

    // ---- 跨分组拖放 ----
    private static void BetweenBoxes(string sandbox, string root)
    {
        var service = NewService(root, s => { s.DropAction = BoxPolicyCodes.Move; s.AutoHideShellIcons = false; });
        var source = service.Create("来源组");
        var target = service.Create("目标组");
        var file = Path.Combine(source.FolderPath, "between.txt");
        File.WriteAllText(file, "between");

        // 拖到目标组的某个项之前。
        var anchor = Path.Combine(target.FolderPath, "anchor.txt");
        File.WriteAllText(anchor, "anchor");
        var landed = service.MoveBetweenBoxes(source.Id, target.Id, [file], anchor, false);
        Check("跨分组拖放：文件真的搬到了目标组", landed.Count == 1 && File.Exists(landed[0]) && !File.Exists(file));
        Check("跨分组拖放：文件在目标组的文件夹里",
            Path.GetDirectoryName(landed[0])!.Equals(target.FolderPath, StringComparison.OrdinalIgnoreCase));
        Check("跨分组拖放：文件落在了放下的地方（锚点之前）",
            service.ItemKeys(target.Id).ToList().IndexOf(landed[0]) < service.ItemKeys(target.Id).ToList().IndexOf(anchor),
            string.Join(" | ", service.ItemKeys(target.Id)));

        // 系统项跨分组：只搬引用，不动文件。
        service.AddVirtualItem(source.Id, "network");
        service.MoveBetweenBoxes(source.Id, target.Id, [BoxItemKey.ForShell("network")], null, false);
        Check("跨分组拖放系统项：来源组不再有它，目标组有了",
            !service.VirtualItems(source.Id).Contains("network") && service.VirtualItems(target.Id).Contains("network"));

        // 开关关着的时候，收进分组不该动注册表：整理器没有接管过这些图标。
        var networkClsid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";
        var hidePath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
        var networkBefore = ReadValue(hidePath, networkClsid);
        Check("开关关着的时候，收进分组不往注册表里造值",
            networkBefore is null, $"注册表里已经有值 {networkBefore}");
    }

    // ---- 删除分组的三种规则 ----
    private static void DeletePolicy(string sandbox, string root, string desktop)
    {
        // 内容移回桌面。
        var moveService = NewService(root, s => { s.DeletePolicy = BoxPolicyCodes.MoveContentsToDesktop; s.AutoHideShellIcons = false; });
        var moveBox = moveService.Create("删除移回组");
        var content = Path.Combine(moveBox.FolderPath, "back.txt");
        File.WriteAllText(content, "back");
        moveService.Delete(moveBox, desktop);
        Check("删除分组（内容移回桌面）：文件回到了桌面上",
            File.Exists(Path.Combine(desktop, "back.txt")), string.Join(",", Directory.GetFiles(desktop)));
        Check("删除分组（内容移回桌面）：分组从列表里没了", moveService.Find(moveBox.Id) is null);

        // 保留文件夹。
        var keepService = NewService(root, s => { s.DeletePolicy = BoxPolicyCodes.KeepFolder; s.AutoHideShellIcons = false; });
        var keepBox = keepService.Create("删除保留组");
        var kept = Path.Combine(keepBox.FolderPath, "kept.txt");
        File.WriteAllText(kept, "kept");
        keepService.Delete(keepBox, desktop);
        Check("删除分组（保留文件夹）：文件夹和文件都还在", Directory.Exists(keepBox.FolderPath) && File.Exists(kept));

        // 桌面上有同名文件时，移回来的那个改名，不会覆盖。
        var clashService = NewService(root, s => { s.DeletePolicy = BoxPolicyCodes.MoveContentsToDesktop; s.AutoHideShellIcons = false; });
        var clashBox = clashService.Create("删除撞名组");
        var clash = Path.Combine(clashBox.FolderPath, "back.txt");
        File.WriteAllText(clash, "from box");
        clashService.Delete(clashBox, desktop);
        Check("删除分组：桌面上已有同名文件时，移回来的那个改了名，没有覆盖",
            File.ReadAllText(Path.Combine(desktop, "back.txt")) == "back"
            && File.ReadAllText(Path.Combine(desktop, "back (2).txt")) == "from box",
            string.Join(",", Directory.GetFiles(desktop)));
    }

    // ---- 原生桌面图标：真的注册表 ----
    private static void ShellIcons(string sandbox, string root)
    {
        // 用回收站来验：它在 Windows 11 上默认是显示的，所以能验出真的「显示 -> 藏起来 -> 显示回来」。
        var shellId = "recycle-bin";
        var clsid = "{645FF040-5081-101B-9F08-00AA002F954E}";
        var newStart = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel";
        var classic = @"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\ClassicStartMenu";

        // 记下原样，最后要还原回去。
        var before = ReadValue(newStart, clsid);
        var beforeClassic = ReadValue(classic, clsid);
        _registryTouched.Add((newStart, clsid, before));
        _registryTouched.Add((classic, clsid, beforeClassic));
        // 原来的经典开始菜单那处整个键都没有，还原时要把键删掉。
        var classicKeyExisted = Registry.CurrentUser.OpenSubKey(classic) is not null;

        // 记下原来看得见看不见：注册表里 0 是显示、1 是藏起来，值不在时是 Windows 自己的默认。
        bool WasVisible(int? value) => value is null
            ? BoxShellItems.Find(shellId)!.DefaultVisible
            : value.Value == 0;
        var wasVisible = WasVisible(before);

        try
        {
            var service = NewService(root, s => s.AutoHideShellIcons = true);
            var box = service.Create("图标组");

            // 收进分组：图标被藏起来，两个地方都写了。
            service.AddVirtualItem(box.Id, shellId);
            Check("收进分组后，回收站的桌面图标在新开始菜单那处被藏起来",
                ReadValue(newStart, clsid) == 1, $"值是 {ReadValue(newStart, clsid)}");
            Check("收进分组后，回收站的桌面图标在经典开始菜单那处也被藏起来",
                ReadValue(classic, clsid) == 1, $"值是 {ReadValue(classic, clsid)}");
            Check("页面读到它是不显示的", !service.ShellIconVisible(shellId));
            Check("第一次收进分组时记住了图标原来的样子", service.Data.IconRecords.ContainsKey(shellId));
            Check("记住的样子就是原来那个样子",
                service.Data.IconRecords[shellId].PreviousVisible == wasVisible, $"记的是 {service.Data.IconRecords[shellId].PreviousVisible}");

            // 移出分组：图标放回来，看得见看不见回到原来的样子。
            service.RemoveVirtualItem(box.Id, shellId);
            Check("移出分组后，回收站的桌面图标回到原来的样子",
                WasVisible(ReadValue(newStart, clsid)) == wasVisible,
                $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
            Check("移出分组后，回收站的桌面图标真的看得见了", wasVisible && ReadValue(newStart, clsid) == 0);
            Check("移出分组后，记录也销掉了", !service.Data.IconRecords.ContainsKey(shellId));

            // 退出时归还。
            service.AddVirtualItem(box.Id, shellId);
            Check("再收一次，图标又被藏起来", ReadValue(newStart, clsid) == 1);
            service.RestoreShellIcons();
            Check("退出时归还，回收站的桌面图标回到原来的样子",
                WasVisible(ReadValue(newStart, clsid)) == wasVisible && ReadValue(newStart, clsid) == 0,
                $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
            Check("退出时归还，记录也销掉了", !service.Data.IconRecords.ContainsKey(shellId));

            // 开关关掉：所有被管过的图标都放回来。
            service.AddVirtualItem(box.Id, shellId);
            Check("再收一次，图标又被藏起来", ReadValue(newStart, clsid) == 1);
            service.Settings.AutoHideShellIcons = false;
            service.SyncShellIcons();
            Check("开关关掉后，图标放回来了",
                WasVisible(ReadValue(newStart, clsid)) == wasVisible && ReadValue(newStart, clsid) == 0,
                $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
            Check("开关关掉后，记录也销掉了", !service.Data.IconRecords.ContainsKey(shellId));

            // 启动时的同步：保存的数据里有这个分组，重开时图标仍然是藏的。
            var saved = service.Snapshot();
            var savedBox = saved.Boxes[0];
            saved.VirtualItems[savedBox.Id] = [shellId];
            saved.Settings.AutoHideShellIcons = true;
            var reopened = new BoxService();
            reopened.Load(saved);
            Check("重开程序时，被分组收着的图标仍然是藏的", ReadValue(newStart, clsid) == 1, $"值是 {ReadValue(newStart, clsid)}");

            // 原来记住的样子不能被覆盖：第一次接管时的状态要一直留着。
            var record = reopened.Data.IconRecords.TryGetValue(shellId, out var found) ? found : null;
            Check("重开程序时，图标原来的样子没有被覆盖成新的",
                record is not null && record.PreviousVisible == wasVisible,
                record is null ? "没有记录" : $"记的是 {record.PreviousVisible}，原来 {wasVisible}");

            reopened.RestoreShellIcons();
            Check("重开后再退出，图标回到原来的样子",
                WasVisible(ReadValue(newStart, clsid)) == wasVisible && ReadValue(newStart, clsid) == 0,
                $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");

            // 两个分组都收着同一个图标：一个放出去，另一个还收着，图标就还藏着。
            var shared = NewService(root, s => s.AutoHideShellIcons = true);
            var first = shared.Create("甲组");
            var second = shared.Create("乙组");
            shared.AddVirtualItem(first.Id, shellId);
            shared.AddVirtualItem(second.Id, shellId);
            shared.RemoveVirtualItem(first.Id, shellId);
            Check("两个分组都收着同一个图标：一个放出去，另一个还收着，图标还藏着",
                ReadValue(newStart, clsid) == 1, $"值是 {ReadValue(newStart, clsid)}");
            shared.RemoveVirtualItem(second.Id, shellId);
            Check("两个分组都放出去，图标才回到原来的样子",
                WasVisible(ReadValue(newStart, clsid)) == wasVisible && ReadValue(newStart, clsid) == 0,
                $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
        }
        finally
        {
            // 还原注册表：不管验成什么样，都不留痕迹。经典开始菜单那处原本整个键都没有，就把它删掉。
            WriteValue(newStart, clsid, before);
            WriteValue(classic, clsid, beforeClassic);
            if (!classicKeyExisted) Registry.CurrentUser.DeleteSubKeyTree(classic, false);
        }
    }

    // 注册表里的一个值；没有这个值就是 null。
    private static int? ReadValue(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) is int value ? value : null;
    }

    // 写注册表里的一个值；null 就是把值删掉。这个函数不会凭空造出键：键不在时什么也不做。
    private static void WriteValue(string path, string name, int? value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, true);
        if (key is null) return;
        if (value is null) key.DeleteValue(name, false);
        else key.SetValue(name, value.Value, RegistryValueKind.DWord);
    }

    // 把这个程序动过的注册表值原样放回去。
    private static void RestoreRegistry()
    {
        foreach (var (path, name, before) in _registryTouched)
        {
            try { WriteValue(path, name, before); }
            catch (Exception ex) { Console.WriteLine($"WARN: 注册表没能还原 {path}\\{name} -- {ex.Message}"); }
        }
    }
}
