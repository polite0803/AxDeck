namespace AxDeck.Core;

// AxDeck 接管桌面这件事的两步：进来时把 Windows 自己画的桌面图标藏起来，出去时照原样放回去。
//
// 为什么要记下原样：用户可能本来就把桌面图标关掉了。AxDeck 退出时一律写"显示"，就等于替用户
// 改了他的设置。所以进来时先问一句现在是显是藏，把它记住，出去时写回记住的那个。
//
// 这一层不碰注册表：现在显不显、要不要写，都由外面问 Windows 得到答案再递进来。
public static class DesktopTakeOver
{
    // 接管前桌面是什么样，以及 AxDeck 有没有动过它。
    public readonly record struct State(bool WasVisible, bool Taken)
    {
        // 没接管过的状态：退出时什么也不该写。
        public static State Untouched { get; } = new(WasVisible: true, Taken: false);
    }

    // 接管：桌面现在是显示的，就记住"原来是显示的"并把它藏起来；本来就藏着，什么也不做——
    // 那不是 AxDeck 藏的，退出时也不该由 AxDeck 去显示。
    // wanted 是用户要不要 AxDeck 接管桌面；关掉了就只是镜像一份，真实桌面照旧。
    public static State Take(bool iconsVisible, bool wanted)
    {
        if (!wanted || !iconsVisible) return State.Untouched;
        return new State(WasVisible: true, Taken: true);
    }

    // 接管之后要不要写注册表把图标藏起来。
    public static bool ShouldHide(State state) => state.Taken;

    // 退出：只有 AxDeck 真的藏过，才写回去；写回的是接管前记下的那个状态。
    public static bool ShouldRestore(State state) => state.Taken;

    // 退出时要写回的值。没接管过时这个值没有意义，先看 ShouldRestore。
    public static bool RestoreTo(State state) => state.WasVisible;

    // ---- 没交还就没了的情况 ----

    // 藏图标之前先落一个记号；交还之后把记号删掉。记号还在，就说明上一次运行没有走到交还那一步：
    // 任务管理器里结束进程、程序崩溃、断电，退出代码都没有机会跑。这时 Windows 的桌面图标还藏着，
    // 而记录"原来是什么样"的内存也跟着进程一起没了，只能按"放回去"处理——能被 A 藏起来的图标，
    // 前提本来就是它当时是显示着的（见 Take），所以放回去就是它原来的样子。
    public static bool NeedsRecovery(bool markerExists) => markerExists;

    // 要不要落记号：只有真的要藏图标时才落，别的两种情况没动过桌面，不该留下任何东西。
    public static bool ShouldMark(State state) => state.Taken;
}