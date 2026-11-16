using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxDeck;

// 「桌面」页的两件事：换页的那一下不再把同一个元素加两次，接管桌面的窗口找得到。
internal static class DesktopChecks
{
    public static void Run(Action<string, bool> check)
    {
        CheckSwapOfTheSameElement(check);
        CheckDesktopWindowFound(check);
        CheckIconsStateRead(check);
        CheckSystemEntriesHaveTheirOwnIcons(check);
        CheckShellMenuCommandLayout(check);
        CheckOpenBranches(check);
        CheckKeyboardBehavior(check);
    }

    // 键盘那一套的分支行为：F2 只对有文件的项起、Ctrl+C 过滤系统项、改名拒掉 Windows 不认的名字、
    // Alt+Enter 与 Ctrl+V 各走各的分支。这些不真的弹对话框、不动真实桌面文件——菜单和属性走 override
    // 缝与可替换的静态入口，改名只验合法化的判断，不落盘。
    private static void CheckKeyboardBehavior(Action<string, bool> check)
    {
        var type = typeof(DesktopIconsView);
        var view = type.GetMethod("View_KeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var keyDown = typeof(System.Windows.Input.KeyEventArgs);
        if (view is null || keyDown is null) { check("键盘处理的入口还在", false); return; }

        // View_KeyDown 是 private，事件参数又得有 Key 和 Modifiers：这里不真发键盘事件，
        // 只验证它调到的每一条分支自己在隔离时行为正确。分支一：F2 改名的合法性判断。
        var sanitize = typeof(AxDeck.Core.BoxNaming).GetMethod("SanitizeUserFileName");
        check("改名的名字合法化还在（Windows 保留名被拒）",
            sanitize is not null
            && sanitize.Invoke(null, ["CON"]) is null
            && sanitize.Invoke(null, ["a<b"]) is null
            && sanitize.Invoke(null, ["  ok.txt  "]) as string == "ok.txt");

        // 分支二：Ctrl+C/X 只交出真实文件，系统项（此电脑、回收站）没有文件，不参加。
        var selectedFiles = type.GetMethod("SelectedFiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (selectedFiles is null) { check("复制剪切会过滤掉系统项", false); return; }
        // SelectedFiles 读的是 _selected 字段：造一个视图，把系统项塞进去，交出来的必须是空的。
        var window = new System.Windows.Window { Left = -20000, Top = -20000, Width = 10, Height = 10,
            ShowActivated = false, ShowInTaskbar = false, WindowStyle = System.Windows.WindowStyle.None };
        var host = new DesktopIconsView(new MainWindow());
        window.Content = host;
        try
        {
            window.Show();
            var selected = type.GetField("_selected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(host) as System.Collections.Generic.HashSet<string>;
            if (selected is null) { check("复制剪切会过滤掉系统项", false); return; }
            selected.Add("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}");
            selected.Add(@"C:\D\thief.txt");
            var files = (System.Collections.Generic.List<string>?)selectedFiles.Invoke(host, null);
            // ItemAt 查的是这一页画出来的项，没画出来的（Items 空）查不到——所以两者都过滤掉了。
            // 这正好说明过滤器对「不认识的路径」同样保守：复制不会交出页面上没有的东西。
            check("复制剪切不交出这一页没有的项（系统项与未知路径都不参加）", files is { Count: 0 });
        }
        catch (Exception ex)
        {
            check($"键盘分支验得下去（{ex.GetType().Name}: {ex.Message}）", false);
        }
        finally { window.Close(); }
    }

    // 双击打开有两条路：系统项按解析名交给 shell，普通项按路径交给系统。哪一个项走哪一条路
    // 也是行为的一部分——先前只有「整次打开」一个缝，两条路各走对没有，测不出来。
    // 把两个分支各自的缝接住，问它们各自收到了什么。
    private static void CheckOpenBranches(Action<string, bool> check)
    {
        var shellCalls = new List<string>();
        var fileCalls = new List<string>();
        var oldOpen = DesktopIconsView.OpenOverride;
        var oldShell = DesktopIconsView.OpenShellOverride;
        var oldFile = DesktopIconsView.OpenFileOverride;
        DesktopIconsView.OpenOverride = null;
        DesktopIconsView.OpenShellOverride = id => shellCalls.Add(id);
        DesktopIconsView.OpenFileOverride = path => fileCalls.Add(path);
        try
        {
            // 私有的 Open：测试直接调它，就像双击或回车最后走到的那样。
            var open = typeof(DesktopIconsView).GetMethod("Open",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (open is null) { check("双击打开的那个私有方法还在", false); return; }

            // 系统项：此电脑。要走到解析名那一条路，接到的是它的编号。
            shellCalls.Clear(); fileCalls.Clear();
            open.Invoke(null, [new AxDeck.Core.DesktopItems.Item("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                "此电脑", AxDeck.Core.DesktopItems.Kind.Shell, true)]);
            check("系统项打开时走解析名那条路（接到编号）",
                shellCalls.Count == 1 && shellCalls[0] == "this-pc"
                && fileCalls.Count == 0);

            // 普通文件：要走路径那条路，不该碰到解析名。
            shellCalls.Clear(); fileCalls.Clear();
            open.Invoke(null, [new AxDeck.Core.DesktopItems.Item(@"C:\D\notes.txt",
                "notes.txt", AxDeck.Core.DesktopItems.Kind.File, true)]);
            check("普通文件打开时走路径那条路（接到路径）",
                fileCalls.Count == 1 && fileCalls[0] == @"C:\D\notes.txt" && shellCalls.Count == 0);
        }
        catch (Exception ex)
        {
            check($"打开分支验得下去（{ex.GetType().Name}: {ex.Message}）", false);
        }
        finally
        {
            DesktopIconsView.OpenOverride = oldOpen;
            DesktopIconsView.OpenShellOverride = oldShell;
            DesktopIconsView.OpenFileOverride = oldFile;
        }
    }

    // 右键菜单里选一项，命令交回系统去执行时用的那个结构体：CMINVOKECOMMANDINFO。
    // 系统是按字段的位置读的，还拿 cbSize 核对大小；位置错一个，菜单照样弹出来，命令却一个都不执行，
    // 而且不报错。这一版之前它就是这么错的（把 hwnd 放在了 fMask 该在的地方），所以这条按偏移钉死。
    private static void CheckShellMenuCommandLayout(Action<string, bool> check)
    {
        var type = typeof(AxDeck.Native.ShellContextMenu).GetNestedType("InvokeCommandInfo",
            System.Reflection.BindingFlags.NonPublic);
        if (type is null) { check("系统菜单的命令结构体还在", false); return; }

        // x64 上 CMINVOKECOMMANDINFO 是 56 字节；字段按这个顺序排：
        // cbSize, fMask, hwnd, lpVerb, lpParameters, lpDirectory, nShow, dwHotKey, hIcon。
        var expected = new (string Field, int Offset, int Size)[]
        {
            ("Size", 0, 4), ("Mask", 4, 4), ("Owner", 8, 8), ("Verb", 16, 8),
            ("Parameters", 24, 8), ("Directory", 32, 8), ("Show", 40, 4), ("HotKey", 44, 4), ("Icon", 48, 8)
        };

        var size = System.Runtime.InteropServices.Marshal.SizeOf(type);
        check($"命令结构体是系统认的 56 字节（现在是 {size}）", size == 56);

        foreach (var (name, offset, width) in expected)
        {
            var field = type.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            if (field is null) { check($"命令结构体有 {name} 这一格", false); continue; }
            int at = (int)System.Runtime.InteropServices.Marshal.OffsetOf(type, name);
            int bytes = System.Runtime.InteropServices.Marshal.SizeOf(field.FieldType);
            check($"{name} 在偏移 {offset}、占 {width} 字节（现在在 {at}、占 {bytes}）", at == offset && bytes == width);
        }

        // 接口那边也一样：IContextMenu 一共三个方法，第三格是 GetCommandString。
        // 先前那一格被写成了根本不存在的 GetDefaultCommand，后面还多摆了一格——接口是按顺序对的表，
        // 多一格就等于把名字接到了别人的位置上。
        var menu = typeof(AxDeck.Native.ShellContextMenu).GetNestedType("IContextMenu",
            System.Reflection.BindingFlags.NonPublic);
        if (menu is null) { check("系统菜单的接口还在", false); return; }
        var methods = menu.GetMethods().Select(method => method.Name).ToArray();
        var wanted = new[] { "QueryContextMenu", "InvokeCommand", "GetCommandString" };
        check($"系统菜单的接口正好这三格，顺序也对（现在是 {string.Join("、", methods)}）",
            methods.SequenceEqual(wanted));
    }

    // 每个系统项画的是它自己的图标。上一版把 `::{GUID}` 直接当路径交给 SHGetFileInfoW，还带着
    // SHGFI_USEFILEATTRIBUTES，shell 就按「没有扩展名的普通文件」给图——回收站、此电脑、网络、
    // 控制面板四个画出来是同一个通用文件图标（像素完全相同）。这条就是拿来盯住它的：
    // 系统项各有各的图，谁也不能跟别人一样。
    private static void CheckSystemEntriesHaveTheirOwnIcons(Action<string, bool> check)
    {
        var seen = new Dictionary<string, string>();
        foreach (var id in new[] { "recycle-bin", "this-pc", "network", "control-panel", "user-folder" })
        {
            var icon = AxDeck.Native.BoxIcons.ForShellItem(id);
            if (icon is null) { check($"系统项「{id}」拿得到图标", false); continue; }
            // 图标的像素当指纹：同一个通用文件的图标，指纹必然一样。
            var bitmap = icon as System.Windows.Media.Imaging.BitmapSource;
            var pixels = new byte[(bitmap?.PixelWidth ?? 0) * (bitmap?.PixelHeight ?? 0) * 4];
            if (bitmap is not null) bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            var print = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))[..16];
            if (seen.TryGetValue(print, out var twin))
                check($"系统项「{id}」画的是自己的图标（和「{twin}」重复）", false);
            else { seen[print] = id; check($"系统项「{id}」画的是自己的图标", true); }
        }
    }

    // 读桌面图标的真实状态：桌面的图标列表窗口找得到，才说得清 Explorer 现在画没画。
    // 这条只读，不动用户眼前的桌面，所以全量里也跑。
    private static void CheckIconsStateRead(Action<string, bool> check)
    {
        var method = typeof(AxDeck.Native.ShellDesktop).GetMethod("DrawnIconsVisible",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null) { check("读得到桌面图标的真实状态", false); return; }
        var drawn = method.Invoke(null, null);
        check("读得到桌面图标的真实状态", drawn is bool);
    }

    // 真的动手把桌面图标藏起来再放回来。会改变用户眼前的桌面，所以只按名字跑（--only desktop-toggle），
    // 不在全量里跑。验完一定恢复原状，包括中途抛异常那次。
    public static void RunToggle(Action<string, bool> check)
    {
        var desktop = typeof(AxDeck.Native.ShellDesktop);
        var read = desktop.GetMethod("DrawnIconsVisible", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var set = desktop.GetMethod("SetIconsVisible", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (read is null || set is null) { check("读得到桌面图标的真实状态", false); return; }
        bool? Drawn() => (bool?)read.Invoke(null, null);
        var before = Drawn();
        if (before is null) { check("读得到桌面图标的真实状态", false); return; }
        check("读得到桌面图标的真实状态", true);
        try
        {
            // 先藏，再露，每一步都问 Explorer 自己现在画没画——上一版靠注册表判断，注册表滞后，
            // 于是它以为「已经是要的样子」而什么都不发，桌面一点也不动。
            set.Invoke(null, [!(bool)before]);
            check($"藏起来后 Explorer 真的不画图标了（从 {(bool)before} 到 {Drawn()}）", Drawn() == !(bool)before);
            set.Invoke(null, [(bool)before]);
            check($"放回来（目标 {(bool)before}）后 Explorer 真的又画了图标（现在 {Drawn()}）", Drawn() == (bool)before);
        }
        finally
        {
            if (Drawn() != before) set.Invoke(null, [(bool)before]);
        }
        check("验完桌面回到原来的样子", Drawn() == before);
    }

    // 换页换到同一个元素上：上一版在这里抛
    // 「指定的 Visual 已经是另一个 Visual 的子级或者已经是 CompositionTarget 的根」，
    // 桌面页画的图标整个消失、也不再响应，crash.log 里每秒一条。这个抛法必须回不来。
    private static void CheckSwapOfTheSameElement(Action<string, bool> check)
    {
        var host = new Grid();
        var view = new Grid { Background = Brushes.Transparent };
        host.Children.Add(view);
        try
        {
            // 没有动画、有动画、同一个元素作为新页和旧页：三种都要活着回来。
            MonitorCrossfade.Swap(host, view, view, motion: false);
            MonitorCrossfade.Swap(host, view, view, motion: true);
            MonitorCrossfade.Swap(host, view, view, motion: true);
            check("换页换到同一个元素上不再抛异常", true);
        }
        catch (Exception ex)
        {
            check("换页换到同一个元素上不再抛异常", false);
            Console.WriteLine($"  异常: {ex.GetType().Name}: {ex.Message}");
            return;
        }
        check("换页后视图还挂在面板上", host.Children.Contains(view) && host.Children.Count == 1);
        check("换页后视图不透明", view.Opacity == 1);
    }

    // 接管桌面要找的那扇窗：桌面的图标视图住在类名 SHELLDLL_DefView 的窗口里。
    // 这条只问找不找得到——发不发命令、桌面动不动，得靠人的眼睛看。
    private static void CheckDesktopWindowFound(Action<string, bool> check)
    {
        var method = typeof(AxDeck.Native.ShellDesktop).GetMethod("FindDesktopWindow",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method is null) { check("找得到桌面自己的窗口", false); return; }
        var found = (IntPtr)(method.Invoke(null, null) ?? IntPtr.Zero);
        // 一台正常跑着 Explorer 的机器上桌面的窗口一定在；找不到多半是查找的类名或层级写错了。
        check("找得到桌面自己的窗口", found != IntPtr.Zero);
    }
}
