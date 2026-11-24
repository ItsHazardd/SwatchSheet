using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>Opens image files with Windows' built-in decoders. Safe to call from a background thread.</summary>
public static class ImageLoader
{
    public static readonly string[] Extensions =
        { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".heic" };

    public const string DialogFilter =
        "Images|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico;*.heic|All files|*.*";

    public static bool LooksLikeImage(string path) =>
        Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public const int ThumbWidth = 168;
    public const int ThumbHeight = 104;

    public sealed record Loaded(PixelImage Pixels, BitmapSource Thumb);

    public static Loaded LoadFile(string path)
    {
        // Read the whole file first so it isn't kept locked while the app is open.
        var bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var pixels = ToPixels(ApplyOrientation(frame, ReadOrientation(frame)));
        return new Loaded(pixels, MakeThumb(pixels));
    }

    public static BitmapSource MakeThumb(PixelImage pixels) => ToBitmap(pixels.Downscale(ThumbWidth * 2, ThumbHeight * 2));

    public static PixelImage ToPixels(BitmapSource source)
    {
        long count = (long)source.PixelWidth * source.PixelHeight;
        if (count > PixelImage.MaxPixels)
            throw new InvalidOperationException(
                $"This image is too big to open ({source.PixelWidth} x {source.PixelHeight} pixels).");
        BitmapSource bgra = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = bgra.PixelWidth * 4;
        var data = new byte[(long)stride * bgra.PixelHeight];
        bgra.CopyPixels(data, stride, 0);
        return new PixelImage(bgra.PixelWidth, bgra.PixelHeight, stride, data);
    }

    public static BitmapSource ToBitmap(PixelImage pixels)
    {
        var bitmap = BitmapSource.Create(pixels.Width, pixels.Height, 96, 96, PixelFormats.Bgra32, null, pixels.Pixels, pixels.Stride);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Phone photos are often stored sideways with a note saying which way is up.</summary>
    private static int ReadOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata meta && meta.ContainsQuery("System.Photo.Orientation") &&
                meta.GetQuery("System.Photo.Orientation") is ushort value)
                return value;
        }
        catch
        {
            // Some formats don't support this question; they're never rotated.
        }
        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return transform is null ? source : new TransformedBitmap(source, transform);
    }
}
