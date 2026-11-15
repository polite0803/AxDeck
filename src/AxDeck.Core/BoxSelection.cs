namespace AxDeck.Core;

// The selection in a box's file area: which items are picked, and how a drag across the area picks them.
// Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), whose file area selects the way Explorer does:
// a click picks one, Ctrl picks several, and a drag across the area picks everything it touches.

public static class BoxSelection
{
    // A rectangle in the file area's own coordinates.
    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    // A point in the file area's own coordinates.
    public readonly record struct Point(double X, double Y);

    // The rectangle from one corner to the other, whichever way the drag went.
    public static Rect Normalize(Point start, Point current)
        => new(Math.Min(start.X, current.X), Math.Min(start.Y, current.Y),
            Math.Abs(current.X - start.X), Math.Abs(current.Y - start.Y));

    // Whether two rectangles touch each other at all.
    public static bool Intersects(Rect left, Rect right)
        => left.Left < right.Right && left.Right > right.Left && left.Top < right.Bottom && left.Bottom > right.Top;

    // The items a rectangle covers, in the order they are given.
    public static IReadOnlyList<string> ItemsIn(Rect area, IReadOnlyList<(string Key, Rect Rect)> items)
        => [.. items.Where(item => Intersects(area, item.Rect)).Select(item => item.Key)];

    // What a click does to the selection: a plain click leaves only that item, Ctrl adds or takes it away.
    public static IReadOnlyList<string> AfterClick(IReadOnlyList<string> selection, string key, bool toggle)
    {
        if (!toggle) return [key];
        var next = new List<string>(selection);
        if (!next.Remove(key)) next.Add(key);
        return next;
    }

    // Whether a click on an item should start the renaming: it should when that item is the only one chosen, and the
    // setting says a double click opens the item. This is how Explorer behaves: clicking a name that is already
    // selected makes it editable, but only after a moment, in case a double click was meant.
    public static bool ShouldRenameOnClick(IReadOnlyCollection<string> selection, string key, bool doubleClickOpen)
        => doubleClickOpen && selection.Count == 1 && selection.Contains(key);

    // What a drag across the area does to the selection: the items the rectangle touches join it.
    public static IReadOnlyList<string> AfterDrag(IReadOnlyList<string> selection, IReadOnlyList<string> touched)
    {
        var next = new List<string>(selection);
        foreach (var key in touched) if (!next.Contains(key)) next.Add(key);
        return next;
    }
}

// The keys the file area answers to, the same ones Explorer answers to. Ported from Dasktop
// (https://github.com/AugSakura/Dasktop, MIT), whose file area takes Ctrl/Meta+A, C, X, V, F2, Delete, Enter and Escape
// (useBoxFileSelection.ts, handleFileViewKeydown).
public static class BoxKeyboard
{
    // What a key press means in the file area.
    public enum Command
    {
        // 什么都没有：这个键不归这一块管。
        None,
        // 选中这一块里的每一项。
        SelectAll,
        // 把选中的复制到剪贴板。
        Copy,
        // 把选中的剪切到剪贴板。
        Cut,
        // 把剪贴板上的东西贴进来。
        Paste,
        // 给选中的第一项改名。
        Rename,
        // 把选中的删掉。
        Delete,
        // 打开选中的第一项。
        Open,
        // 把选区清掉。
        Clear
    }

    // 这一组键要不要带 Ctrl（或者 Meta）才算数。
    public const bool NeedsControl = true;

    // 这个键在这个块里是什么意思。
    // 修饰键由外面给：真实输入时它是键盘的状态，测试时它可以是别的来源——
    // 两种情况下走的都是这一段逻辑，所以行为不会走岔。
    public static Command For(string key, bool control)
    {
        if (control)
        {
            if (key == "A") return Command.SelectAll;
            if (key == "C") return Command.Copy;
            if (key == "X") return Command.Cut;
            if (key == "V") return Command.Paste;
            return Command.None;
        }

        if (key == "F2") return Command.Rename;
        if (key == "Delete") return Command.Delete;
        if (key == "Enter") return Command.Open;
        if (key == "Escape") return Command.Clear;
        return Command.None;
    }

    // 这个键是不是输入框自己的（改名时那些键得留给输入框）。
    public static bool IsEditableKey(string key) => key is "Enter" or "Escape";
}
