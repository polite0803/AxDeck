using System.Runtime.InteropServices;

namespace AxDeck.Native;

// The Explorer's own right-click menu, for an item on the desktop page. The menu is the system's: its items come from the shell
// handlers registered for that kind of file, and the commands are run by the shell itself. Ported from Dasktop
// (https://github.com/AugSakura/Dasktop, MIT), which shows the very same menu (shell_context/menu.rs).
//
// The way it is built: the shell parses the path into a PIDL (a list of item identifiers), the parent of that PIDL is
// bound as a shell folder, and that folder gives the menu of the item. The menu is then shown with TrackPopupMenu, and
// the command the user picked is handed back to the shell to run.

internal static class ShellContextMenu
{
    // The system menu's items start at this command id; the ids below it belong to the menu itself.
    private const uint FirstCommandId = 1;
    // The highest command id the menu may use.
    private const uint LastCommandId = 0x7fff;
    // The system's own items go in at the top, so the menu reads like Explorer's.
    private const uint InsertPosition = 0;
    // TrackPopupMenu answers this when the user closes the menu without choosing anything.
    private const int NoCommandChosen = 0;
    // The shell's flags for a plain menu.
    private const uint ShellFlagsNormal = 0;
    // TrackPopupMenu's flags: the menu follows the left button, the right one can also pick an item, and the menu
    // answers with the command that was chosen instead of zeroing it.
    private const uint TrackFlags = 0x0000 /* left aligned */ | 0x0002 /* right button picks */ | 0x0100 /* answers with the command */;
    // The window the command is shown in.
    private const int ShowNormal = 1;

    // The system's own items go in at the top, so the menu reads like Explorer's.
    private static readonly Guid IShellFolderInterfaceId = new("000214E6-0000-0000-C000-000000000046");
    // 菜单那个接口的编号：系统要给的就是它。
    private static readonly Guid IContextMenuInterfaceId = new("000214E4-0000-0000-C000-000000000046");
    // COM has to be ready before the shell is asked for anything: without it, the shell's own objects are not there.
    private const uint CoInitApartmentThreaded = 0x2;

    // Shows the system's menu for the file at the path, at the point on the screen. True when a command was run.
    public static bool Show(IntPtr owner, string path, int screenX, int screenY)
    {
        var pidl = IntPtr.Zero;
        var com = EnsureCom();
        try
        {
            pidl = ParseDisplayName(path);
            if (pidl == IntPtr.Zero) return false;
            return ShowFromPidl(owner, pidl, screenX, screenY);
        }
        finally
        {
            // The shell gave the PIDL to us, so we give it back.
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            com.Dispose();
        }
    }

    // 把系统菜单建出来，但不弹出来，只报它的项数。
    // 这一格是为了能自动验一验：菜单到底有没有被系统认出来。
    // 走的每一步都跟 Show 走的是同一条路，只是最后不弹。
    public static int CountItems(string path)
    {
        var pidl = IntPtr.Zero;
        var com = EnsureCom();
        try
        {
            pidl = ParseDisplayName(path);
            if (pidl == IntPtr.Zero) return -1;
            return CountItemsFromPidl(pidl);
        }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            com.Dispose();
        }
    }

    // Shows the system's menu for one of the Windows entries, by the name the shell knows it under.
    public static bool ShowForParsingName(IntPtr owner, string parsingName, int screenX, int screenY)
    {
        var pidl = IntPtr.Zero;
        var com = EnsureCom();
        try
        {
            pidl = ParseDisplayName(parsingName);
            if (pidl == IntPtr.Zero) return false;
            return ShowFromPidl(owner, pidl, screenX, screenY);
        }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            com.Dispose();
        }
    }

    // 打开一项的属性对话框，不弹菜单。文件路径本来可以走 ShellExecute 的 properties 动词，但系统项
    // （::{...} 这样的解析名）ShellExecute 不认——所以这里还是走 IContextMenu，只是按规范动词直接调命令。
    // lpVerb 除了「命令在菜单里的位置」还能装一个指向动词字符串的指针：shell 看到指针值落在命令编号
    // 范围之外就按字符串读。64 位进程里指针必然在那之上，不会跟 MAKEINTRESOURCE 混淆。
    public static bool ShowPropertiesDialog(string parsingName)
    {
        var pidl = IntPtr.Zero;
        var verb = IntPtr.Zero;
        var com = EnsureCom();
        try
        {
            pidl = ParseDisplayName(parsingName);
            if (pidl == IntPtr.Zero) return false;

            var child = IntPtr.Zero;
            var folder = IntPtr.Zero;
            var menuPointer = IntPtr.Zero;
            IContextMenu? menu = null;
            try
            {
                var asked = BuildMenu(pidl, IntPtr.Zero, out menuPointer, out child, out folder);
                if (asked < 0 || menuPointer == IntPtr.Zero) return false;

                menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer);
                Marshal.Release(menuPointer);
                menuPointer = IntPtr.Zero;
                if (menu is null) return false;

                // CMINVOKECOMMANDINFO 是 ANSI 结构，动词字符串按 ANSI 给。
                verb = Marshal.StringToHGlobalAnsi("properties");
                var info = new InvokeCommandInfo
                {
                    Size = (uint)Marshal.SizeOf<InvokeCommandInfo>(),
                    Verb = verb,
                    Show = ShowNormal
                };
                return menu.InvokeCommand(ref info) >= 0;
            }
            finally
            {
                // child 是父 PIDL 里面指着的一段，不是单独分配的，不能释放——
                // 释放了就是把别人的内存还回去，进程当场崩。
                if (menuPointer != IntPtr.Zero) Marshal.Release(menuPointer);
                if (menu is not null) Marshal.ReleaseComObject(menu);
                if (folder != IntPtr.Zero) Marshal.Release(folder);
            }
        }
        finally
        {
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            if (verb != IntPtr.Zero) Marshal.FreeHGlobal(verb);
            com.Dispose();
        }
    }

    // COM 的准备：这一段线程得是一个「套间」，系统的那些对象才肯出来。
    // 它只能在同一个线程上用完就放，所以做成一次性的。
    private sealed class ComScope : IDisposable
    {
        private readonly bool _ours;
        public ComScope(bool ours) { _ours = ours; }
        public void Dispose() { if (_ours) CoUninitialize(); }
    }

    private static ComScope EnsureCom()
    {
        var result = CoInitializeEx(IntPtr.Zero, CoInitApartmentThreaded);
        // S_OK 表示这是本线程第一次初始化（由我们负责放掉）；
        // S_FALSE 表示之前已经初始化过了（不是我们放的，不能放）；
        // RPC_E_CHANGED_MODE 表示这个线程已经用别的方式初始化过了，那就不用管。
        return new ComScope(result == 1);
    }

    // The shell's own menu of the item the PIDL points at.
    private static bool ShowFromPidl(IntPtr owner, IntPtr pidl, int screenX, int screenY)
    {
        var child = IntPtr.Zero;
        var folder = IntPtr.Zero;
        var menuPointer = IntPtr.Zero;
        IContextMenu? menu = null;
        try
        {
            var asked = BuildMenu(pidl, owner, out menuPointer, out child, out folder);
            if (asked < 0 || menuPointer == IntPtr.Zero) return false;

            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer);
            Marshal.Release(menuPointer);
            menuPointer = IntPtr.Zero;
            if (menu is null) return false;

            var handle = CreatePopupMenu();
            if (handle == IntPtr.Zero) return false;
            try
            {
                // The shell fills the menu in and tells us which command ids it used.
                var filled = menu.QueryContextMenu(handle, InsertPosition, FirstCommandId, LastCommandId, ShellFlagsNormal);
                // 系统有时候会回一个 E_FAIL（0x800000AE），但菜单其实已经填好了。
                // 这种情况按失败处理，菜单就一次也弹不出来——所以看的是填进去几项，不是那个返回值。
                if (GetMenuItemCount(handle) <= 0) return false;

                var chosen = TrackPopupMenu(handle, TrackFlags, screenX, screenY, 0, owner, IntPtr.Zero);
                if (chosen == NoCommandChosen) return false;

                // InvokeCommand 要的是这个命令在菜单里的位置（从 0 数）。TrackPopupMenu 给回来的是命令的编号，
                // 编号从 FirstCommandId 开始，所以位置就是编号减掉它。
                var offset = chosen - (int)FirstCommandId;
                // 关键：这个位置是以 MAKEINTRESOURCE 的方式传过去的——lpVerb 这个指针的值本身就是这个数字，
                // 而不是指向一个装着这个数字的内存。原版也是这么写的（PCSTR(command_offset as *const u8)）。
                var info = new InvokeCommandInfo
                {
                    Size = (uint)Marshal.SizeOf<InvokeCommandInfo>(),
                    Owner = owner,
                    Verb = (IntPtr)offset,
                    Show = ShowNormal
                };
                return menu.InvokeCommand(ref info) >= 0;
            }
            finally { DestroyMenu(handle); }
        }
        finally
        {
            // child 是父 PIDL 里面指着的一段，不是单独分配的，不能释放——
            // 释放了就是把别人的内存还回去，进程当场崩。
            if (menuPointer != IntPtr.Zero) Marshal.Release(menuPointer);
            if (menu is not null) Marshal.ReleaseComObject(menu);
            if (folder != IntPtr.Zero) Marshal.Release(folder);
        }
    }

    // 把系统菜单建出来，数一数它有几项。跟 Show 走的是同一条路，只是最后不弹。
    private static int CountItemsFromPidl(IntPtr pidl)
    {
        var child = IntPtr.Zero;
        var folder = IntPtr.Zero;
        var menuPointer = IntPtr.Zero;
        IContextMenu? menu = null;
        try
        {
            var asked = BuildMenu(pidl, IntPtr.Zero, out menuPointer, out child, out folder);
            if (asked < 0 || menuPointer == IntPtr.Zero) return -1;

            menu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPointer);
            Marshal.Release(menuPointer);
            menuPointer = IntPtr.Zero;
            if (menu is null) return -1;

            var handle = CreatePopupMenu();
            if (handle == IntPtr.Zero) return -1;
            try
            {
                var filled = menu.QueryContextMenu(handle, InsertPosition, FirstCommandId, LastCommandId, ShellFlagsNormal);
                // 系统有时候会回一个 E_FAIL（0x800000AE），但菜单其实已经填好了。
                // 这种情况按失败处理，菜单就一次也弹不出来——所以看的是填进去几项，不是那个返回值。
                var count = GetMenuItemCount(handle);
                if (count <= 0) return -1;
                return count;
            }
            finally
            {
                DestroyMenu(handle);
            }
        }
        finally
        {
            // child 是父 PIDL 里面指着的一段，不是单独分配的，不能释放——
            // 释放了就是把别人的内存还回去，进程当场崩。
            if (menuPointer != IntPtr.Zero) Marshal.Release(menuPointer);
            if (menu is not null) Marshal.ReleaseComObject(menu);
            if (folder != IntPtr.Zero) Marshal.Release(folder);
        }
    }

    // 从路径到菜单接口，一步一步走：解析 → 找它所在的文件夹 → 让那个文件夹给出菜单。
    // 这一段是整条路最难的地方：每一步的签名都错了，菜单就一次也不会出来。
    private static int BuildMenu(IntPtr pidl, IntPtr owner, out IntPtr menuPointer, out IntPtr child, out IntPtr folder)
    {
        menuPointer = IntPtr.Zero;
        child = IntPtr.Zero;
        folder = IntPtr.Zero;

        // The item belongs to a folder: that folder is what knows the menu.
        var iid = IShellFolderInterfaceId;
        var bound = SHBindToParent(pidl, ref iid, out folder, out child);
        if (bound < 0 || folder == IntPtr.Zero) return bound;

        // 菜单那个接口的编号：系统要给的就是它。
        var menuInterfaceId = IContextMenuInterfaceId;
        // GetUIObjectOf 要的是「项的数组的首地址」，不是数组本身。
        // 这一格要是交给封送去做，它会排错位置，进程直接崩掉——所以这里自己摆好再传地址。
        var pidls = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(pidls, child);
            var parent = (IShellFolder)Marshal.GetObjectForIUnknown(folder);
            // 绑定时给的那一份引用，换成 RCW 时已经算在 RCW 头上了，这里放掉它。
            Marshal.Release(folder);
            folder = IntPtr.Zero;

            var asked = parent.GetUIObjectOf(owner, 1, pidls, ref menuInterfaceId, IntPtr.Zero, out menuPointer);
            return asked;
        }
        finally { Marshal.FreeHGlobal(pidls); }
    }

    // The shell's list of item identifiers for the path.
    private static IntPtr ParseDisplayName(string path)
    {
        try
        {
            // SHParseDisplayName 的返回值是 HRESULT，PIDL 是从第三个参数里出来的。
            // 当返回值被当成 PIDL 时，成功那一下正好是 0，看着就像「没解析出来」，
            // 于是菜单一次也没有打开过。
            var result = SHParseDisplayName(path, IntPtr.Zero, out var pidl, 0, IntPtr.Zero);
            if (result < 0)
            {
                if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
                return IntPtr.Zero;
            }
            return pidl;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return IntPtr.Zero; }
    }

    // The folder the item belongs to, and the item's own part of it.
    // SHBindToParent 的真实签名有四个参数：PIDL、接口的编号、要给的接口指针、项自己的那截 PIDL。
    // 少了接口编号那一个，系统只回答「没实现」，菜单就一次也没有打开过。
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHBindToParent(IntPtr pidl, ref Guid interfaceId, out IntPtr folder, out IntPtr child);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr pidl, uint attributes, IntPtr attributesOut);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr owner, IntPtr item);

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint flags);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    // What InvokeCommand needs to run the command the user chose. The layout is the shell's own, so every field has to
    // be there and in its place: the shell reads them by their place, and it also checks cbSize against the size it
    // knows. The order below is CMINVOKECOMMANDINFO from shlobj_core.h (56 bytes on x64) — cbSize, fMask, hwnd, lpVerb,
    // lpParameters, lpDirectory, nShow, dwHotKey, hIcon. Getting a field out of order does not fail loudly: the shell
    // reads a bogus size, refuses the call, and the command the user picked simply never runs.
    [StructLayout(LayoutKind.Sequential)]
    private struct InvokeCommandInfo
    {
        public uint Size;
        public uint Mask;
        public IntPtr Owner;
        public IntPtr Verb;
        public IntPtr Parameters;
        public IntPtr Directory;
        public int Show;
        public uint HotKey;
        public IntPtr Icon;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        // 这些方法的顺序和个数都不能改：它们是系统的那个表，一格一格对着的。
        // GetUIObjectOf 是第八个，前面的少一个，框架就会调错的那一格。
        void ParseDisplayName(IntPtr eaten, IntPtr name);
        void EnumObjects(IntPtr owner, uint enumFlags, IntPtr idl, out IntPtr enumerator);
        void BindToObject(IntPtr pidl, uint bindOptions, ref Guid interfaceId, out IntPtr bound);
        void BindToStorage(IntPtr pidl, ref Guid interfaceId, out IntPtr storage);
        void CompareIDs(IntPtr folder, uint type, IntPtr id1, IntPtr id2, out int result);
        void CreateViewObject(IntPtr owner, ref Guid interfaceId, out IntPtr view);
        // GetAttributesOf：项的个数、项的数组、属性的进出一体。
        void GetAttributesOf(uint count, IntPtr[] idls, IntPtr attributes);
        // GetUIObjectOf：窗口、项的个数、项的数组的首地址、要的接口编号、保留位、给的接口指针。
        // 第三个参数必须是数组的首地址（裸指针），不能写成 IntPtr[]：
        // 写成数组时封送层会把它排到别的地方去，进程当场崩掉。
        [PreserveSig] int GetUIObjectOf(IntPtr owner, uint count, IntPtr pidls, ref Guid interfaceId, IntPtr reserved, out IntPtr menu);
        void GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr name);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        // 这上面只有这三个方法，顺序也不能改：接口是一张按顺序对着的表，多写一格或写错一格，
        // 后面那几格就都对着别人的位置了。SDK 的定义见 ShObjIdl_core.h 的 IContextMenu。
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint insertPosition, uint firstId, uint lastId, uint flags);
        // The command the user chose is given back by its id.
        [PreserveSig] int InvokeCommand(ref InvokeCommandInfo info);
        // 问一项的规范动词名（properties、open 这些，跟系统用哪种语言无关）。这里一直没调用过，
        // 但声明必须是对的：先前这一格被写成了根本不存在的 GetDefaultCommand，还多占了一格。
        // 缓冲区是窄字符还是宽字符由 type 里的 GCS_UNICODE 决定，所以按裸指针给，免得封送按错的那种去写。
        [PreserveSig] int GetCommandString(IntPtr commandOffset, uint type, IntPtr reserved, IntPtr name, uint maxLength);
    }
}
