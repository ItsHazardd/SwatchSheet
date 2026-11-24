namespace SwatchSheet.Core;

/// <summary>Where each swatch goes on the "Sheet image" picture.</summary>
public static class SheetLayout
{
    public const double SwatchWidth = 140;
    public const double SwatchHeight = 104;
    public const double LabelHeight = 46;
    public const double Gap = 18;
    public const double Margin = 36;
    public const double HeaderHeight = 74;

    public sealed record Cell(int Index, double X, double Y);
    public sealed record Result(int Columns, int Rows, double Width, double Height, IReadOnlyList<Cell> Cells);

    /// <summary>A roughly wide-rectangle grid with rows as even as possible.</summary>
    public static int AutoColumns(int count)
    {
        if (count <= 1) return 1;
        int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(count * 1.5)), 1, 8);
        int rows = (int)Math.Ceiling(count / (double)cols);
        return Math.Min(count, (int)Math.Ceiling(count / (double)rows));
    }

    /// <summary>Width of the swatch grid alone, margins included.</summary>
    public static double GridWidth(int count)
    {
        int cols = AutoColumns(count);
        return Margin * 2 + cols * SwatchWidth + (cols - 1) * Gap;
    }

    /// <param name="headerHeight">Space for the title, description and color count.</param>
    /// <param name="minWidth">Makes the picture at least this wide (room for a long title or description).</param>
    public static Result Compute(int count, double headerHeight = HeaderHeight, double minWidth = 0)
    {
        int cols = AutoColumns(count);
        int rows = Math.Max(1, (int)Math.Ceiling(count / (double)cols));
        double cellH = SwatchHeight + LabelHeight;
        double width = Math.Max(GridWidth(count), minWidth);
        double height = Margin + headerHeight + rows * cellH + (rows - 1) * Gap + Margin;
        var cells = new List<Cell>(count);
        for (int i = 0; i < count; i++)
        {
            int c = i % cols, r = i / cols;
            cells.Add(new Cell(i, Margin + c * (SwatchWidth + Gap), Margin + headerHeight + r * (cellH + Gap)));
        }
        return new Result(cols, rows, width, height, cells);
    }
}
