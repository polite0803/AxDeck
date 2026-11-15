namespace AxDeck.Core;

// The collapsing of a box: a box that is folded keeps only its title, and unfolds again while the mouse is on it.
// Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), whose collapsed box keeps its title bar as the way in
// (useBoxCollapsePreview.ts): the window itself becomes as tall as the title, and unfolds when the pointer comes to it.

// What a box is doing about its own folding.
public static class BoxCollapse
{
    // Everything that is happening to the box right now, which may hold it open.
    public readonly record struct State
    {
        // The mouse is over the box.
        public bool BoxHovered { get; init; }
        // The mouse is over the title of the box.
        public bool TitleHovered { get; init; }
        // A file is being dragged over the box.
        public bool DragHovering { get; init; }
        // The shell's own menu is open on one of the items.
        public bool NativeMenuOpen { get; init; }
        // The box's own menu is open.
        public bool BoxMenuOpen { get; init; }
        // The title is being edited.
        public bool EditingTitle { get; init; }
        // The box is being dragged by its title.
        public bool Dragging { get; init; }
        // The mouse is on one of the edges that resize the box.
        public bool ResizeHandleHovered { get; init; }
        // The box is being resized.
        public bool Resizing { get; init; }
    }

    // Whether something is happening to the box that must not be interrupted by folding it away.
    // This is the original's list, one for one (useBoxCollapsePreview.ts, shouldKeepCollapsedPreviewOpen).
    public static bool KeepsOpen(State state)
        => state.BoxHovered || state.TitleHovered || state.DragHovering || state.NativeMenuOpen
            || state.BoxMenuOpen || state.EditingTitle || state.Dragging || state.ResizeHandleHovered || state.Resizing;

    // Whether the box should be folded right now: it is set to fold, nothing is happening to it, and it is not
    // momentarily unfolded.
    public static bool ShouldFold(bool collapsed, bool unfolded, State state)
        => collapsed && !unfolded && !KeepsOpen(state);

    // The height a folded box has: only its title is left. The original keeps the title bar as the way in
    // (useBoxCollapsePreview.ts, collapsedWindowHeight).
    public static double FoldedHeight => BoxWindowSize.TitleHeight;

    // The height of the window: folded it is the title, unfolded it is the height the user gave it.
    public static double HeightFor(bool folded, double height) => folded ? FoldedHeight : height;

    // Whether the contents of the box are shown. A folded box shows only its title, and the contents take no mouse,
    // so a click there falls through to the desktop (useBoxCollapsePreview.ts, boxBodyStyle).
    public static bool ContentsVisible(bool folded) => !folded;

    // How long the contents stay open after the mouse has left: the original waits, so that moving the mouse towards the
    // edge that resizes the box, or towards a menu, does not fold it away on the way (useBoxCollapsePreview.ts,
    // scheduleCollapsedPreviewClose).
    public static int CloseDelayMs(DasktopSettings settings) => Math.Max(0, settings.CollapseDelayMs);

    // How long the box waits before it fades out once the mouse has left. The original puts a short delay in front of
    // the fade, so the user sees the box fold away first and it does not fade while it is still moving
    // (useBoxCollapsePreview.ts, resolveBoxIdleOpacityAnimationDelay).
    public static int FadeOutDelayMs(DasktopSettings settings)
        => (int)Math.Min(Math.Max(1, settings.CollapseAnimationMs) * 0.2, 140);
}

// The title of a box, while the user is writing it. Ported from Dasktop
// (https://github.com/AugSakura/Dasktop, MIT), whose title editing keeps the name of the box and leaves the real files
// alone (useBoxTitleEditing.ts).
public static class BoxTitleEditing
{
    // The name to save when the editing is done. Empty is allowed: the original lets a box have no name at all
    // (useBoxTitleEditing.ts, commitTitleEditing).
    public static string? Commit(string draft, string current)
    {
        var next = (draft ?? "").Trim();
        // 没有改就不写：改一下标题不该多动一次数据。
        if (next == current) return null;
        return next;
    }

    // The name to go back to when the editing is called off. It changes nothing on disk
    // (useBoxTitleEditing.ts, cancelTitleEditing).
    public static string Cancel(string current) => current;

    // Whether a click on the title starts the editing: it is the second click of a double click.
    public static bool IsDoubleClick(int clickCount) => clickCount >= 2;
}
