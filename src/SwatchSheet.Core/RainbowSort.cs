namespace SwatchSheet.Core;

/// <summary>
/// Puts colors in rainbow order: red, orange, yellow, green, blue, indigo, violet, then pinks.
/// Greys, whites and blacks (colors with almost no hue) go at the end, lightest to darkest.
/// Inside each slice of the rainbow, lighter shades come before darker ones.
/// </summary>
public static class RainbowSort
{
    /// <summary>Below this saturation a color reads as grey.</summary>
    public const double GreySaturation = 0.12;

    /// <summary>Below this brightness a color reads as black, whatever its hue.</summary>
    public const double BlackValue = 0.10;

    /// <summary>Hue slice width in degrees. Colors in the same slice are ordered by lightness.</summary>
    public const double SliceDegrees = 10;

    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, Rgb> colorOf) =>
        items.Select((item, index) => (item, index, key: Key(colorOf(item))))
             .OrderBy(t => t.key.Group)
             .ThenBy(t => t.key.Slice)
             .ThenBy(t => t.key.Shade)
             .ThenBy(t => t.index) // equal colors keep their order
             .Select(t => t.item)
             .ToList();

    public static bool IsNeutral(Rgb c)
    {
        var (_, s, v) = c.ToHsv();
        return s < GreySaturation || v < BlackValue;
    }

    private static (int Group, int Slice, double Shade) Key(Rgb c)
    {
        var (h, _, v) = c.ToHsv();
        if (IsNeutral(c)) return (1, 0, -c.Luminance);

        // Deep reds just below 360 belong with red at the start, not after the pinks.
        double hue = h >= 345 ? h - 360 : h;
        int slice = (int)Math.Floor((hue + 15) / SliceDegrees);
        return (0, slice, -v - c.Luminance);
    }
}
