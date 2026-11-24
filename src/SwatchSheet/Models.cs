using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>One color on the sheet. Never changes once made, so undo can just keep old lists.</summary>
public sealed class SwatchItem
{
    public SwatchItem(Rgb value, string? source)
    {
        Value = value;
        Source = source;
        Brush = new SolidColorBrush(Color.FromRgb(value.R, value.G, value.B));
        Brush.Freeze();
    }

    public Rgb Value { get; }
    public string? Source { get; }
    public SolidColorBrush Brush { get; }
    public string Hex => Value.Hex;
    public string RgbShort => $"{Value.R}, {Value.G}, {Value.B}";
    public string Tip =>
        $"{Hex}   {Value.RgbText}" + (Source is null ? "" : $"\nfrom {Source}") +
        "\n\nClick to copy  |  drag to move";
}

/// <summary>An image in the left-hand list.</summary>
public sealed class ImageEntry
{
    public required string Name { get; init; }
    /// <summary>Null for pasted images, which only live in memory.</summary>
    public string? FilePath { get; init; }
    public required BitmapSource Thumb { get; init; }
    public required string SizeText { get; init; }
    /// <summary>Pasted images keep their pixels here, since there's no file to reload them from.</summary>
    public PixelImage? Kept { get; init; }
    public string Tip => FilePath ?? "Pasted from the clipboard (not saved when you close the app)";
}

/// <summary>A saved palette in the Palettes list, with a strip of its first colors.</summary>
public sealed class PaletteItem
{
    private const int StripColors = 14;

    public PaletteItem(PaletteInfo info)
    {
        Info = info;
        Strip = info.Colors.Take(StripColors).Select(c =>
        {
            var b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            b.Freeze();
            return b;
        }).ToList();
    }

    public PaletteInfo Info { get; }
    public string Title => Info.Title;
    public IReadOnlyList<SolidColorBrush> Strip { get; }
    public string Details
    {
        get
        {
            int n = Info.Colors.Count;
            var count = n == 1 ? "1 color" : $"{n} colors";
            return Info.Date.Length > 0 ? $"{Info.Date}  ·  {count}" : count;
        }
    }
    public string Tip => $"Open \"{Info.Title}\"\n{Info.FilePath}";
}

/// <summary>Everything about the sheet that isn't the colors. Kept in undo only when opening a palette.</summary>
public sealed record SheetText(string Title, string Description, string Date, bool ShowDate);

/// <summary>An undo step. Text is null for color-only changes, so undo never wipes what you typed.</summary>
public sealed record SheetState(List<SwatchItem> Swatches, SheetText? Text);

/// <summary>One line in the copy preview: an optional color chip, then text.</summary>
public sealed class PreviewLine
{
    public required IReadOnlyList<PreviewSegment> Segments { get; init; }
}

public sealed class PreviewSegment
{
    public required string Text { get; init; }
    public Brush? Chip { get; init; }
    public bool HasChip => Chip is not null;
    /// <summary>The sheet title line.</summary>
    public bool Bold { get; init; }
    /// <summary>The description line.</summary>
    public bool Muted { get; init; }
}
