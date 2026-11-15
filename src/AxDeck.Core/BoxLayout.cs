namespace AxDeck.Core;

// The place and the size of a box, and the rules that keep a box in its place: the grid of icons its size snaps to, and the edges
// it snaps to when it is dragged near them. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT).

// The edges a box can be dragged by.
public enum BoxResizeDirection { East, North, NorthEast, NorthWest, South, SouthEast, SouthWest, West }

// The window's bounds, as they are read and written.
public readonly record struct BoxBounds(double X, double Y, double Width, double Height);

// The size of a box window: its normal size and the smallest it may be.
public static class BoxWindowSize
{
    public const double DefaultWidth = 320, DefaultHeight = 320;
    public const double MinWidth = 240, MinHeight = 240;
    // The height of the title, which the collapsed box keeps and the grid leaves room for.
    public const double TitleHeight = 40;
    // The padding around the grid of icons.
    public const double GridPadding = 10;
    // Where a new box is put, and how far the next one is moved, so two new boxes do not lie on each other.
    public const double InitialX = 96, InitialY = 96, CascadeStep = 28;
}

// The grid a box's size snaps to, and the edges it snaps to.
public static class BoxLayout
{
    // The height of one row of icons: the icon, the gap under it, the name under that, and the padding around the icon itself.
    // The three numbers come straight from the original's own view of an icon (desktopIcon.ts): the gap under the icon
    // is 6, the padding around the icon is 5 on each side, and the name takes two lines plus two pixels.
    public static double RowHeight(DasktopSettings settings)
    {
        var labelLineHeight = Math.Max(14, Math.Ceiling(settings.LabelTextSize * 1.32));
        var labelBlockHeight = settings.ShowItemLabels ? labelLineHeight * 2 + 2 : 0;
        var labelGap = settings.ShowItemLabels ? DesktopIconView.LabelGap : 0;
        return settings.IconSize + labelGap + labelBlockHeight + DesktopIconView.ItemBlockPadding * 2d;
    }

    // The width of one column of icons: the name under it, or the icon with its own padding when no name is shown.
    // The original measures the icon's own padding as zero (desktopIcon.ts, itemInlinePadding), so the column is as
    // wide as the name, or as the icon itself when the icon is the wider of the two.
    public static double ColumnWidth(DasktopSettings settings)
        => Math.Max(settings.FilenameWidth, settings.IconSize + DesktopIconView.ItemInlinePadding * 2d);

    // The gap between two icons of the same row. The original keeps this apart from the cell: the cell is how big a
    // place an icon takes, the gap is the empty room between two of them, and the two are set by two different sliders
    // (index.vue, the grid's columnGap and rowGap).
    public static double ColumnGap(DasktopSettings settings) => Math.Max(0, settings.IconGapX);

    // The gap between two rows of icons.
    public static double RowGap(DasktopSettings settings) => Math.Max(0, settings.IconGapY);

    // The stride of one column: the place an icon takes plus the gap that follows it. This is the step the icons of a
    // row take, and it is what the layout has to add up correctly when it works out where a dropped file lands.
    public static double ColumnStride(DasktopSettings settings) => ColumnWidth(settings) + ColumnGap(settings);

    // The stride of one row: the height of a row of icons plus the gap that follows it.
    public static double RowStride(DasktopSettings settings) => RowHeight(settings) + RowGap(settings);

    // The bounds of a box snapped to the grid of its icons. The edge opposite the one being dragged stays where the user put it,
    // so snapping moves only the edge that was dragged.
    public static BoxBounds SnapToGrid(BoxBounds bounds, DasktopSettings settings, BoxResizeDirection? direction)
    {
        var cell = ColumnWidth(settings);
        var width = SnapAxis(bounds.Width, cell, settings.IconGapX, BoxWindowSize.MinWidth, BoxWindowSize.GridPadding * 2);
        var row = RowHeight(settings);
        var height = SnapAxis(bounds.Height, row, settings.IconGapY, BoxWindowSize.MinHeight, BoxWindowSize.TitleHeight + BoxWindowSize.GridPadding * 2);
        var x = AnchorsRight(direction) ? bounds.X + bounds.Width - width : bounds.X;
        var y = AnchorsBottom(direction) ? bounds.Y + bounds.Height - height : bounds.Y;
        return new(x, y, width, height);
    }

    // The size of one axis snapped to a whole number of cells, never below the smallest the window may take.
    private static double SnapAxis(double size, double cellSize, double gap, double minimumSize, double padding)
    {
        var stride = cellSize + gap;
        var content = Math.Max(size - padding, cellSize);
        var minimumContent = Math.Max(minimumSize - padding, cellSize);
        var minimumCount = Math.Max(1, Math.Ceiling((minimumContent + gap) / stride));
        var count = Math.Max(minimumCount, Math.Round((content + gap) / stride, MidpointRounding.AwayFromZero));
        return Math.Round(padding + count * cellSize + Math.Max(0, count - 1) * gap);
    }

    // Whether the right edge stays put while the left one is dragged.
    private static bool AnchorsRight(BoxResizeDirection? direction)
        => direction is BoxResizeDirection.West or BoxResizeDirection.NorthWest or BoxResizeDirection.SouthWest;

    // Whether the bottom edge stays put while the top one is dragged.
    private static bool AnchorsBottom(BoxResizeDirection? direction)
        => direction is BoxResizeDirection.North or BoxResizeDirection.NorthEast or BoxResizeDirection.NorthWest;

    // The place of a box that was dragged, snapped to the edges of the screen and of the other boxes. The edges of the screen are
    // the work area, so a box never comes to lie over the taskbar.
    public static (double X, double Y) SnapPosition(double x, double y, double width, double height,
        IReadOnlyList<DesktopBox> others, string? selfId, BoxWorkArea workArea, DasktopSettings settings)
    {
        if (!settings.SnapToEdges) return (x, y);
        var threshold = settings.SnapThreshold;
        var nextX = x;
        var nextY = y;

        foreach (var other in others)
        {
            if (string.Equals(other.Id, selfId, StringComparison.Ordinal)) continue;
            var otherRight = other.X + other.Width;
            var otherBottom = other.Y + other.Height;
            if (Math.Abs(nextX - otherRight) < threshold) nextX = otherRight;
            if (Math.Abs(nextX + width - other.X) < threshold) nextX = other.X - width;
            if (Math.Abs(nextY - otherBottom) < threshold) nextY = otherBottom;
            if (Math.Abs(nextY + height - other.Y) < threshold) nextY = other.Y - height;
        }

        var workRight = workArea.X + workArea.Width;
        var workBottom = workArea.Y + workArea.Height;
        if (Math.Abs(nextX - workArea.X) < threshold) nextX = workArea.X;
        if (Math.Abs(nextX + width - workRight) < threshold) nextX = workRight - width;
        if (Math.Abs(nextY - workArea.Y) < threshold) nextY = workArea.Y;
        if (Math.Abs(nextY + height - workBottom) < threshold) nextY = workBottom - height;
        return (nextX, nextY);
    }

    // Where a box of this size is placed so that it lies wholly on the screen.
    public static BoxBounds KeepInside(BoxBounds bounds, BoxWorkArea workArea)
    {
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        var x = Math.Clamp(bounds.X, workArea.X, workArea.X + Math.Max(0, workArea.Width - width));
        var y = Math.Clamp(bounds.Y, workArea.Y, workArea.Y + Math.Max(0, workArea.Height - height));
        return new(x, y, width, height);
    }
}

// The work area of the screen a box is on: the screen's area without the taskbar.
public readonly record struct BoxWorkArea(double X, double Y, double Width, double Height);

// How the original draws one icon inside a box. The numbers are not ours to choose: the height of a row of icons and
// the size a box snaps to are both worked out from them, so changing one without the other puts the icons and the
// grid out of step. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), desktopIcon.ts.
public static class DesktopIconView
{
    // The room between the icon and the name under it.
    public const double LabelGap = 6;
    // The padding around the icon itself.
    public const double ItemBlockPadding = 5;
    // The padding on each side of the icon.
    public const double ItemInlinePadding = 0;
    // How far the mouse has to move before a press turns into a drag.
    public const double DragStartThreshold = 5;
    // How long a click on an already chosen item waits before it turns into a renaming.
    public const int RenameClickDelayMs = 520;
}
