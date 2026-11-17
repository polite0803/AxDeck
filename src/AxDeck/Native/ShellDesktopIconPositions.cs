using System.IO;
using System.Runtime.InteropServices;
using AxDeck.Core;

namespace AxDeck.Native;

// 真实桌面上图标的位置：Explorer 把桌面上的图标摆在一个 SysListView32 里，每一项的坐标就是「手动排好的顺序」。
//
// 读：LVM_GETITEMPOSITION 与 LVM_GETITEMTEXTW 都要一个指针，而那个列表在 explorer.exe 里，所以要在它的进程里
//     借一块内存、收发消息、再把结果读回来。这些都不改桌面：读不动就返回 Unavailable，让页面退回按名字排。
// 写：LVM_SETITEMPOSITION 只要一个打包好的坐标，普通 SendMessage 就够，不必动 explorer 的内存
//     （CommCtrl.h：wParam 是项的下标，lParam 是 MAKELPARAM(x, y)，坐标是列表客户区的坐标）。
//
// 顺序的持久化由 Explorer 自己做（关掉时写进它自己的位置记录）。程序没法强制它当场落盘，
// 也绝不去写那份没公开格式的记录——写了会被 Explorer 内存里的状态盖回去，甚至把它弄坏。
internal sealed class ShellDesktopIconPositions : IDesktopIconPositions
{
    // ListView 的消息（CommCtrl.h，都在 LVM_FIRST 0x1000 之后）。
    private const uint ListViewGetItemCount = 0x1004;
    private const uint ListViewGetItemPosition = 0x1010;
    private const uint ListViewSetItemPosition = 0x100F;
    private const uint ListViewGetItemText = 0x1073;
    // LVITEMW.mask：这一格要的是文字。
    private const uint ListViewItemText = 0x0001;
    // 列表的样式位：开了「自动排列图标」时，设进去的位置会被它当场改回去。
    private const long StyleAutoArrange = 0x0100;
    private const int IndexStyle = -16;

    // 借 explorer 的内存要的几样权利。
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint MemoryCommit = 0x1000;
    private const uint MemoryReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint MemoryRelease = 0x8000;

    // explorer 进程里那块借来的内存：LVITEMW、一段放名字的缓冲、一个 POINT。
    private const int ScratchSize = 4096;
    private const int TextOffset = 256;
    private const int TextChars = 260;
    private const int PointOffset = TextOffset + TextChars * 2;
    // 列表里一项占的一样：它在列表里的第几格，它的名字，它的坐标。
    private readonly record struct Row(int Index, string Name, double X, double Y);

    public DesktopIconSnapshot Read()
    {
        var list = ShellDesktop.IconListHandle();
        if (list == IntPtr.Zero) return DesktopIconSnapshot.Unavailable();
        if (AutoArrange(list)) return DesktopIconSnapshot.Unavailable(DesktopIconPositionStatus.AutoArrange);
        return Map(ReadRows(list)) is { } map
            ? new DesktopIconSnapshot(DesktopIconPositionStatus.Ok, map.Slots)
            : DesktopIconSnapshot.Unavailable();
    }

    public DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements)
    {
        if (placements.Count == 0) return DesktopIconWriteResult.Done;
        var list = ShellDesktop.IconListHandle();
        if (list == IntPtr.Zero) return DesktopIconWriteResult.Failed(DesktopIconPositionStatus.Unavailable);
        if (AutoArrange(list)) return DesktopIconWriteResult.Failed(DesktopIconPositionStatus.AutoArrange);
        if (Map(ReadRows(list)) is not { } map) return DesktopIconWriteResult.Failed(DesktopIconPositionStatus.Unavailable);
        // 有一项在列表里认不出来就不写：写一半会让真实桌面和这一页对不上。
        foreach (var placement in placements)
            if (!map.Indexes.ContainsKey(placement.Key)) return DesktopIconWriteResult.Failed(DesktopIconPositionStatus.Unavailable);

        foreach (var placement in placements)
        {
            // 坐标要装进两个 16 位有符号数。超出范围的值宁可不动，也不写一个错的过去。
            if (placement.X is > 32767 or < -32768 || placement.Y is > 32767 or < -32768) continue;
            int packed = ((short)placement.X & 0xFFFF) | (((short)placement.Y & 0xFFFF) << 16);
            SendMessage(list, ListViewSetItemPosition, map.Indexes[placement.Key], packed);
        }
        // 让 Explorer 按新位置重画：位置已经变了，但它的绘制可能还停在旧的排布上。
        InvalidateRect(list, IntPtr.Zero, false);
        return DesktopIconWriteResult.Done;
    }

    // 列表里每一项的名字和位置。借 explorer 的内存，读完就还。
    private static List<Row> ReadRows(IntPtr list)
    {
        List<Row> rows = [];
        int count = SendMessage(list, ListViewGetItemCount, 0, IntPtr.Zero);
        if (count <= 0) return rows;

        GetWindowThreadProcessId(list, out uint processId);
        var process = OpenProcess(ProcessVmOperation | ProcessVmRead | ProcessVmWrite, false, processId);
        if (process == IntPtr.Zero) return rows;
        var scratch = IntPtr.Zero;
        try
        {
            scratch = VirtualAllocEx(process, IntPtr.Zero, ScratchSize, MemoryCommit | MemoryReserve, PageReadWrite);
            if (scratch == IntPtr.Zero) return rows;
            var itemAt = scratch;
            var textAt = IntPtr.Add(scratch, TextOffset);
            var pointAt = IntPtr.Add(scratch, PointOffset);
            var text = new byte[TextChars * 2];
            var point = new byte[8];

            for (int index = 0; index < count; index++)
            {
                var request = new ListViewItem { Mask = ListViewItemText, Item = index, Text = textAt, TextMax = TextChars };
                if (!WriteProcessMemory(process, itemAt, ref request, Marshal.SizeOf<ListViewItem>(), out _)) continue;
                int chars = SendMessage(list, ListViewGetItemText, index, itemAt);
                string name = "";
                // 只解 Explorer 真正写进去的那几个字符（SendMessage 的返回值），不能拿整块缓冲区解：
                // 缓冲是借来复用的，上一项的名字还留在里面，按整块解出来就成了「这一项 + 上一项的尾巴」，
                // 于是每一项都对不上这一页的名字，排序在真实桌面上永远配不上位置。
                if (chars > 0 && ReadProcessMemory(process, textAt, text, text.Length, out int got) && got >= 2)
                {
                    int bytes = Math.Min(Math.Min(chars, TextChars) * 2, got & ~1);
                    name = System.Text.Encoding.Unicode.GetString(text, 0, bytes).TrimEnd('\0');
                }

                SendMessage(list, ListViewGetItemPosition, index, pointAt);
                if (!ReadProcessMemory(process, pointAt, point, point.Length, out _)) continue;
                rows.Add(new Row(index, name, BitConverter.ToInt32(point, 0), BitConverter.ToInt32(point, 4)));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            rows.Clear();
        }
        finally
        {
            if (scratch != IntPtr.Zero) VirtualFreeEx(process, scratch, IntPtr.Zero, MemoryRelease);
            CloseHandle(process);
        }
        return rows;
    }

    // 把读回来的行认回这一页要画的那些项。认名字、配位置的规则在 Core 的 DesktopIconMapping 里，
    // 那里是纯计算、能被测试覆盖；这里只负责把真实桌面的项递过去。
    private static (IReadOnlyDictionary<string, DesktopIconSlot> Slots, IReadOnlyDictionary<string, int> Indexes)? Map(List<Row> rows)
    {
        var items = ShellDesktop.Items();
        if (rows.Count == 0 || items.Count == 0) return null;
        var byPath = new Dictionary<string, DesktopItems.Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items) byPath[item.Path] = item;
        return DesktopIconMapping.Map(
            [.. rows.Select(row => new DesktopIconRow(row.Index, row.Name, row.X, row.Y))],
            [.. items.Select(item => item.Path)],
            key => byPath.TryGetValue(key, out var item) ? Names(item) : []);
    }

    // 一项在资源管理器里可能显示成哪些名字。
    // 系统项的名字随系统语言，而 AxDeck 的界面语言未必跟系统一致，所以中英两种都拿出来比；
    // 用户文件夹更特殊：桌面上写的是账户的显示名，只有从它的路径才认得出来。
    private static IReadOnlyList<string> Names(DesktopItems.Item item)
    {
        if (item.Kind != DesktopItems.Kind.Shell) return [item.Name];
        // FromPath 而不是 FromParsingName：用户文件夹在这一页上的路径是账户的 Profile 目录，不是解析名。
        if (BoxShellItems.FromPath(item.Path) is { } id && BoxShellItems.Find(id) is { } known)
        {
            // 用户文件夹：桌面上画的是账户的显示名（「刘小平」），而 Profile 目录叫 polit——两者不一定相同，
            // 所以名字问 shell 要。目录名一并留着，显示名取不到时还能按老办法比一次。
            if (id == BoxShellItems.UserFolder)
            {
                var names = new List<string> { item.Name, known.Chinese, known.English };
                if (ShellKnownFolder.UserFolderName() is { Length: > 0 } display) names.Add(display);
                if (Path.GetFileName(item.Path) is { Length: > 0 } account) names.Add(account);
                return names;
            }
            return [item.Name, known.Chinese, known.English];
        }
        return [item.Name];
    }

    private static bool AutoArrange(IntPtr list) => (GetWindowLongPtr(list, IndexStyle).ToInt64() & StyleAutoArrange) != 0;

    // LVITEMW（CommCtrl.h）。只用到 mask / iItem / pszText / cchTextMax，其余按系统认识的布局补齐：
    // 少一格或顺序错了，系统按错的位置读，读回来的就是别的字段。
    [StructLayout(LayoutKind.Sequential)]
    private struct ListViewItem
    {
        public uint Mask;
        public int Item;
        public int SubItem;
        public uint State;
        public uint StateMask;
        public IntPtr Text;
        public int TextMax;
        public int Image;
        public IntPtr Param;
        public int Indent;
        public int GroupId;
        public uint Columns;
        public IntPtr ColumnOrder;
        public IntPtr ColumnFormats;
        public int Group;
    }

    // user32 里没有不带后缀的 SendMessage，只有 SendMessageA / SendMessageW。不加 CharSet 时运行时按
    // 「SendMessage」去找入口，找不到就抛 EntryPointNotFoundException；这里要的是 W 那一份。
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern int SendMessage(IntPtr window, uint message, int wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern int SendMessage(IntPtr window, uint message, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr window, IntPtr rect, bool erase);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, int size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, IntPtr size, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, ref ListViewItem buffer, int size, out int written);
}