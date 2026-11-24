using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>Draws the whole sheet as a picture: a grid of swatches with their codes underneath.</summary>
public static class SheetImage
{
    private const double Scale = 2; // drawn at double size so it stays sharp when pasted

    private const double TextWidthWithNotes = 620;  // picture is at least this wide when there's a title or description
    private const double MaxTitleWidth = 1000;

    /// <param name="date">Shown next to the color count under the title. Null or empty leaves it out.</param>
    public static BitmapSource Render(IReadOnlyList<Rgb> colors, string? title = null, string? description = null, string? date = null)
    {
        title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        date = string.IsNullOrWhiteSpace(date) ? null : date.Trim();

        var bg = Brush("#14161B");
        var text = Brush("#ECEEF3");
        var soft = Brush("#B4BAC8");
        var muted = Brush("#8A92A6");
        var edge = new Pen(Brush("#26FFFFFF"), 1);
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var regular = new Typeface("Segoe UI");
        var mono = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        var monoLight = new Typeface(new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double m = SheetLayout.Margin;

        // Work out how wide the picture needs to be, then wrap the title and description to fit.
        var titleText = Text(title ?? "Color sheet", bold, 26, text);
        double width = SheetLayout.GridWidth(colors.Count);
        if (title is not null || description is not null) width = Math.Max(width, TextWidthWithNotes);
        width = Math.Max(width, Math.Min(MaxTitleWidth, titleText.Width + 2 * m + 2));
        titleText.MaxTextWidth = width - 2 * m;

        FormattedText? descText = null;
        if (description is not null)
        {
            descText = Text(description, regular, 14, soft);
            descText.MaxTextWidth = width - 2 * m;
            descText.LineHeight = 20;
        }

        // Title, then "date · N colors" right under it, then the description.
        double subtitleTop = m - 4 + titleText.Height + 4;
        double descTop = subtitleTop + 22 + 6;
        double headerBottom = descText is null ? subtitleTop + 22 : descTop + descText.Height;
        double headerHeight = headerBottom + 22 - m;
        var layout = SheetLayout.Compute(colors.Count, headerHeight, width);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(bg, null, new Rect(0, 0, layout.Width, layout.Height));
            dc.DrawRectangle((Brush)Application.Current.FindResource("RainbowBrush"), null, new Rect(0, 0, layout.Width, 5));

            dc.DrawText(titleText, new Point(m, m - 4));
            if (descText is not null) dc.DrawText(descText, new Point(m, descTop));
            var count = $"{colors.Count} color{(colors.Count == 1 ? "" : "s")}";
            var subtitle = date is null ? count : $"{date}  ·  {count}";
            dc.DrawText(Text(subtitle, regular, 13, muted), new Point(m, subtitleTop));

            foreach (var cell in layout.Cells)
            {
                var c = colors[cell.Index];
                var rect = new Rect(cell.X, cell.Y, SheetLayout.SwatchWidth, SheetLayout.SwatchHeight);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), edge, rect, 12, 12);
                dc.DrawText(Text(c.Hex, mono, 15, text), new Point(cell.X + 2, rect.Bottom + 8));
                dc.DrawText(Text($"{c.R}, {c.G}, {c.B}", monoLight, 11.5, muted), new Point(cell.X + 2, rect.Bottom + 28));
            }
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(layout.Width * Scale), (int)Math.Ceiling(layout.Height * Scale),
            96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private static FormattedText Text(string s, Typeface face, double size, Brush brush) =>
        new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, Scale);
}
