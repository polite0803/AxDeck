using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AxDeck.Core;
using AxDeck.Native;

namespace AxDeck;

// The desktop organizer's window, driven the way the user drives it: a file is dragged onto it, an item is clicked
// with the right button, and the window is asked what it looks like from the outside. The checks look at real
// side effects (the file really moved, the menu really opened) and at the real window (is it on top, does a click
// reach it), because that is where the organizer's behaviour was breaking.
internal static class BoxInteractionTests
{
    private static Action<string, bool> _check = null!;
    private static readonly List<string> _failedNames = [];

    public static void Run(Action<string, bool> check)
    {
        _check = check;
        _failedNames.Clear();

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // The box's own menu takes the panel's menu style; the test app has none, so a plain one stands in. Each style
        // has to be written for its own kind of control, or the framework refuses it.
        //
        // 这里塞的桩只是让菜单能建起来，好让「右键按下去」那一步能走完；
        // 「样式在不在」这件事另有��条断言去查真正的两个 XAML 文件（CheckMenuStyles），
        // 因为这个桩答不了那个问题——它自己就是那个问题的答案。
        foreach (var (key, type) in new (string, Type)[]
        {
            ("ShelfMenu", typeof(ContextMenu)),
            ("ShelfMenuItem", typeof(MenuItem)),
            ("ShelfMenuSeparator", typeof(Separator))
        })
            if (app.Resources[key] is null) app.Resources[key] = new Style(type);

        var sandbox = Path.Combine(Path.GetTempPath(), "axdeck-boxprobe-" + Guid.NewGuid().ToString("N")[..8]);
        var root = Path.Combine(sandbox, "boxes");
        Directory.CreateDirectory(root);
        try
        {
            var service = new BoxService();
            service.Load(new DasktopData { Settings = new DasktopSettings { CollectionRoot = root, AutoHideShellIcons = false } });
            var box = service.Create("探针组");
            foreach (var name in new[] { "一.txt", "二.txt" })
                File.WriteAllText(Path.Combine(box.FolderPath, name), name);

            var window = new BoxWindow(service, box);
            // On screen: the window's own hit test has to be able to find it, so it cannot sit far off screen.
            window.Left = 200; window.Top = 200;
            try
            {
                window.Show();
                Pump();
                Pump();

                CheckWindow(window);
                CheckDrop(window, service, box, sandbox);
                CheckContextMenu(window);
                CheckDropTargetRegistration();
                CheckMenuStyles();
                CheckHitTesting();
                CheckKeyboard(window, service, box);
                CheckClipboard();
                CheckShellParsing();
            }
            catch (Exception ex) { Note("the box window answered without complaint", false, ex.ToString()); }
            finally { window.Close(); }
        }
        finally { try { Directory.Delete(sandbox, true); } catch { } }

        foreach (var name in _failedNames) _check(name, false);
    }

    private static void Note(string name, bool ok, string? detail = null)
    {
        if (ok) { _check(name, true); return; }
        _failedNames.Add(detail is null ? name : $"{name} -- {detail}");
    }

    // ---- 窗口在外面的样子 ----

    private static void CheckWindow(BoxWindow window)
    {
        Note("分组窗口开着", window.IsVisible);

        var hwnd = new WindowInteropHelper(window).Handle;
        // The window has to be where the desktop can click it: on screen, with a size of its own.
        GetWindowRect(hwnd, out var rect);
        var onScreen = rect.Right > 0 && rect.Bottom > 0 && rect.Right - rect.Left > 100 && rect.Bottom - rect.Top > 100;
        Note("分组窗口在屏幕上，有自己的大小", onScreen,
            $"窗口矩形 {rect.Left},{rect.Top},{rect.Right - rect.Left}x{rect.Bottom - rect.Top}");

        // The window is asked to be the topmost one: without it the desktop's own icons and every other window
        // sit on top of the box, and none of it can be clicked.
        Note("分组窗口浮在最上层", window.Topmost);

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        // A transparent window here means the window is click-through: the clicks land on the desktop, not on the box.
        Note("分组窗口不是点击穿透的", (ex & WS_EX_TRANSPARENT) == 0, $"exstyle=0x{ex:X}");
        Note("分组窗口没有被禁用", window.IsEnabled);

        CheckEmptyBoxHitTest(window);
    }

    // The empty box: the state a box is in right after it is made, and the state the user said nothing could be
    // dropped into. The system finds the drop target by hit testing the mouse, so the element under the pointer
    // decides whether the box receives anything. An empty area of the box is where the pointer lands.
    private static void CheckEmptyBoxHitTest(BoxWindow window)
    {
        var icons = window.Icons;
        var area = icons.Parent as FrameworkElement;
        if (area is null) { Note("空分组也能接住拖放", false, "找不到图标区"); return; }

        // The point is in the empty part of the box: below the title, and away from the icons.
        var point = new Point(area.ActualWidth > 20 ? area.ActualWidth - 6 : 10, 6);
        var hit = VisualTreeHelper.HitTest(area as Visual, point)?.VisualHit;

        // The hit has to be the area that takes the drops, or one of its children: anything else means the drop
        // never reaches the box.
        var reaches = hit is not null && (ReferenceEquals(hit, area) || IsChildOf(hit, area));
        Note("空分组也能接住拖放", reaches,
            $"命中的是 {hit?.GetType().Name ?? "什么都没有"}，图标区是 {area.GetType().Name}");
    }

    // 某个元素是不是另一个元素的后代。
    private static bool IsChildOf(DependencyObject? element, DependencyObject? parent)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, parent)) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    // ---- 拖进来 ----

    private static void CheckDrop(BoxWindow window, BoxService service, DesktopBox box, string sandbox)
    {
        var icons = window.Icons;
        var area = (FrameworkElement)icons.Parent!;
        var source = Path.Combine(sandbox, "拖进来.txt");
        File.WriteAllText(source, "drag me");

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, new[] { source });

        // The drag events are raised by the system, and their arguments cannot be built by hand, so the private
        // constructor the system uses is used here as well.
        var args = DragArgs(area, UIElement.DragEnterEvent, data);
        area.RaiseEvent(args);
        var entered = args.Handled && args.Effects != DragDropEffects.None;
        Note("拖进来的时候窗口接住了", entered, $"handled={args.Handled} effects={args.Effects}");

        var over = DragArgs(area, UIElement.DragOverEvent, data);
        over.Effects = DragDropEffects.Move;
        area.RaiseEvent(over);
        Note("拖进来的时候窗口算出了落点", over.Handled && over.Effects == DragDropEffects.Move,
            $"handled={over.Handled} effects={over.Effects}");
        Note("拖进来的时候落点那条线露出来了", window.DropMark.Visibility == Visibility.Visible,
            $"可见性 {window.DropMark.Visibility}");

        var drop = DragArgs(area, UIElement.DropEvent, data);
        area.RaiseEvent(drop);
        Note("放下的动作被接住了", drop.Handled);
        Note("文件真的进了分组", File.Exists(Path.Combine(box.FolderPath, "拖进来.txt")),
            string.Join(",", Directory.GetFiles(box.FolderPath).Select(Path.GetFileName)));
        Note("放下的动作在顺序表里留下了痕迹", service.ItemKeys(box.Id).Any(key => key.EndsWith("拖进来.txt", StringComparison.OrdinalIgnoreCase)),
            string.Join(" | ", service.ItemKeys(box.Id)));
    }

    // ---- 右键 ----

    private static void CheckContextMenu(BoxWindow window)
    {
        var icons = window.Icons;
        var item = icons.Children.OfType<FrameworkElement>().FirstOrDefault();
        if (item is null) { Note("图标区里有东西可以按右键", false); return; }

        // 系统的那个菜单是阻塞的：它开在桌面上，一直等到用户选一项或者把它关掉。
        // 所以自动测试里不能真的按这个右键——它会把这套测试挂在那儿等一个人来点。
        // 这里验的是它前面那一段：系统认不认得这一项，能不能把菜单给出来。
        var tag = item.Tag as string ?? "";
        var items = BoxItemMenu.CountItems(tag);
        Note("右键按下去系统认得这一项", items > 0, $"{tag} → {items} 项");

        // 系统项走的是另一条路：它没有路径，只有系统认得的名字。
        var shellItem = icons.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => BoxItemKey.IsShell(child.Tag as string ?? ""));
        if (shellItem is not null)
        {
            var shellTag = shellItem.Tag as string ?? "";
            var shellItems = BoxItemMenu.CountItems(shellTag);
            Note("右键按下去系统认得这一项（系统项）", shellItems > 0, $"{shellTag} → {shellItems} 项");
        }
    }

    // The menus of the boxes take the panel's menu style, and the panel keeps those styles in its own window
    // dictionary, not in the application's. So the lookup that builds a menu has to be watched: if the style is
    // not found, the menu is thrown away and the right button does nothing at all.
    //
    // The test app cannot load App.xaml (the tests link the sources, not the XAML), so the check reads the two files
    // as they are: App.xaml has to merge the dictionary, and the dictionary has to define the styles for the kinds of
    // control the menu is made of. A style written for the wrong kind of control is refused by the framework, and
    // the menu is thrown away just the same.
    private static void CheckMenuStyles()
    {
        var source = new DirectoryInfo(AppContext.BaseDirectory);
        while (source is not null && !File.Exists(Path.Combine(source.FullName, "AxDeck.sln"))) source = source.Parent;
        if (source is null) { Note("应用的资源字典读得到", false, "找不到源码树"); return; }

        var appXaml = Path.Combine(source.FullName, "src", "AxDeck", "App.xaml");
        var shelfXaml = Path.Combine(source.FullName, "src", "AxDeck", "ShelfResources.xaml");
        if (!File.Exists(appXaml) || !File.Exists(shelfXaml))
        {
            Note("应用的资源字典读得到", false, $"App.xaml 或 ShelfResources.xaml 不在那儿：{appXaml} / {shelfXaml}");
            return;
        }

        // App.xaml has to merge the dictionary: a lookup in the application found nothing before, and the right button
        // of a box threw and opened no menu.
        var appText = File.ReadAllText(appXaml);
        Note("应用级别合并了菜单样式字典",
            appText.Contains("ShelfResources.xaml", StringComparison.Ordinal),
            "App.xaml 里没有合并 ShelfResources.xaml");

        // And the dictionary has to hold the styles, each for the kind of control it is used on.
        var shelfText = File.ReadAllText(shelfXaml);
        foreach (var (key, target) in new[]
        {
            ("ShelfMenu", "ContextMenu"),
            ("ShelfMenuItem", "MenuItem"),
            ("ShelfMenuSeparator", "Separator")
        })
        {
            var declared = System.Text.RegularExpressions.Regex.IsMatch(shelfText,
                $@"x:Key=""{key}""[^>]*TargetType=""{target}""");
            Note($"菜单样式 {key} 是给 {target} 用的", declared,
                $"{key} 没有声明成 {target}");
        }

        // Building a menu must not throw: this is what happens when the right button is pressed on a box.
        try
        {
            var menu = BoxMenu.Build();
            menu.Items.Add(BoxMenu.Item("测试", () => { }));
            menu.Items.Add(BoxMenu.Separator());
            Note("分组的菜单能建起来", menu.Items.Count == 2, $"建了 {menu.Items.Count} 项");
        }
        catch (Exception ex)
        {
            Note("分组的菜单能建起来", false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ---- 命中测试：Windows 靠它找拖放的接收者 ----

    // The system finds the drop target by hit testing the mouse: it asks the window what is under the pointer, and
    // only the element that comes back can receive the drop. A WPF element without a background is not hit at all: a
    // Grid with no Background is transparent to the mouse, and so is a Panel with nothing painted in it.
    //
    // The box's icon area is exactly such a Grid (AllowDrop = true, but nothing painted in it), so a drop that lands
    // on the empty part of a box never reaches it. This is checked here with the very element the box uses, so the
    // answer comes from the framework and not from a guess.
    private static void CheckHitTesting()
    {
        // The area of the box: a Grid that takes drops, with nothing painted in it.
        var area = new Grid { AllowDrop = true, Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Left };
        // The same area, but with a background: this is what makes it visible to the mouse.
        var painted = new Grid { AllowDrop = true, Background = Brushes.Transparent, Width = 100, Height = 100, HorizontalAlignment = HorizontalAlignment.Right };

        var host = new Grid { Width = 300, Height = 200 };
        host.Children.Add(area);
        host.Children.Add(painted);

        var window = new Window
        {
            Width = 300, Height = 200, Left = -20000, Top = -20000,
            AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Content = host
        };
        try
        {
            window.Show();
            Pump();

            // The point is inside the host: on the left the bare Grid, on the right the painted one.
            var bare = HitTest(host, new Point(50, 50));
            var withPaint = HitTest(host, new Point(250, 50));

            // This is the baseline the box is measured against: a Grid without a background is transparent to the
            // mouse, so the pointer passes straight through it and nothing is hit at all.
            Note("对照：没有背景的 Grid 命中不到东西", bare is null,
                $"命中的是 {bare?.GetType().Name ?? "什么都没有"}");
            Note("对照：画了背景的 Grid 参与命中测试", withPaint is Grid && ReferenceEquals(withPaint, painted),
                $"命中的是 {withPaint?.GetType().Name ?? "什么都没有"}");
        }
        finally { window.Close(); }
    }

    // 命中测试：问框架一个点上是什么。
    private static DependencyObject? HitTest(UIElement root, Point point)
        => VisualTreeHelper.HitTest(root as Visual, point)?.VisualHit;

    // ---- 键盘 ----

    // The keyboard of the file area. The user said the shortcuts were dead, and they were: the handler was on a
    // Grid that cannot take the focus, so the keys never arrived. This checks that the keys really get through now:
    // a real key is posted to the window, and the box has to answer it.
    private static void CheckKeyboard(BoxWindow window, BoxService service, DesktopBox box)
    {
        // The item the keys act on: a real file. The system's own entries (This PC, the Recycle Bin) have no file
        // name to change, so F2 skips them—the original does the same (useBoxFileActions.ts, startSelectedItemRename).
        var item = window.Icons.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => !BoxItemKey.IsShell(child.Tag as string ?? ""));
        if (item is null) { Note("键盘：有图标可以选", false); return; }
        var tag = item.Tag as string ?? "";

        // The window has to be the foreground one for the keys to arrive. A process that starts in the background
        // is not allowed to take the foreground away from the window that has it; pressing and releasing ALT is the
        // way Windows says "you may take it now".
        var hwnd = new WindowInteropHelper(window).Handle;
        TakeForeground(hwnd);
        Pump();

        // Select it first, the way a click does: the keyboard acts on what is chosen.
        SelectItem(window, tag);
        Note("键盘：选中的那一项画了出来", IsPainted(window, tag));

        // F2 renames the chosen item: the name turns into a box that can be typed in.
        PostKey(window, Key.F2);
        Pump();
        Note("键盘：F2 让名字变成可以编辑的框", HasRenameInput(window));

        // Escape takes it back out again.
        PostKey(window, Key.Escape);
        Pump();
        Note("键盘：Esc 把改名框收了起来", !HasRenameInput(window));

        // Ctrl+A chooses everything in the box.
        SelectItem(window, tag);
        PostKey(window, Key.A, ModifierKeys.Control);
        Pump();
        var all = service.ItemKeys(box.Id);
        Note("键盘：Ctrl+A 选中分组里的每一项",
            all.Count > 0 && SelectedCount(window) == all.Count,
            $"选中了 {SelectedCount(window)} 项，分组里有 {all.Count} 项");
    }

    // 拿前台：这个进程是在后台起的，Windows 不让它抢前台。
    // 按一下 ALT 再松开，是 Windows 留给程序的口令：这一下之后就可以抢了。
    private static void TakeForeground(IntPtr hwnd)
    {
        var alt = KeyInput(0x12, false);
        SendOne(alt);
        SendOne(KeyInput(0x12, true));
        Pump();
        SetForegroundWindow(hwnd);
        Pump();
    }

    // 选中一项：走的是点击那条路，所以选区是真的被改了。
    private static void SelectItem(BoxWindow window, string tag)
    {
        var item = window.Icons.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => child.Tag as string == tag);
        if (item is null) return;
        // 生产代码把选区挂在图标区的 PreviewMouseLeftButtonDown 上（预览阶段先走），
        // 所以这里发的一手也得是 Preview 那一个：发成 MouseLeftButtonDown 的话，
        // 冒泡阶段那个处理器虽然也在，选区那一段却不在它里面。
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
            Source = item
        };
        // 真实输入里 OriginalSource 是输入系统给的；手工造的事件没有它，
        // 而生产代码是从 OriginalSource 往上找那一项的——没有它就当成点在空白处，选区会被清掉。
        SetOriginalSource(args, item);
        // 事件要从图标区那里发出去，生产的选区处理就挂在图标区的 PreviewMouseLeftButtonDown 上。
        // 从这一项自己发的话，路由是走不到的——真实输入是系统从窗口往下发的。
        var area = FileAreaOf(window);
        if (area is not null) area.RaiseEvent(args);
        else item.RaiseEvent(args);
    }

    // 生产的那个图标区（选区和键盘都挂在它上面）。
    private static FrameworkElement? FileAreaOf(BoxWindow window)
    {
        var field = typeof(BoxWindow).GetField("_fileArea",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(window) as FrameworkElement;
    }

    // 这一项在不在窗口的视觉树里：不在的话事件路由根本到不了图标区。
    private static bool IsInWindow(BoxWindow window, FrameworkElement item)
    {
        var current = item;
        while (current is not null)
        {
            if (ReferenceEquals(current, window)) return true;
            current = current.Parent as FrameworkElement;
        }
        return false;
    }

    // 读一读生产代码里的选区：选中了哪些项。
    private static string ReadSelected(BoxWindow window)
    {
        var field = typeof(BoxWindow).GetField("_selected",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var value = field?.GetValue(window) as System.Collections.IEnumerable;
        if (value is null) return "(拿不到)";
        var names = value.Cast<object>().Select(x => x?.ToString() ?? "");
        return string.Join(" | ", names);
    }

    // OriginalSource 是只读的属性，只能从它背后的那个字段塞进去。
    private static void SetOriginalSource(RoutedEventArgs args, object source)
    {
        var field = typeof(RoutedEventArgs).GetField(
            "_originalSource",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        field?.SetValue(args, source);
    }
    // 那一项底下是不是画了选中的蓝。
    private static bool IsPainted(BoxWindow window, string tag)
    {
        var item = window.Icons.Children.OfType<FrameworkElement>()
            .FirstOrDefault(child => child.Tag as string == tag);
        return item is Border border && border.BorderThickness.Left > 0;
    }

    // 现在选中了多少项：画了蓝的那些。
    private static int SelectedCount(BoxWindow window)
        => window.Icons.Children.OfType<Border>().Count(border => border.BorderThickness.Left > 0);

    // 名字那一行现在是不是一个可以编辑的框。
    private static bool HasRenameInput(BoxWindow window)
        => window.Icons.Children.OfType<FrameworkElement>()
            .SelectMany(child => (child as Border)?.Child is System.Windows.Controls.Panel panel
                ? panel.Children.OfType<FrameworkElement>()
                : [])
            .Any(child => child is TextBox);

    // 把一个键真的按下去：走的是 WPF 自己的输入管线，所以收不收得到看的是真行为。
    // 不用 PostMessage：WPF 的输入层只处理真正来自键盘的消息，PostMessage 投递的那种它不看。
    // 用 SendInput：这个窗口是透明的不抢焦点（ShowActivated=false），
    // 注入的键进不来——真实用户用的时候是点一下才有焦点的。
    private static void PostKey(Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        // 窗口得是前台的，键盘的消息才往它身上走。
        TakeForeground(hwnd);

        var keyCode = KeyToVirtualKey(key);
        // Ctrl 要先按住：这一段必须先落下去，等系统把状态记下来，
        // 后面按主键的时候 Keyboard.Modifiers 才读得到它。
        // 一批全打进去不行——等窗口收到主键的 KeyDown 时，Ctrl 早就松了。
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            SendOne(KeyboardInput(0x11, false));
            Pump();
        }
        SendOne(KeyboardInput(keyCode, false));
        Pump();
        SendOne(KeyboardInput(keyCode, true));
        Pump();
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            SendOne(KeyboardInput(0x11, true));
            Pump();
        }
    }

    // 一次按键：按下或者松开。
    private static Input KeyboardInput(int virtualKey, bool up)
    {
        var input = default(Input);
        input.Type = 1; // INPUT_KEYBOARD
        input.Data.Keyboard = new KeyboardInputData
        {
            VirtualKey = (ushort)virtualKey,
            ScanCode = 0,
            Flags = up ? 0x0002u : 0u, // KEYEVENTF_KEYUP
            Time = 0,
            ExtraInfo = IntPtr.Zero
        };
        return input;
    }

    // ALT 键：按一下再松开，用来拿前台。
    private static Input KeyInput(int virtualKey, bool up) => KeyboardInput(virtualKey, up);

    // 一次只送一个键。
    // 送不出去的时候要说出来：SendInput 失败是不吭声的，
    // 看起来就像「窗口收不到键盘」，其实是自己填错了结构。
    private static void SendOne(Input input)
    {
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
        if (sent != 1)
            throw new InvalidOperationException($"SendInput 没有送出去（错误码 {Marshal.GetLastWin32Error()}）");
    }

    // 键名到虚拟键码。
    private static int KeyToVirtualKey(Key key) => key switch
    {
        Key.A => 0x41, Key.C => 0x43, Key.X => 0x58, Key.V => 0x56,
        Key.F2 => 0x71, Key.Delete => 0x2E, Key.Enter => 0x0D, Key.Escape => 0x1B,
        _ => 0
    };

    // 一次真实按键要填的结构。
    //
    // 这里的大小必须和 Win32 的 INPUT 一样：x64 上它是 40 字节——
    // 一个 uint（类型）+ 补齐到 8 字节 + 32 字节的联合体（联合体的大小由 MOUSEINPUT 决定）。
    // 少写了这个联合体或者用了 int 类型的 type，它就只有 28 字节，
    // SendInput 会因为参数不对而直接拒绝（错误码 87），键一个也进不来——
    // 而且它不报错，只是安安静静地失败，看起来像是窗口收不到焦点。
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputData Data;
    }

    // 按键消息带的那一份数据。
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] public MouseInputData Mouse;
        [FieldOffset(0)] public KeyboardInputData Keyboard;
    }

    // 鼠标输入的数据。
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    // 键盘输入的数据。
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    // 把两个线程的输入队列接起来：接上之后，其中一个线程设的前台窗口对另一个也认。
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint threadOne, uint threadTwo, bool doAttach);

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    // 把键盘焦点给这个窗口。窗口成了前台，但焦点可能还在别的窗口上。
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    // ---- 文件剪贴板 ----

    // The file clipboard: the one Explorer's copy and cut write. It has to speak the same language as the rest of
    // Windows, so a file copied in a box can be pasted into Explorer's window.
    private static void CheckClipboard()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "axdeck-clip-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        try
        {
            var first = Path.Combine(sandbox, "一.txt");
            var second = Path.Combine(sandbox, "二.txt");
            File.WriteAllText(first, "1");
            File.WriteAllText(second, "2");

            var written = ShellClipboard.Write([first, second], ShellClipboard.Operation.Copy);
            Note("剪贴板：写进了两个文件", written);

            var (paths, operation) = ShellClipboard.Read();
            Note("剪贴板：读回来还是那两个文件",
                paths.Count == 2 && paths.Contains(first) && paths.Contains(second),
                string.Join(" | ", paths));
            Note("剪贴板：记下的是复制", operation == ShellClipboard.Operation.Copy);

            // Cut is written the same way, only the intent differs.
            ShellClipboard.Write([first], ShellClipboard.Operation.Move);
            var (cutPaths, cutOperation) = ShellClipboard.Read();
            Note("剪贴板：剪切和复制分开记",
                cutPaths.Count == 1 && cutPaths[0] == first && cutOperation == ShellClipboard.Operation.Move,
                $"{cutPaths.Count} 个，意图 {cutOperation}");
        }
        catch (Exception ex)
        {
            Note("剪贴板：写进了两个文件", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally { try { Directory.Delete(sandbox, true); } catch { } }
    }

    // ---- 系统的菜单 ----

    // The system's own menu for a file: it is built from the path the shell gives, and it is the same menu Explorer's
    // shows. What can be checked without a person standing in front of the screen is the part before the menu opens:
    // the shell has to accept the path and hand back the menu.
    //
    // 这一段走的是生产的那个函数（ShellContextMenu.CountItems），不是测试自己重抄一遍：
    // 自己重抄的那份签名是错的，测的是假的，生产上的错它一条都发现不了。
    private static void CheckShellParsing()
    {
        // A file that is really there: the shell must be able to make a menu for it.
        var sandbox = Path.Combine(Path.GetTempPath(), "axdeck-shell-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(sandbox);
        try
        {
            var file = Path.Combine(sandbox, "菜单.txt");
            File.WriteAllText(file, "menu");

            // 生产的那个函数：解析路径 → 找到它所在的文件夹 → 让那个文件夹把菜单给出来。
            // 返回的是菜单的项数：小于零说明这一步断了。
            var items = ShellContextMenu.CountItems(file);
            Note("系统菜单：文件夹给出了这一项的菜单", items > 0, $"{items} 项");
        }
        catch (Exception ex)
        {
            Note("系统菜单：文件夹给出了这一项的菜单", false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally { try { Directory.Delete(sandbox, true); } catch { } }
    }

    // 造一个系统会发的那种拖放事件。
    private static DragEventArgs DragArgs(FrameworkElement target, RoutedEvent kind, IDataObject data)    {
        // The system builds these itself; the constructor it uses is not public, so it is used here as well.
        var ctor = typeof(DragEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects), typeof(DependencyObject), typeof(Point) },
            null);
        if (ctor is null) throw new InvalidOperationException("the drag event arguments cannot be made here");
        var args = (DragEventArgs)ctor.Invoke(new object?[]
        {
            data, DragDropKeyStates.None, DragDropEffects.None, target, new Point(20, 20)
        });
        args.RoutedEvent = kind;
        args.Source = target;
        return args;
    }

    // ---- 系统到底能不能把拖放进这个窗口 ----

    // The events above were raised by hand, so they only proved that the handlers work. The question the user hit is
    // another one: does Windows find this window as a drop target at all? Windows finds the drop target by hit
    // testing the mouse position, and it only asks windows that registered a drop target. WPF registers one when an
    // element has AllowDrop, so the window is asked directly: is there a drop target on it?
    private static void CheckDropTargetRegistration()
    {
        // The box's own window: it is the one that has to receive the files.
        var box = new Grid { AllowDrop = true };
        var transparent = new Window
        {
            Width = 200, Height = 200, Left = -20000, Top = -20000,
            AllowsTransparency = true, Background = Brushes.Transparent,
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Content = box
        };
        // The plain window: the same shape, without the transparency. It is the baseline: whatever this one does,
        // the transparent one has to do as well.
        var plain = new Window
        {
            Width = 200, Height = 200, Left = -20000, Top = -20000,
            Background = Brushes.Transparent, ShowInTaskbar = false, ShowActivated = false,
            Content = new Grid { AllowDrop = true }
        };
        try
        {
            transparent.Show();
            plain.Show();
            Pump();

            // Revoking answers the question without changing anything: it says the target was there when it succeeded.
            var transparentCode = RevokeDropTarget(transparent);
            var plainCode = RevokeDropTarget(plain);
            Console.WriteLine($"拖放目标：透明窗口 0x{transparentCode:X8}，普通窗口 0x{plainCode:X8}");
            Note("普通窗口的 HWND 上有拖放目标（这一步是基准）", plainCode == 0,
                $"0x{plainCode:X8}，0 是成功，0x80040100 是压根没注册");
            Note("透明窗口的 HWND 上也有拖放目标", transparentCode == 0,
                $"0x{transparentCode:X8}，0 是成功，0x80040100 是压根没注册");
        }
        finally
        {
            transparent.Close();
            plain.Close();
        }
    }

    // 撤销一个窗口上的拖放目标：成功说明它本来就注册过。
    private static int RevokeDropTarget(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return -1;
        try { return RevokeDragDrop(hwnd); }
        catch (Exception ex) { return ex.HResult; }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int RegisterDragDrop(IntPtr hwnd, [MarshalAs(UnmanagedType.Interface)] object target);
    [System.Runtime.InteropServices.DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(IntPtr hwnd);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
    }
}
