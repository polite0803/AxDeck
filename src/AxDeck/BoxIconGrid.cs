using System.Windows;
using System.Windows.Controls;

namespace AxDeck;

// The grid of icons inside a box: a row of equally wide places, each one as tall as a row of icons, with a gap between
// two of them. Ported from Dasktop (https://github.com/AugSakura/Dasktop, MIT), where the icons sit in a CSS grid whose
// columnGap and rowGap are the two sliders the user sets (index.vue, boxGridStyle).
//
// The grid there and this one differ in one thing that matters: there the gap is room between two places, and the place
// itself does not grow because of it. A WrapPanel cannot do that, because its item width is the width of the place. So
// the places are laid out here: each child is given a cell of its own, and the gap is room between the cells.
internal sealed class BoxIconGrid : Panel
{
    // The width of one place, and the height of one row.
    public double CellWidth { get; set; } = 80;
    public double CellHeight { get; set; } = 80;
    // The room between two places of the same row, and between two rows.
    public double ColumnGap { get; set; }
    public double RowGap { get; set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var count = Children.Count;
        if (count == 0) return new Size(0, 0);

        // How many places fit on a row: the grid is as wide as it is given, and the places do not shrink.
        var usable = double.IsInfinity(availableSize.Width) ? availableSize.Width : Math.Max(0, availableSize.Width);
        var stride = CellWidth + ColumnGap;
        var columns = double.IsInfinity(usable) || usable <= 0
            ? count
            : Math.Max(1, (int)Math.Floor((usable + ColumnGap) / stride));
        columns = Math.Min(columns, count);

        foreach (var child in Children.OfType<UIElement>())
        {
            child.Measure(new Size(CellWidth, CellHeight));
        }

        var rows = (int)Math.Ceiling(count / (double)columns);
        var width = columns * CellWidth + Math.Max(0, columns - 1) * ColumnGap;
        var height = rows * CellHeight + Math.Max(0, rows - 1) * RowGap;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = Children.Count;
        if (count == 0) return new Size(0, 0);

        var stride = CellWidth + ColumnGap;
        var columns = Math.Max(1, (int)Math.Floor((finalSize.Width + ColumnGap) / stride));
        columns = Math.Min(columns, count);

        var index = 0;
        foreach (var child in Children.OfType<UIElement>())
        {
            var row = index / columns;
            var column = index % columns;
            var x = column * stride;
            var y = row * (CellHeight + RowGap);
            child.Arrange(new Rect(x, y, CellWidth, CellHeight));
            index++;
        }

        var rows = (int)Math.Ceiling(count / (double)columns);
        var height = rows * CellHeight + Math.Max(0, rows - 1) * RowGap;
        return new Size(finalSize.Width, height);
    }
}
