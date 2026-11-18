using System.IO;
using AxDeck;
using AxDeck.Core;
using Microsoft.Win32;

namespace AxDeck.BoxVerify;

// 桌面页的端到端验证：真的注册表、真的文件系统、真的 BoxService。
// 每一条断言都检查一个真实的副作用，而不是内存里的模型。
//
// 「桌面整理」那套分组已经整套删掉，这里验的是桌面页真正还在做的两件事：
// 文件拖进一个文件夹会变成什么，以及 Windows 自己画的那几枚桌面图标现在是显示还是藏着。
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
        TransferIn(sandbox);
        Conflict(sandbox);
        ShellIcons(sandbox);
    }

    // ---- 拖进来：复制 / 移动 / 映射 ----
    private static void TransferIn(string sandbox)
    {
        var source = Path.Combine(sandbox, "note.txt");
        File.WriteAllText(source, "hello");

        // 桌面页拖进来的东西落到桌面上；验证用沙盒里一个空文件夹代替它。
        var copyTarget = Folder(sandbox, "copy-target");
        var copied = BoxTransferExecutor.Execute([source], copyTarget, BoxDropAction.Copy, BoxConflictPolicy.Rename);
        Check("拖入为复制：目标里有了文件，源文件还在",
            copied.Count == 1 && File.Exists(copied[0]) && File.Exists(source), string.Join(",", copied));
        Check("拖入为复制：源文件的内容没有变", File.ReadAllText(source) == "hello");

        var moveTarget = Folder(sandbox, "move-target");
        var moving = Path.Combine(sandbox, "moving.txt");
        File.WriteAllText(moving, "move me");
        var moved = BoxTransferExecutor.Execute([moving], moveTarget, BoxDropAction.Move, BoxConflictPolicy.Rename);
        Check("拖入为移动：文件到了目标里，源位置空了",
            moved.Count == 1 && File.Exists(moved[0]) && !File.Exists(moving), string.Join(",", moved));

        var mapTarget = Folder(sandbox, "map-target");
        var mapping = Path.Combine(sandbox, "mapped.txt");
        File.WriteAllText(mapping, "map me");
        var mapped = BoxTransferExecutor.Execute([mapping], mapTarget, BoxDropAction.Map, BoxConflictPolicy.Rename);
        Check("拖入为映射：目标里是快捷方式，源文件还在",
            mapped.Count == 1 && File.Exists(mapped[0]) && File.Exists(mapping)
            && Path.GetExtension(mapped[0]).Equals(".lnk", StringComparison.OrdinalIgnoreCase), string.Join(",", mapped));
    }

    // ---- 同名处理：改名 / 跳过 / 替换 ----
    private static void Conflict(string sandbox)
    {
        var conflictRoot = Folder(sandbox, "conflict");
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

    // ---- 原生桌面图标：真的注册表 ----
    private static void ShellIcons(string sandbox)
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

        // 从一个已知的起点验：注册表里 0 是显示、1 是藏起来。
        // 这台机器上回收站可能是用户自己在「桌面图标」设置里藏起来的，那样测的就是机器的样子，不是代码的行为——
        // 所以先把起点摆成显示，验完再原样放回去。
        WriteVisible(newStart, clsid, true);
        WriteVisible(classic, clsid, true);

        try
        {
            // 手动开关：桌面页的「桌面图标」那一块就是这么写的。
            var service = Load(new DesktopLook());
            Check("从一个显示的图标开始，页面读到它是显示的", service.ShellIconVisible(shellId));
            service.SetShellIconVisible(shellId, false);
            Check("关掉之后，新开始菜单那处被写成藏着",
                ReadValue(newStart, clsid) == 1, $"值是 {ReadValue(newStart, clsid)}");
            Check("关掉之后，经典开始菜单那处也被写成藏着",
                ReadValue(classic, clsid) == 1, $"值是 {ReadValue(classic, clsid)}");
            Check("关掉之后，页面读到它是不显示的", !service.ShellIconVisible(shellId));
            service.SetShellIconVisible(shellId, true);
            Check("再打开，两处都写回显示",
                ReadValue(newStart, clsid) == 0 && ReadValue(classic, clsid) == 0,
                $"新开始菜单 {ReadValue(newStart, clsid)}，经典开始菜单 {ReadValue(classic, clsid)}");

            // 老存档留下的记录：上一个版本把图标藏起来时记着「它原来是显示的」。
            // 分组已经没了，读存档时要把这些图标放回去再清掉记录，否则一张被上次运行藏起来的图标会一直藏着。
            WriteVisible(newStart, clsid, false);
            WriteVisible(classic, clsid, false);
            var stale = new DesktopLook();
            stale.IconRecords[shellId] = new ShellIconRecord { PreviousVisible = true, ManagedHidden = true };
            var reopened = new BoxService();
            reopened.Load(stale);
            Check("读老存档时，被上一次运行藏起来的图标放回了显示",
                ReadValue(newStart, clsid) == 0, $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
            Check("读老存档时，经典开始菜单那处也放回了显示",
                ReadValue(classic, clsid) == 0, $"值是 {ReadValue(classic, clsid)?.ToString() ?? "没有"}");
            Check("放回去之后，记录销掉了", stale.IconRecords.Count == 0,
                string.Join(",", stale.IconRecords.Keys));

            // 用户自己本来就把图标藏起来了：记录里记着「原来是藏着的」，放回去就还是藏着的——
            // 放出来就是改了用户自己的设置。
            WriteVisible(newStart, clsid, false);
            WriteVisible(classic, clsid, false);
            var hidden = new DesktopLook();
            hidden.IconRecords[shellId] = new ShellIconRecord { PreviousVisible = false, ManagedHidden = true };
            Load(hidden);
            Check("用户自己藏起来的图标，放回去仍然是藏着的",
                ReadValue(newStart, clsid) == 1, $"值是 {ReadValue(newStart, clsid)?.ToString() ?? "没有"}");
            Check("用户自己藏起来的图标，经典开始菜单那处也仍然是藏着的",
                ReadValue(classic, clsid) == 1, $"值是 {ReadValue(classic, clsid)?.ToString() ?? "没有"}");
            Check("这一条也把记录销掉了", hidden.IconRecords.Count == 0);

            // 记录里有个不认识的系统项：清掉它，但不去碰注册表。
            var unknown = new DesktopLook();
            unknown.IconRecords["no-such-entry"] = new ShellIconRecord { PreviousVisible = true };
            var untouched = ReadValue(newStart, clsid);
            Load(unknown);
            Check("记录里不认识的系统项被清掉，注册表没有因此被动过",
                unknown.IconRecords.Count == 0 && ReadValue(newStart, clsid) == untouched);
        }
        finally
        {
            // 还原注册表：不管验成什么样，都不留痕迹。经典开始菜单那处原本整个键都没有，就把它删掉。
            WriteValue(newStart, clsid, before);
            WriteValue(classic, clsid, beforeClassic);
            if (!classicKeyExisted) Registry.CurrentUser.DeleteSubKeyTree(classic, false);
        }
    }

    private static BoxService Load(DesktopLook data)
    {
        var service = new BoxService();
        service.Load(data);
        return service;
    }

    private static string Folder(string sandbox, string name)
    {
        var path = Path.Combine(sandbox, name);
        Directory.CreateDirectory(path);
        return path;
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

    // 把桌面图标摆成看得见（0）或看不见（1）。键不在时先建出来，不然这个值就没地方写。
    private static void WriteVisible(string path, string name, bool visible)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path, true);
        key?.SetValue(name, visible ? 0 : 1, RegistryValueKind.DWord);
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
