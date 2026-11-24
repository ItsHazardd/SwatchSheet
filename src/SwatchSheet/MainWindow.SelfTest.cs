using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>
/// Developer check: "SwatchSheet.exe --self-test out-folder stripes.png" drives the real window
/// (open image, pick, magnifier, rainbow sort, undo, both copy buttons), saves screenshots of the
/// window and the clipboard contents, writes a report, and closes. Nothing is saved to the sheet.
/// Note: it uses the clipboard, so whatever was on it gets replaced.
/// </summary>
public partial class MainWindow
{
    private async Task RunSelfTestAsync(string outFolder, string imagePath)
    {
        Directory.CreateDirectory(outFolder);
        var report = new StringBuilder();
        int failures = 0;
        void Check(bool ok, string what)
        {
            report.AppendLine($"{(ok ? "ok  " : "FAIL")}  {what}");
            if (!ok) failures++;
        }

        try
        {
            Width = 1360;
            Height = 840;
            await Settle();
            Snap(Path.Combine(outFolder, "0-empty.png"));

            await AddFilesAsync(new[] { imagePath });
            await Settle();
            Check(_pixels is not null, "image opened");
            Check(_images.Count == 1, "image listed");

            // The test image is 10 stripes, 70 px wide. Pick them in a scrambled order.
            int[] order = { 5, 2, 8, 0, 9, 3, 6, 1, 7, 4 };
            foreach (var i in order) PickPixel(i * 70 + 35, 200);
            Check(_swatches.Count == 10, $"10 colors picked (got {_swatches.Count})");
            PickPixel(35, 10);
            Check(_swatches.Count == 10, "picking the same color twice doesn't duplicate it");

            ShowHover(140, 200, new Point(420, 300)); // middle of a stripe edge
            await Settle();
            Snap(Path.Combine(outFolder, "1-picked-with-loupe.png"));
            HideLoupe();

            RainbowSortSheet();
            await Settle();
            var sorted = string.Join(" ", _swatches.Select(s => s.Hex));
            report.AppendLine($"      sorted: {sorted}");
            Check(sorted == "#E63946 #F4A261 #E9C46A #2A9D8F #264653 #6A4C93 #FF70A6 #FFFFFF #808080 #111111",
                "rainbow order: red, orange, yellow, green, teal-blue, violet, pink, then white, grey, black");
            Snap(Path.Combine(outFolder, "2-rainbow-sorted.png"));

            Undo();
            Check(_swatches[0].Hex == "#6A4C93", "undo restores the picked order");
            Redo();
            Check(_swatches[0].Hex == "#E63946", "redo sorts again");

            TitleBox.Text = "Forest palette";
            DateBox.Text = "November 24, 2026";
            DescriptionBox.Text = "Colors pulled from the stripes test image, to check that a longer description wraps nicely on the sheet picture.";
            await Settle();
            Check(PreviewList.Items.Count == 14, $"preview shows title, date, description, gap and 10 colors (got {PreviewList.Items.Count})");
            ShowDateBox.IsChecked = false;
            await Settle();
            Check(PreviewList.Items.Count == 13 && SheetDate == "", "unticking the date hides it from the preview");
            ShowDateBox.IsChecked = true;
            await Settle();

            // Clicking outside a text box stops typing, so shortcuts work again.
            TitleBox.Focus();
            await Settle();
            if (Keyboard.FocusedElement == TitleBox)
            {
                Viewer.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
                await Settle();
                Check(Keyboard.FocusedElement != TitleBox, "clicking the image area leaves the title box");
            }
            else report.AppendLine("skip  click-away check (window didn't get keyboard focus)");

            // Average: 10 stripes mixed into one new swatch, undoable.
            var expectedAverage = Rgb.Average(_swatches.Select(s => s.Value))!.Value;
            AddAverage();
            Check(_swatches.Count == 11 && _swatches[^1].Value == expectedAverage, $"average {expectedAverage.Hex} added at the end");
            AddAverage();
            Check(_swatches.Count == 11, "averaging again changes nothing (the average of a set plus its average is the same)");
            Undo();
            Check(_swatches.Count == 10 && _swatches[0].Hex == "#E63946", "undo removes the average and keeps the sort");

            // Palettes: save, change the sheet, open the palette again, undo back.
            _palettesFolder = Path.Combine(outFolder, "Palettes");
            if (Directory.Exists(_palettesFolder)) Directory.Delete(_palettesFolder, recursive: true);
            Check(SavePalette(askBeforeReplacing: false), "palette saved");
            var saved = Path.Combine(_palettesFolder, "Forest palette.json");
            Check(File.Exists(saved), "palette file is named after the title");
            var folderOfImage = Path.GetDirectoryName(Path.GetFullPath(imagePath))!;
            Check(!File.ReadAllText(saved).Contains(folderOfImage.Replace("\\", "\\\\")) && !File.ReadAllText(saved).Contains("\"images\": [\n    \""),
                "palette file has no image paths (only the image's name, for the hover tip)");

            TitleBox.Text = "Something else";
            RemoveAt(0);
            await Settle();
            ShowPalettes();
            await Settle();
            Check(PaletteListBox.Items.Count == 1, "palette shows in the list");
            PalettesPopup.IsOpen = false;
            OpenPalette(saved);
            await Settle();
            Check(_swatches.Count == 10 && SheetTitle == "Forest palette" && DateBox.Text == "November 24, 2026", "opening the palette brings back colors, title and date");
            Undo();
            Check(_swatches.Count == 9 && SheetTitle == "Something else", "undo after opening goes back to the previous sheet");
            Redo();
            Check(_swatches.Count == 10 && SheetTitle == "Forest palette", "redo opens it again");
            TitleBox.Text = "Typed after";
            RemoveAt(0);
            Undo();
            Check(SheetTitle == "Typed after", "undoing a color change never undoes typing");
            TitleBox.Text = "Forest palette";
            await Settle();

            FormatCss.IsChecked = true;
            await Settle();
            Snap(Path.Combine(outFolder, "3-css-preview.png"));
            FormatHex.IsChecked = true;

            await CopyColorsAsync();
            var data = await ClipboardTools.ReadAsync() ?? throw new Exception("clipboard stayed busy");
            var text = ReadFormat(data, DataFormats.UnicodeText) ?? "";
            File.WriteAllText(Path.Combine(outFolder, "clip-text.txt"), text);
            Check(text.StartsWith("#E63946\r\n#F4A261") && text.EndsWith("#111111"), "plain text copy is the hex list");
            Check(!text.Contains("Forest"), "plain text copy has no title (codes only)");
            var html = ReadFormat(data, DataFormats.Html);
            File.WriteAllText(Path.Combine(outFolder, "clip-html.txt"), html ?? "(none)");
            Check(html is not null && html.Contains("StartFragment:") && html.Contains("color:#E63946"), "HTML (OneNote) copy has colored blocks");
            Check(html is not null && html.Contains(">Forest palette</div>") && html.Contains("wraps nicely"), "HTML copy has the title and description");
            Check(html is not null && html.Contains(">November 24, 2026</div>"), "HTML copy has the date");
            var rtf = ReadFormat(data, DataFormats.Rtf);
            File.WriteAllText(Path.Combine(outFolder, "clip-rtf.txt"), rtf ?? "(none)");
            Check(rtf is not null && rtf.Contains(@"\red230\green57\blue70"), "RTF (Word) copy has the colors");

            await CopyImageAsync();
            var imageData = await ClipboardTools.ReadAsync();
            var image = imageData is null ? null : ClipboardTools.GetImage(imageData);
            Check(image is not null && image.Width > 300, "sheet image is on the clipboard");
            Save(SheetImage.Render(_swatches.Select(s => s.Value).ToList(), SheetTitle, SheetDescription, SheetDate), Path.Combine(outFolder, "4-sheet-image.png"));
            Save(SheetImage.Render(_swatches.Take(2).Select(s => s.Value).ToList(), SheetTitle, SheetDescription, SheetDate), Path.Combine(outFolder, "5-two-colors-with-title.png"));
            Save(SheetImage.Render(_swatches.Take(3).Select(s => s.Value).ToList()), Path.Combine(outFolder, "6-untitled.png"));

            // The "new version" bar (nothing is downloaded here).
            ShowUpdateBar(new UpdateInfo(new Version(9, 9, 9), "v9.9.9", "https://example.test/a.zip", "https://example.test/a.zip.sha256", Updater.ReleasesPage));
            await Settle();
            Check(UpdateBar.Visibility == Visibility.Visible && UpdateText.Text.Contains("9.9.9"), "update bar shows the new version");
            Snap(Path.Combine(outFolder, "8-update-bar.png"));
            UpdateBar.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine($"FAIL  crashed: {ex}");
        }

        report.AppendLine(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        File.WriteAllText(Path.Combine(outFolder, "report.txt"), report.ToString());
        Application.Current.Shutdown(failures == 0 ? 0 : 1);
    }

    /// <summary>Removes a swatch the same way the x button does (undoable).</summary>
    private void RemoveAt(int index)
    {
        _history.Record(Snapshot());
        _swatches.RemoveAt(index);
    }

    private static string? ReadFormat(IDataObject data, string format)
    {
        if (!data.GetDataPresent(format)) return null;
        return data.GetData(format) switch
        {
            string s => s,
            MemoryStream ms => Encoding.UTF8.GetString(ms.ToArray()),
            var other => other?.ToString(),
        };
    }

    private async Task Settle()
    {
        UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private void Snap(string path)
    {
        var root = (FrameworkElement)Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        Save(bitmap, path);
    }

    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
