using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>
/// Copy and paste. Other programs (Discord, OneNote, clipboard history...) sometimes hold the
/// clipboard open for a moment, so every call retries for a while instead of failing at once.
/// </summary>
public static class ClipboardTools
{
    private const int Attempts = 15;
    private const int DelayMs = 50;

    public static Task<bool> SetTextAsync(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        return SetAsync(data, "text");
    }

    /// <summary>
    /// Plain text plus HTML and RTF versions. OneNote, Word, Google Docs and email paste the
    /// colored version; Notepad, Discord and code editors paste the plain text.
    /// </summary>
    /// The title and description only go in the colored versions, so plain-text pastes stay just codes.
    public static Task<bool> SetRichAsync(IReadOnlyList<CopyLine> lines, string? title = null, string? description = null, string? date = null)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, CopyFormats.PlainText(lines));
        data.SetData(DataFormats.Html, new MemoryStream(CopyFormats.ClipboardHtml(CopyFormats.HtmlFragment(lines, title, description, date))));
        data.SetData(DataFormats.Rtf, CopyFormats.Rtf(lines, title, description, date));
        return SetAsync(data, "colors");
    }

    /// <summary>A picture, as PNG (keeps exact colors) plus the classic bitmap most programs understand.</summary>
    public static Task<bool> SetImageAsync(BitmapSource image)
    {
        var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(png);

        var solid = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        solid.Freeze();

        var data = new DataObject();
        data.SetImage(solid);
        data.SetData("PNG", png);
        return SetAsync(data, "image");
    }

    private static async Task<bool> SetAsync(DataObject data, string what)
    {
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (ExternalException ex) when (i < Attempts - 1)
            {
                // Clipboard busy: wait a moment and try again.
                if (i == 0) Log.Info($"Clipboard busy while copying {what}: {ex.Message}");
                await Task.Delay(DelayMs);
            }
            catch (Exception ex)
            {
                Log.Warn($"Couldn't copy {what} to the clipboard: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Takes a copy of everything on the clipboard right away. The live clipboard object reads
    /// lazily, so another program grabbing the clipboard a moment later could make reading fail;
    /// a copy can't. Null if the clipboard stayed busy.
    /// </summary>
    public static async Task<IDataObject?> ReadAsync()
    {
        for (int i = 0; i < Attempts; i++)
        {
            try
            {
                var live = Clipboard.GetDataObject();
                if (live is null) return new DataObject();
                var copy = new DataObject();
                foreach (var format in live.GetFormats(autoConvert: false))
                {
                    object? value;
                    try
                    {
                        value = live.GetData(format, autoConvert: false);
                    }
                    catch (Exception ex) when (!IsBusy(ex))
                    {
                        continue; // a format this program can't read; the others are still fine
                    }
                    if (value is not null) copy.SetData(format, value, autoConvert: false);
                }
                return copy;
            }
            catch (Exception ex) when (IsBusy(ex) && i < Attempts - 1)
            {
                await Task.Delay(DelayMs);
            }
            catch (Exception ex) when (IsBusy(ex))
            {
                Log.Warn($"Clipboard stayed busy: {ex.Message}");
                return null;
            }
        }
        return null;
    }

    /// <summary>True for "another program has the clipboard open right now".</summary>
    private static bool IsBusy(Exception ex) =>
        ex is ExternalException && (ex is not COMException com || com.HResult == unchecked((int)0x800401D0));

    /// <summary>Pulls a picture out of whatever is on the clipboard, or null if there isn't one.</summary>
    public static PixelImage? GetImage(IDataObject data)
    {
        // PNG first: screenshots from Snipping Tool and images copied from browsers keep their exact colors this way.
        try
        {
            if (data.GetDataPresent("PNG") && data.GetData("PNG") is Stream stream)
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return ImageLoader.ToPixels(decoder.Frames[0]);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard PNG couldn't be read, trying the plain bitmap: {ex.Message}");
        }

        try
        {
            if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
            {
                var pixels = ImageLoader.ToPixels(bitmap);
                pixels.FixAllTransparent();
                return pixels;
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Clipboard bitmap couldn't be read: {ex.Message}");
        }
        return null;
    }
}
