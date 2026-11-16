namespace AxDeck.Core;

// 「桌面」页的格子怎么排：一行多高、一格多宽、两格之间留多少空。
// 最初照 Dasktop（https://github.com/AugSakura/Dasktop, MIT）那套分组窗口的排法写，分组删掉之后只剩这一页在用。

public static class BoxLayout
{
    // The height of one row of icons: the icon, the gap under it, the name under that, and the padding around the icon itself.
    // The three numbers come straight from the original's own view of an icon (desktopIcon.ts): the gap under the icon
    // is 6, the padding around the icon is 5 on each side, and the name takes two lines plus two pixels.
    public static double RowHeight(DesktopLookSettings settings)
    {
        var labelLineHeight = Math.Max(14, Math.Ceiling(settings.LabelTextSize * 1.32));
        var labelBlockHeight = settings.ShowItemLabels ? labelLineHeight * 2 + 2 : 0;
        var labelGap = settings.ShowItemLabels ? DesktopIconView.LabelGap : 0;
        return settings.IconSize + labelGap + labelBlockHeight + DesktopIconView.ItemBlockPadding * 2d;
    }

    // The width of one column of icons: the name under it, or the icon with its own padding when no name is shown.
    // The original measures the icon's own padding as zero (desktopIcon.ts, itemInlinePadding), so the column is as
    // wide as the name, or as the icon itself when the icon is the wider of the two.
    public static double ColumnWidth(DesktopLookSettings settings)
        => Math.Max(settings.FilenameWidth, settings.IconSize + DesktopIconView.ItemInlinePadding * 2d);

    // The gap between two icons of the same row. The original keeps this apart from the cell: the cell is how big a
    // place an icon takes, the gap is the empty room between two of them, and the two are set by two different sliders
    // (index.vue, the grid's columnGap and rowGap).
    public static double ColumnGap(DesktopLookSettings settings) => Math.Max(0, settings.IconGapX);

    // The gap between two rows of icons.
    public static double RowGap(DesktopLookSettings settings) => Math.Max(0, settings.IconGapY);

    // The stride of one column: the place an icon takes plus the gap that follows it. This is the step the icons of a
    // row take, and it is what the layout has to add up correctly when it works out where a dropped file lands.
    public static double ColumnStride(DesktopLookSettings settings) => ColumnWidth(settings) + ColumnGap(settings);

    // The stride of one row: the height of a row of icons plus the gap that follows it.
    public static double RowStride(DesktopLookSettings settings) => RowHeight(settings) + RowGap(settings);
}

// How the original draws one icon on the page. The numbers are not ours to choose: the height of a row of icons
// is worked out from them, so changing one without the other puts the icons and the grid out of step.
// Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), desktopIcon.ts.
public static class DesktopIconView
{
    // The room between the icon and the name under it.
    public const double LabelGap = 6;
    // The padding around the icon itself.
    public const double ItemBlockPadding = 5;
    // The padding on each side of the icon.
    public const double ItemInlinePadding = 0;
}
