namespace SwatchSheet.Core;

/// <summary>
/// An image's pixels in memory: 4 bytes per pixel in Blue, Green, Red, Alpha order
/// (the layout Windows uses), row after row.
/// </summary>
public sealed class PixelImage
{
    /// <summary>Biggest image we'll open (about 150 megapixels, ~600 MB of memory).</summary>
    public const long MaxPixels = 150_000_000;

    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public byte[] Pixels { get; }

    public PixelImage(int width, int height, int stride, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("The image has no pixels.");
        if (stride < width * 4) throw new ArgumentException("Row size is too small for the width.");
        if (pixels.Length < (long)stride * (height - 1) + width * 4) throw new ArgumentException("Not enough pixel data.");
        Width = width;
        Height = height;
        Stride = stride;
        Pixels = pixels;
    }

    public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

    /// <summary>Reads one pixel. Returns false when (x, y) is outside the image.</summary>
    public bool TryGetPixel(int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        r = g = b = a = 0;
        if (!Contains(x, y)) return false;
        int i = y * Stride + x * 4;
        b = Pixels[i];
        g = Pixels[i + 1];
        r = Pixels[i + 2];
        a = Pixels[i + 3];
        return true;
    }

    /// <summary>
    /// The color at (x, y). With radius 1 it averages a 3x3 block, radius 2 a 5x5 block, which
    /// smooths out noise in photos. See-through pixels count less (and fully see-through ones
    /// not at all). Returns null when the spot is outside the image or completely see-through.
    /// </summary>
    public Rgb? Sample(int x, int y, int radius = 0)
    {
        if (!Contains(x, y)) return null;
        radius = Math.Max(0, radius);
        double sr = 0, sg = 0, sb = 0, sa = 0;
        for (int py = y - radius; py <= y + radius; py++)
        {
            for (int px = x - radius; px <= x + radius; px++)
            {
                if (!TryGetPixel(px, py, out var r, out var g, out var b, out var a) || a == 0) continue;
                sr += r * (double)a;
                sg += g * (double)a;
                sb += b * (double)a;
                sa += a;
            }
        }
        if (sa == 0) return null;
        return new Rgb(Round(sr / sa), Round(sg / sa), Round(sb / sa));
    }

    private static byte Round(double v) => (byte)Math.Clamp(Math.Round(v, MidpointRounding.AwayFromZero), 0, 255);

    /// <summary>
    /// Some programs put screenshots on the clipboard with every pixel marked fully see-through
    /// even though the picture is solid. If the whole image is see-through, make it solid.
    /// Returns true if it changed anything.
    /// </summary>
    public bool FixAllTransparent()
    {
        for (int y = 0; y < Height; y++)
        {
            int row = y * Stride;
            for (int x = 0; x < Width; x++)
                if (Pixels[row + x * 4 + 3] != 0) return false;
        }
        for (int y = 0; y < Height; y++)
        {
            int row = y * Stride;
            for (int x = 0; x < Width; x++) Pixels[row + x * 4 + 3] = 255;
        }
        return true;
    }

    /// <summary>A smaller copy (for thumbnails) that fits inside maxWidth x maxHeight. Never enlarges.</summary>
    public PixelImage Downscale(int maxWidth, int maxHeight)
    {
        double scale = Math.Min(1.0, Math.Min(maxWidth / (double)Width, maxHeight / (double)Height));
        int nw = Math.Max(1, (int)Math.Round(Width * scale));
        int nh = Math.Max(1, (int)Math.Round(Height * scale));
        var output = new byte[nw * nh * 4];

        for (int ty = 0; ty < nh; ty++)
        {
            int y0 = (int)((long)ty * Height / nh);
            int y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * Height / nh));
            for (int tx = 0; tx < nw; tx++)
            {
                int x0 = (int)((long)tx * Width / nw);
                int x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * Width / nw));
                long b = 0, g = 0, r = 0, a = 0, n = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * Stride;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = row + x * 4;
                        b += Pixels[i]; g += Pixels[i + 1]; r += Pixels[i + 2]; a += Pixels[i + 3];
                        n++;
                    }
                }
                int o = (ty * nw + tx) * 4;
                output[o] = (byte)(b / n);
                output[o + 1] = (byte)(g / n);
                output[o + 2] = (byte)(r / n);
                output[o + 3] = (byte)(a / n);
            }
        }
        return new PixelImage(nw, nh, nw * 4, output);
    }
}
