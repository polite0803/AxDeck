namespace AxDeck.Core;

// 桌面图标的真实位置：Windows 自己把桌面上的图标摆在资源管理器的图标列表里，那个位置就是「手动排好的顺序」的唯一真相。
// 这一层只描述「读到的位置」和「要写的位置」，不碰 Win32——真的去问 Explorer、真的去发消息，是 Native 层那一个适配器的事。
// 视图这边只见接口：测试注入一份假的，CI 就永远碰不到用户的真实桌面。

// 一次位置读取的结果是什么样的：能不能拿、能不能排，由这里说。
public enum DesktopIconPositionStatus
{
    // 每一项都拿到了位置，可以按位置排。
    Ok,
    // 拿不到图标列表（Explorer 没在跑、桌面被 Shell 扩展换掉了），或某一项对不上号。
    // 这不是错误，是可接受的状态：页面退回按名字自动排。
    Unavailable,
    // 资源管理器开了「自动排列图标」：写进去的位置会被它当场改回去，所以排序在这里没有意义。
    AutoArrange,
}

// 一个槽位：图标在列表里的那一处，是资源管理器的客户区坐标，不是屏幕坐标。
// 用 double 而不是 POINT，是因为视图那一侧的落点计算本来就是 WPF 的 DIP；真正的整数坐标在写的时候才化。
public readonly record struct DesktopIconSlot(double X, double Y);

// 读回来的一份快照：每项的位置，键是 DesktopItems.Item.Path（系统项是它的解析名），所以两边能直接比。
public sealed record DesktopIconSnapshot(DesktopIconPositionStatus Status, IReadOnlyDictionary<string, DesktopIconSlot> Slots)
{
    // 拿不到位置时都是空的，调用方先看 Status 再取 Slots。
    public static DesktopIconSnapshot Unavailable(DesktopIconPositionStatus status = DesktopIconPositionStatus.Unavailable)
        => new(status, new Dictionary<string, DesktopIconSlot>());
}

// 要写出去的一个位置：这一项，摆到这里。
public readonly record struct DesktopIconPlacement(string Key, double X, double Y);

// 写一次的回报：成功与否，以及不成功是因为什么。失败一律是返回值，不抛异常到 UI 线程。
public sealed record DesktopIconWriteResult(bool Ok, DesktopIconPositionStatus Status)
{
    public static readonly DesktopIconWriteResult Done = new(true, DesktopIconPositionStatus.Ok);
    public static DesktopIconWriteResult Failed(DesktopIconPositionStatus status) => new(false, status);
}

// 桌面图标位置的读写接口。
public interface IDesktopIconPositions
{
    // 现在这些图标各摆在哪里。读不到就返回 Unavailable，不要猜。
    DesktopIconSnapshot Read();
    // 把它们摆成这样。只有 Read 给出过位置的项会被移动，没给出的留着不动。
    DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements);
}

// 永远读不到位置的实现：没有注入就用它。它一个 Win32 都不碰，所以测试就算忘了换，也不会动到用户的桌面。
public sealed class NullDesktopIconPositions : IDesktopIconPositions
{
    public DesktopIconSnapshot Read() => DesktopIconSnapshot.Unavailable();
    public DesktopIconWriteResult Write(IReadOnlyList<DesktopIconPlacement> placements)
        => DesktopIconWriteResult.Failed(DesktopIconPositionStatus.Unavailable);
}
