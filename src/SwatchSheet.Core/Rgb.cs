using System.Globalization;
using System.Text.RegularExpressions;

namespace SwatchSheet.Core;

/// <summary>One color, 0-255 per channel.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string RgbText => $"rgb({R}, {G}, {B})";

    /// <summary>Hue 0-360, saturation 0-1, value (brightness) 0-1.</summary>
    public (double H, double S, double V) ToHsv()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
        }
        double s = max == 0 ? 0 : d / max;
        return (h, s, max);
    }

    /// <summary>How bright the color looks to the eye, 0 (black) to 1 (white).</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c)
            {
                double v = c / 255.0;
                return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    /// <summary>True when dark text reads better on this color than light text.</summary>
    public bool IsLight => Luminance > 0.179;

    /// <summary>
    /// The average of a set of colors: red, green and blue are each averaged and rounded.
    /// Null if there are no colors.
    /// </summary>
    public static Rgb? Average(IEnumerable<Rgb> colors)
    {
        long r = 0, g = 0, b = 0, n = 0;
        foreach (var c in colors) { r += c.R; g += c.G; b += c.B; n++; }
        if (n == 0) return null;
        static byte Round(long sum, long count) => (byte)Math.Round((double)sum / count, MidpointRounding.AwayFromZero);
        return new Rgb(Round(r, n), Round(g, n), Round(b, n));
    }

    /// <summary>Reads "#RGB" or "#RRGGBB" (the # is optional, spaces are ignored).</summary>
    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length == 3) s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        if (s.Length != 6) return false;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>Finds colors written in any text, e.g. pasted from a website or a notes app.</summary>
public static partial class ColorText
{
    // #RGB, #RRGGBB, or #RRGGBBAA (the alpha part is ignored). The # is required so ordinary
    // words that happen to be hex letters ("cafe", "decade") aren't mistaken for colors.
    [GeneratedRegex(@"#([0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3})(?![0-9a-fA-F])")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex RgbPattern();

    /// <summary>Every color in the text, in the order they appear.</summary>
    public static List<Rgb> FindColors(string? text)
    {
        var found = new List<(int Index, Rgb Color)>();
        if (string.IsNullOrEmpty(text)) return new();

        foreach (Match m in HexPattern().Matches(text))
        {
            var digits = m.Groups[1].Value;
            if (digits.Length == 8) digits = digits[..6];
            if (Rgb.TryParseHex(digits, out var c)) found.Add((m.Index, c));
        }
        foreach (Match m in RgbPattern().Matches(text))
        {
            int r = int.Parse(m.Groups[1].Value), g = int.Parse(m.Groups[2].Value), b = int.Parse(m.Groups[3].Value);
            if (r > 255 || g > 255 || b > 255) continue;
            found.Add((m.Index, new Rgb((byte)r, (byte)g, (byte)b)));
        }
        return found.OrderBy(f => f.Index).Select(f => f.Color).ToList();
    }
}
