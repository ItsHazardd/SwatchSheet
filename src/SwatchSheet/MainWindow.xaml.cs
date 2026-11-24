using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using SwatchSheet.Core;

namespace SwatchSheet;

public partial class MainWindow : Window
{
    // Magnifier: 11 x 11 pixels, each drawn 14 screen pixels wide.
    private const int LoupeCells = 11;
    private const int CellPx = 14;
    private const int LoupeSize = LoupeCells * CellPx;
    private const double MinZoom = 0.02;
    private const double MaxZoom = 64;
    private const int MaxPreviewLines = 80;

    private readonly ObservableCollection<ImageEntry> _images = new();
    private readonly ObservableCollection<SwatchItem> _swatches = new();
    private readonly UndoHistory<SheetState> _history = new(200);
    private readonly DispatcherTimer _saveTimer;
    private readonly string _sheetPath;
    private string _palettesFolder = AppPaths.PalettesFolder;
    private bool _restored;
    private bool _saveBlocked;

    // The image being shown.
    private ImageEntry? _current;
    private PixelImage? _pixels;
    private int _showTicket;
    private readonly List<(ImageEntry Entry, PixelImage Pixels)> _cache = new(); // last few opened images
    private int _pasteCount;

    // Zoom and pan.
    private double _zoom = 1, _offsetX, _offsetY;
    private bool _autoFit = true;
    private bool _leftDown, _rightDown, _panning;
    private Point _pressPoint;
    private double _panStartX, _panStartY;

    private CopyFormat _copyFormat = CopyFormat.HexList;

    private readonly WriteableBitmap _loupeBitmap = new(LoupeSize, LoupeSize, 96, 96, PixelFormats.Bgra32, null);
    private readonly byte[] _loupeBuffer = new byte[LoupeSize * LoupeSize * 4];

    // Dragging swatches to reorder them.
    private SwatchItem? _dragItem;
    private Point _dragStart;
    private bool _dragMoved;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{AppInfo.Name} {AppInfo.Version}";
        VersionButton.Content = $"v{AppInfo.Version}";
        ImageList.ItemsSource = _images;
        SwatchList.ItemsSource = _swatches;
        LoupeImage.Source = _loupeBitmap;
        _sheetPath = Path.Combine(AppPaths.DataFolder, "sheet.json");

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _swatches.CollectionChanged += (_, _) => { RefreshSheet(); QueueSave(); };
        _images.CollectionChanged += (_, _) => { RefreshImages(); QueueSave(); };

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += async (_, _) =>
        {
            // "--self-test <folder> <image>" runs the automatic UI check instead of the normal start.
            var args = Environment.GetCommandLineArgs();
            Root.Focus();
            if (args.Length >= 4 && args[1] == "--self-test") await RunSelfTestAsync(args[2], args[3]);
            else if (args.Length >= 3 && args[1] == "--self-test-update") await RunUpdateSelfTestAsync(args[2]);
            else
            {
                await RestoreAsync();
                await StartUpdatesAsync();
            }
        };
        Closing += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        RefreshSheet();
        RefreshImages();
        ApplyTransform();
    }

    // ------------------------------------------------------------------ saving

    private async Task RestoreAsync()
    {
        var result = SheetStore.Load(_sheetPath);
        _saveBlocked = result.SaveBlocked;
        foreach (var c in result.Sheet.Colors)
            if (Rgb.TryParseHex(c.Hex, out var rgb) && _swatches.All(s => s.Value != rgb))
                _swatches.Add(new SwatchItem(rgb, c.Source));

        // Sheets from before 1.1.3 have no date yet, so they get today's.
        ApplyText(new SheetText(result.Sheet.Title, result.Sheet.Description,
            result.Sheet.Date.Length > 0 ? result.Sheet.Date : SavedSheet.Today(), result.Sheet.ShowDate));

        if (result.Problem is not null)
            MessageBox.Show(this, result.Problem, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);

        var missing = result.Sheet.Images.Where(p => !File.Exists(p)).ToList();
        foreach (var p in missing) Log.Info($"Image from last time is gone, skipping: {p}");
        await AddFilesAsync(result.Sheet.Images.Where(File.Exists), quiet: true);

        // Images dropped onto the .exe, or opened with "Open with > Swatch Sheet".
        var opened = Environment.GetCommandLineArgs().Skip(1).Where(a => !a.StartsWith("--")).ToList();
        if (opened.Count > 0) await AddFilesAsync(opened);

        _restored = true;
        Log.Info($"Restored {_swatches.Count} colors and {_images.Count} images from {_sheetPath}");
    }

    private void QueueSave()
    {
        if (!_restored) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (!_restored || _saveBlocked) return;
        try
        {
            var sheet = CurrentSheet();
            sheet.Images = _images.Where(i => i.FilePath is not null).Select(i => i.FilePath!).ToList();
            SheetStore.Save(_sheetPath, sheet);
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't save the sheet.", ex);
            ShowToast("Couldn't save your colors. Check the log in the data folder.");
        }
    }

    /// <summary>The sheet as it is right now (colors, title, date, description), without the image list.</summary>
    private SavedSheet CurrentSheet() => new()
    {
        Colors = _swatches.Select(s => new SavedSwatch { Hex = s.Hex, Source = s.Source }).ToList(),
        Title = SheetTitle,
        Description = SheetDescription,
        Date = DateBox.Text.Trim(),
        ShowDate = ShowDateBox.IsChecked == true,
    };

    // ------------------------------------------------------------------ palettes

    private void SavePalette_Click(object sender, RoutedEventArgs e) => SavePalette();

    /// <summary>Saves the sheet into the Palettes folder, named after its title. Returns true if it was saved.</summary>
    private bool SavePalette(bool askBeforeReplacing = true)
    {
        if (_swatches.Count == 0)
        {
            ShowToast("Pick some colors first, then save them as a palette.");
            return false;
        }
        if (SheetTitle.Length == 0)
        {
            ShowToast("Give the sheet a title first. That's the palette's name.");
            TitleBox.Focus();
            return false;
        }

        var path = PaletteStore.PathFor(_palettesFolder, SheetTitle);
        if (askBeforeReplacing && File.Exists(path))
        {
            var answer = MessageBox.Show(this,
                $"There's already a palette called \"{Path.GetFileNameWithoutExtension(path)}\".\n\nReplace it with this sheet?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return false;
        }

        try
        {
            PaletteStore.Save(_palettesFolder, CurrentSheet());
            Log.Info($"Saved palette {path}");
            ShowToast($"Saved \"{Path.GetFileNameWithoutExtension(path)}\" to Palettes");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't save palette {path}", ex);
            ErrorWindow.ShowError("Couldn't save the palette.", ex);
            return false;
        }
    }

    private void Palettes_Click(object sender, RoutedEventArgs e) => ShowPalettes();

    private void ShowPalettes()
    {
        PaletteList result;
        try
        {
            result = PaletteStore.List(_palettesFolder);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't list {_palettesFolder}", ex);
            ShowToast("Couldn't read the Palettes folder. Details are in the log.");
            return;
        }
        PaletteListBox.ItemsSource =result.Palettes.Select(p => new PaletteItem(p)).ToList();
        int n = result.Palettes.Count;
        PaletteCountText.Text = n == 0 ? "" : n.ToString();
        PalettesEmpty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (result.Unreadable > 0)
            ShowToast($"{result.Unreadable} file{(result.Unreadable == 1 ? "" : "s")} in Palettes couldn't be read and {(result.Unreadable == 1 ? "was" : "were")} skipped.");
        PalettesPopup.IsOpen = true;
    }

    private void PaletteList_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject) is { } d ? FindParent<ListBoxItem>(d) : null;
        if (row?.DataContext is not PaletteItem item) return;
        PalettesPopup.IsOpen = false;
        OpenPalette(item.Info.FilePath);
    }

    /// <summary>Replaces the sheet with a saved palette. Undo brings the old sheet back.</summary>
    private void OpenPalette(string path)
    {
        SavedSheet palette;
        try
        {
            palette = PaletteStore.Load(path);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open palette {path}", ex);
            ShowToast("Couldn't open that palette. The file may be damaged.");
            return;
        }

        var colors = new List<SwatchItem>();
        foreach (var c in palette.Colors)
            if (Rgb.TryParseHex(c.Hex, out var rgb) && colors.All(s => s.Value != rgb))
                colors.Add(new SwatchItem(rgb, c.Source));

        _history.Record(new SheetState(_swatches.ToList(), CurrentText()));
        ReplaceAll(colors);
        ApplyText(new SheetText(palette.Title, palette.Description, palette.Date, palette.ShowDate));
        SwatchScroll.ScrollToTop();
        RefreshSheet();
        ShowToast($"Opened \"{(palette.Title.Length > 0 ? palette.Title : Path.GetFileNameWithoutExtension(path))}\". Ctrl+Z goes back.");
    }

    private void OpenPaletteFolder_Click(object sender, RoutedEventArgs e)
    {
        PalettesPopup.IsOpen = false;
        try
        {
            Directory.CreateDirectory(_palettesFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_palettesFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't open {_palettesFolder}", ex);
            ShowToast("Couldn't open the Palettes folder.");
        }
    }

    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T found) return found;
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    // ------------------------------------------------------------------ images

    private async void AddImages_Click(object sender, RoutedEventArgs e) => await BrowseAsync();

    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add images",
            Filter = ImageLoader.DialogFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true) await AddFilesAsync(dialog.FileNames);
    }

    /// <summary>Opens image files (folders are opened one level deep). The first new one is shown.</summary>
    private async Task AddFilesAsync(IEnumerable<string> paths, bool quiet = false)
    {
        var files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(path).Where(ImageLoader.LooksLikeImage).OrderBy(f => f).Take(200));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Couldn't list {path}: {ex.Message}");
                }
            }
            else if (File.Exists(path) && ImageLoader.LooksLikeImage(path))
            {
                files.Add(path);
            }
        }
        files = files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            if (!quiet) ShowToast("Those don't look like image files.");
            return;
        }

        ImageEntry? firstNew = null;
        int failed = 0;
        foreach (var file in files)
        {
            var already = _images.FirstOrDefault(i => string.Equals(i.FilePath, file, StringComparison.OrdinalIgnoreCase));
            if (already is not null)
            {
                firstNew ??= already;
                continue;
            }
            try
            {
                var loaded = await Task.Run(() => ImageLoader.LoadFile(file));
                var entry = new ImageEntry
                {
                    Name = Path.GetFileName(file),
                    FilePath = file,
                    Thumb = loaded.Thumb,
                    SizeText = $"{loaded.Pixels.Width} × {loaded.Pixels.Height}",
                };
                _images.Add(entry);
                Remember(entry, loaded.Pixels);
                firstNew ??= entry;
            }
            catch (Exception ex)
            {
                failed++;
                Log.Error($"Couldn't open {file}", ex);
            }
        }

        if (firstNew is not null) ImageList.SelectedItem = firstNew;
        if (failed > 0)
            ShowToast(failed == 1 && files.Count == 1
                ? "Couldn't open that image. It may be damaged or an unsupported type."
                : $"Couldn't open {failed} of {files.Count} images. Details are in the log.");
    }

    private void AddPasted(PixelImage pixels)
    {
        var entry = new ImageEntry
        {
            Name = $"Pasted image {++_pasteCount}",
            Thumb = ImageLoader.MakeThumb(pixels),
            SizeText = $"{pixels.Width} × {pixels.Height}  ·  pasted",
            Kept = pixels,
        };
        _images.Add(entry);
        ImageList.SelectedItem = entry;
    }

    private void Remember(ImageEntry entry, PixelImage pixels)
    {
        if (entry.Kept is not null) return;
        _cache.RemoveAll(c => c.Entry == entry);
        _cache.Insert(0, (entry, pixels));
        if (_cache.Count > 2) _cache.RemoveAt(_cache.Count - 1);
    }

    private async void ImageList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ImageList.SelectedItem is ImageEntry entry && entry != _current) await ShowImageAsync(entry);
    }

    private async Task ShowImageAsync(ImageEntry entry)
    {
        int ticket = ++_showTicket;
        var pixels = entry.Kept ?? _cache.FirstOrDefault(c => c.Entry == entry).Pixels;
        if (pixels is null)
        {
            StatusText.Text = $"Opening {entry.Name}...";
            try
            {
                pixels = (await Task.Run(() => ImageLoader.LoadFile(entry.FilePath!))).Pixels;
            }
            catch (Exception ex)
            {
                Log.Error($"Couldn't reopen {entry.FilePath}", ex);
                ShowToast("Couldn't open that image. Was it moved or deleted?");
                StatusText.Text = "";
                return;
            }
        }
        if (ticket != _showTicket) return; // another image was picked while this one loaded

        Remember(entry, pixels);
        _current = entry;
        _pixels = pixels;
        Picture.Source = ImageLoader.ToBitmap(pixels);
        Picture.Width = pixels.Width;
        Picture.Height = pixels.Height;
        Fit();
        RefreshImages();
        StatusText.Text = $"{entry.Name}  ·  {pixels.Width} × {pixels.Height}";
    }

    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ImageEntry entry) return;
        int index = _images.IndexOf(entry);
        _cache.RemoveAll(c => c.Entry == entry);
        _images.Remove(entry);
        if (entry == _current)
        {
            _current = null;
            _pixels = null;
            Picture.Source = null;
            HideLoupe();
            StatusText.Text = "";
            if (_images.Count > 0) ImageList.SelectedItem = _images[Math.Min(index, _images.Count - 1)];
            RefreshImages();
        }
        e.Handled = true;
    }

    private void RefreshImages()
    {
        ImagesEmpty.Visibility = _images.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ImageCountText.Text = _images.Count == 0 ? "" : _images.Count.ToString();
        EmptyHint.Visibility = _pixels is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ zoom and pan

    private void ApplyTransform()
    {
        Picture.RenderTransform = new MatrixTransform(_zoom, 0, 0, _zoom, _offsetX, _offsetY);
        // Sharp square pixels when zoomed in, smooth when zoomed out.
        RenderOptions.SetBitmapScalingMode(Picture, _zoom >= 1.5 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        ZoomText.Text = _pixels is null ? "" : $"{_zoom * 100:0}%";
    }

    private void Fit()
    {
        _autoFit = true;
        if (_pixels is null) return;
        double w = Math.Max(1, Viewer.ActualWidth - 48), h = Math.Max(1, Viewer.ActualHeight - 48);
        _zoom = Math.Clamp(Math.Min(w / _pixels.Width, h / _pixels.Height), MinZoom, MaxZoom);
        _offsetX = (Viewer.ActualWidth - _pixels.Width * _zoom) / 2;
        _offsetY = (Viewer.ActualHeight - _pixels.Height * _zoom) / 2;
        ApplyTransform();
    }

    private void ZoomAt(Point p, double newZoom)
    {
        newZoom = Math.Clamp(newZoom, MinZoom, MaxZoom);
        _offsetX = p.X - (p.X - _offsetX) * newZoom / _zoom;
        _offsetY = p.Y - (p.Y - _offsetY) * newZoom / _zoom;
        _zoom = newZoom;
        _autoFit = false;
        ApplyTransform();
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Fit();

    private void ActualSize_Click(object sender, RoutedEventArgs e) => ActualSize();

    private void ActualSize()
    {
        if (_pixels is null) return;
        ZoomAt(new Point(Viewer.ActualWidth / 2, Viewer.ActualHeight / 2), 1);
    }

    private void Viewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_autoFit) Fit();
    }

    private void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pixels is null) return;
        ZoomAt(e.GetPosition(Viewer), _zoom * (e.Delta > 0 ? 1.25 : 1 / 1.25));
        UpdateHover(e);
    }

    private void Viewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_pixels is null) return;
        _leftDown = true;
        StartPress(e);
    }

    private void Viewer_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_pixels is null) return;
        _rightDown = true;
        StartPress(e);
    }

    private void StartPress(MouseEventArgs e)
    {
        Root.Focus(); // leave the title/description boxes
        _panning = false;
        _pressPoint = e.GetPosition(Viewer);
        _panStartX = _offsetX;
        _panStartY = _offsetY;
        Viewer.CaptureMouse();
    }

    private void Viewer_MouseMove(object sender, MouseEventArgs e)
    {
        if ((_leftDown || _rightDown) && _pixels is not null)
        {
            var p = e.GetPosition(Viewer);
            // Small wobbles during a click still count as a click; a real drag moves the image.
            if (!_panning && (p - _pressPoint).Length > 4) _panning = true;
            if (_panning)
            {
                _offsetX = _panStartX + (p.X - _pressPoint.X);
                _offsetY = _panStartY + (p.Y - _pressPoint.Y);
                _autoFit = false;
                ApplyTransform();
                HideLoupe();
                Viewer.Cursor = Cursors.SizeAll;
                return;
            }
        }
        UpdateHover(e);
    }

    private void Viewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftDown) return;
        bool wasPan = _panning;
        EndPress();
        if (!wasPan) PickAt(e);
        UpdateHover(e);
    }

    private void Viewer_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_rightDown) return;
        EndPress();
        UpdateHover(e);
    }

    private void EndPress()
    {
        _leftDown = _rightDown = _panning = false;
        Viewer.ReleaseMouseCapture();
    }

    private void Viewer_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_leftDown && !_rightDown) HideLoupe();
    }

    // ------------------------------------------------------------------ picking and the magnifier

    private bool TryPixelUnderMouse(MouseEventArgs e, out int x, out int y)
    {
        var pos = e.GetPosition(Picture);
        x = (int)Math.Floor(pos.X);
        y = (int)Math.Floor(pos.Y);
        return _pixels is not null && _pixels.Contains(x, y);
    }

    private void PickAt(MouseEventArgs e)
    {
        if (TryPixelUnderMouse(e, out int x, out int y)) PickPixel(x, y);
    }

    private void PickPixel(int x, int y)
    {
        var color = _pixels!.Sample(x, y);
        if (color is null)
        {
            ShowToast("That spot is see-through, so it has no color.");
            return;
        }
        AddColor(color.Value, _current?.Name);
    }

    private void UpdateHover(MouseEventArgs e)
    {
        if (_pixels is null || !TryPixelUnderMouse(e, out int x, out int y))
        {
            HideLoupe();
            if (_current is not null && _pixels is not null)
                StatusText.Text = $"{_current.Name}  ·  {_pixels.Width} × {_pixels.Height}";
            return;
        }
        Viewer.Cursor = Cursors.Cross;
        ShowHover(x, y, e.GetPosition(Viewer));
    }

    /// <summary>Magnifier and status text for image pixel (x, y), with the cursor at p in the viewer.</summary>
    private void ShowHover(int x, int y, Point p)
    {
        if (_pixels is null) return;
        DrawLoupe(x, y);
        var color = _pixels.Sample(x, y);
        if (color is Rgb c)
        {
            LoupeSwatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            LoupeHex.Text = c.Hex;
            LoupeRgb.Text = $"{c.R}, {c.G}, {c.B}";
            StatusText.Text = $"x {x}, y {y}  ·  {c.Hex}  ·  {c.RgbText}";
        }
        else
        {
            LoupeSwatch.Background = Brushes.Transparent;
            LoupeHex.Text = "see-through";
            LoupeRgb.Text = "";
            StatusText.Text = $"x {x}, y {y}  ·  see-through";
        }

        // Keep the magnifier next to the cursor, flipping sides near the edges.
        Loupe.Visibility = Visibility.Visible;
        double lw = Loupe.ActualWidth > 0 ? Loupe.ActualWidth : 168;
        double lh = Loupe.ActualHeight > 0 ? Loupe.ActualHeight : 196;
        double left = p.X + 24, top = p.Y + 24;
        if (left + lw > Viewer.ActualWidth - 8) left = p.X - 24 - lw;
        if (top + lh > Viewer.ActualHeight - 8) top = p.Y - 24 - lh;
        Canvas.SetLeft(Loupe, Math.Max(8, left));
        Canvas.SetTop(Loupe, Math.Max(8, top));
    }

    private void HideLoupe()
    {
        Loupe.Visibility = Visibility.Collapsed;
        Viewer.Cursor = null;
    }

    /// <summary>Draws the pixels around (cx, cy) as big squares, with a box around what will be sampled.</summary>
    private void DrawLoupe(int cx, int cy)
    {
        var px = _pixels!;
        int half = LoupeCells / 2;
        var buf = _loupeBuffer;

        for (int cell = 0; cell < LoupeCells * LoupeCells; cell++)
        {
            int col = cell % LoupeCells, row = cell / LoupeCells;
            int sx = cx + col - half, sy = cy + row - half;
            byte r, g, b;
            if (px.TryGetPixel(sx, sy, out var pr, out var pg, out var pb, out var pa))
            {
                // Blend see-through pixels over a grey checkerboard so they read as see-through.
                byte check = (byte)(((sx + sy) & 1) == 0 ? 70 : 50);
                r = Mix(pr, check, pa); g = Mix(pg, check, pa); b = Mix(pb, check, pa);
            }
            else
            {
                r = g = b = 18; // outside the image
            }

            for (int yy = 0; yy < CellPx; yy++)
            {
                int rowStart = ((row * CellPx + yy) * LoupeSize + col * CellPx) * 4;
                for (int xx = 0; xx < CellPx; xx++)
                {
                    // Faint grid line on the right and bottom edge of each square.
                    bool grid = xx == CellPx - 1 || yy == CellPx - 1;
                    int i = rowStart + xx * 4;
                    buf[i] = grid ? (byte)(b * 0.8) : b;
                    buf[i + 1] = grid ? (byte)(g * 0.8) : g;
                    buf[i + 2] = grid ? (byte)(r * 0.8) : r;
                    buf[i + 3] = 255;
                }
            }
        }

        // Box around the pixel that will be picked: white with a dark outline so it shows on any color.
        int first = half * CellPx - 1;
        int size = CellPx + 1;
        DrawBox(first - 1, size + 2, 0, 0, 0, 1);
        DrawBox(first, size, 255, 255, 255, 2);

        _loupeBitmap.WritePixels(new Int32Rect(0, 0, LoupeSize, LoupeSize), buf, LoupeSize * 4, 0);
    }

    private static byte Mix(byte color, byte under, byte alpha) =>
        (byte)((color * alpha + under * (255 - alpha)) / 255);

    private void DrawBox(int start, int size, byte r, byte g, byte b, int thickness)
    {
        for (int t = 0; t < thickness; t++)
        {
            int a = start + t, z = start + size - 1 - t;
            for (int k = a; k <= z; k++)
            {
                SetLoupePixel(k, a, r, g, b);
                SetLoupePixel(k, z, r, g, b);
                SetLoupePixel(a, k, r, g, b);
                SetLoupePixel(z, k, r, g, b);
            }
        }
    }

    private void SetLoupePixel(int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= LoupeSize || y >= LoupeSize) return;
        int i = (y * LoupeSize + x) * 4;
        _loupeBuffer[i] = b;
        _loupeBuffer[i + 1] = g;
        _loupeBuffer[i + 2] = r;
        _loupeBuffer[i + 3] = 255;
    }

    // ------------------------------------------------------------------ the sheet

    /// <summary>Undo step for a color-only change (the title and other text are left alone).</summary>
    private SheetState Snapshot() => new(_swatches.ToList(), null);

    private SheetText CurrentText() =>
        new(TitleBox.Text, DescriptionBox.Text, DateBox.Text, ShowDateBox.IsChecked == true);

    private void ApplyText(SheetText text)
    {
        TitleBox.Text = text.Title;
        DescriptionBox.Text = text.Description;
        DateBox.Text = text.Date;
        ShowDateBox.IsChecked = text.ShowDate;
        RefreshDateLook();
    }

    private void ApplyState(SheetState state)
    {
        ReplaceAll(state.Swatches);
        if (state.Text is not null) ApplyText(state.Text);
    }

    private void ReplaceAll(IEnumerable<SwatchItem> items)
    {
        var list = items.ToList();
        _swatches.Clear();
        foreach (var s in list) _swatches.Add(s);
    }

    private void AddColor(Rgb color, string? source)
    {
        if (_swatches.Any(s => s.Value == color))
        {
            ShowToast($"{color.Hex} is already in the sheet", color);
            return;
        }
        _history.Record(Snapshot());
        _swatches.Add(new SwatchItem(color, source));
        ShowToast($"Added {color.Hex}", color);
        Dispatcher.BeginInvoke(SwatchScroll.ScrollToEnd, DispatcherPriority.Background);
    }

    private void Rainbow_Click(object sender, RoutedEventArgs e) => RainbowSortSheet();

    private void RainbowSortSheet()
    {
        if (_swatches.Count < 2) return;
        var sorted = RainbowSort.Sort(_swatches, s => s.Value);
        if (sorted.SequenceEqual(_swatches))
        {
            ShowToast("Already in rainbow order");
            return;
        }
        _history.Record(Snapshot());
        ReplaceAll(sorted);
        SwatchScroll.ScrollToTop();
        ShowToast("Sorted like a rainbow");
    }

    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();
    private void Redo_Click(object sender, RoutedEventArgs e) => Redo();

    // Steps that opened a palette also remember the text, so the current text is kept
    // alongside them (that's what lets Redo put the palette's title back).
    private void Undo()
    {
        if (!_history.TryPeekUndo(out var target)) { ShowToast("Nothing to undo"); return; }
        var current = new SheetState(_swatches.ToList(), target.Text is null ? null : CurrentText());
        _history.TryUndo(current, out var previous);
        ApplyState(previous!);
        RefreshSheet();
        ShowToast("Undone");
    }

    private void Redo()
    {
        if (!_history.TryPeekRedo(out var target)) { ShowToast("Nothing to redo"); return; }
        var current = new SheetState(_swatches.ToList(), target.Text is null ? null : CurrentText());
        _history.TryRedo(current, out var next);
        ApplyState(next!);
        RefreshSheet();
        ShowToast("Redone");
    }

    private void Average_Click(object sender, RoutedEventArgs e) => AddAverage();

    /// <summary>Mixes every color on the sheet into one and adds it at the end.</summary>
    private void AddAverage()
    {
        if (_swatches.Count < 2)
        {
            ShowToast("Pick at least two colors to average.");
            return;
        }
        var average = Rgb.Average(_swatches.Select(s => s.Value))!.Value;
        if (_swatches.Any(s => s.Value == average))
        {
            ShowToast($"The average, {average.Hex}, is already on the sheet", average);
            return;
        }
        _history.Record(Snapshot());
        _swatches.Add(new SwatchItem(average, $"average of {_swatches.Count} colors"));
        ShowToast($"Added the average, {average.Hex}", average);
        Dispatcher.BeginInvoke(SwatchScroll.ScrollToEnd, DispatcherPriority.Background);
    }

    // The date line under the title.

    private string SheetDate => ShowDateBox.IsChecked == true ? DateBox.Text.Trim() : "";

    private void Today_Click(object sender, RoutedEventArgs e)
    {
        DateBox.Text = SavedSheet.Today();
        ShowDateBox.IsChecked = true;
    }

    private void ShowDate_Changed(object sender, RoutedEventArgs e)
    {
        RefreshDateLook();
        if (!IsLoaded) return;
        RefreshPreview();
        QueueSave();
    }

    /// <summary>A hidden date is greyed out so it's clear it won't be copied.</summary>
    private void RefreshDateLook()
    {
        if (DateBox is null) return;
        DateBox.Opacity = ShowDateBox.IsChecked == true ? 1 : 0.45;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_swatches.Count == 0) return;
        var answer = MessageBox.Show(this,
            $"Remove all {_swatches.Count} colors from the sheet?\n\nYou can bring them back with Undo (Ctrl+Z).",
            AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        _history.Record(Snapshot());
        _swatches.Clear();
        ShowToast("Sheet cleared. Ctrl+Z brings it back.");
    }

    private void RemoveSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SwatchItem item) return;
        _history.Record(Snapshot());
        _swatches.Remove(item);
        ShowToast($"Removed {item.Hex}", item.Value);
        e.Handled = true;
    }

    private void RefreshSheet()
    {
        int n = _swatches.Count;
        CountText.Text = n == 1 ? "1 color" : $"{n} colors";
        SheetEmpty.Visibility = n == 0 ? Visibility.Visible : Visibility.Collapsed;
        RainbowButton.IsEnabled = n > 1;
        AverageButton.IsEnabled = n > 1;
        AverageChip.Background = n > 1 && Rgb.Average(_swatches.Select(s => s.Value)) is Rgb avg
            ? new SolidColorBrush(Color.FromRgb(avg.R, avg.G, avg.B))
            : (Brush)FindResource("MutedBrush");
        SavePaletteButton.IsEnabled = n > 0;
        ClearButton.IsEnabled = n > 0;
        CopyButton.IsEnabled = n > 0;
        CopyImageButton.IsEnabled = n > 0;
        UndoButton.IsEnabled = _history.CanUndo;
        RedoButton.IsEnabled = _history.CanRedo;
        RefreshPreview();
    }

    // Swatch clicks and drag-to-reorder.

    private void Swatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Root.Focus();
        _dragItem = (sender as FrameworkElement)?.Tag as SwatchItem;
        _dragStart = e.GetPosition(this);
        _dragMoved = false;
    }

    private void Swatch_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed) return;
        if ((e.GetPosition(this) - _dragStart).Length < 6) return;
        _dragMoved = true;
        var item = _dragItem;
        _dragItem = null;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(SwatchItem), item), DragDropEffects.Move);
    }

    private async void Swatch_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = (sender as FrameworkElement)?.Tag as SwatchItem;
        bool click = item is not null && item == _dragItem && !_dragMoved;
        _dragItem = null;
        if (!click) return;
        bool ok = await ClipboardTools.SetTextAsync(item!.Hex);
        ShowToast(ok ? $"Copied {item.Hex}" : "Couldn't copy. Another program is using the clipboard.", item.Value);
    }

    private void Swatch_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(SwatchItem))) return;
        e.Effects = DragDropEffects.Move;
        if (sender is Border card) card.BorderBrush = (Brush)FindResource("AccentBrush");
        e.Handled = true;
    }

    private void Swatch_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border card) card.BorderBrush = Brushes.Transparent;
    }

    private void Swatch_Drop(object sender, DragEventArgs e)
    {
        if (sender is Border card) card.BorderBrush = Brushes.Transparent;
        if (e.Data.GetData(typeof(SwatchItem)) is not SwatchItem moving) return;
        if ((sender as FrameworkElement)?.Tag is not SwatchItem target || target == moving) return;
        int from = _swatches.IndexOf(moving), to = _swatches.IndexOf(target);
        if (from < 0 || to < 0) return;
        _history.Record(Snapshot());
        _swatches.Move(from, to);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ copy and paste

    private void Format_Checked(object sender, RoutedEventArgs e)
    {
        _copyFormat = sender == FormatCss ? CopyFormat.Css
                    : sender == FormatRgb ? CopyFormat.RgbList
                    : CopyFormat.HexList;
        if (IsLoaded) RefreshPreview();
    }

    private string SheetTitle => TitleBox.Text.Trim();
    private string SheetDescription => DescriptionBox.Text.Trim();

    private void SheetText_Changed(object sender, TextChangedEventArgs e)
    {
        RefreshPreview();
        QueueSave();
    }

    /// <summary>Enter (or Escape) finishes editing. In the description, Shift+Enter starts a new line.</summary>
    private void SheetText_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool finish = e.Key == Key.Escape ||
                      (e.Key == Key.Enter && (sender == TitleBox || Keyboard.Modifiers != ModifierKeys.Shift));
        if (!finish) return;
        e.Handled = true;
        Root.Focus();
    }

    private List<CopyLine> CopyLines() => CopyFormats.Lines(_swatches.Select(s => s.Value).ToList(), _copyFormat);

    /// <summary>Shows exactly what "Copy colors" will copy, with a chip of each color.</summary>
    private void RefreshPreview()
    {
        if (PreviewList is null) return;
        var lines = CopyLines();
        var brushes = _swatches.GroupBy(s => s.Value).ToDictionary(g => g.Key, g => (Brush)g.First().Brush);
        // Title and description go on top, the way OneNote and Word will show them.
        var header = new List<PreviewLine>();
        if (lines.Count > 0 && SheetTitle.Length > 0)
            header.Add(new PreviewLine { Segments = new[] { new PreviewSegment { Text = SheetTitle, Bold = true } } });
        if (lines.Count > 0 && SheetDate.Length > 0)
            header.Add(new PreviewLine { Segments = new[] { new PreviewSegment { Text = SheetDate, Muted = true } } });
        if (lines.Count > 0 && SheetDescription.Length > 0)
            header.Add(new PreviewLine { Segments = new[] { new PreviewSegment { Text = SheetDescription, Muted = true } } });
        if (header.Count > 0)
            header.Add(new PreviewLine { Segments = new[] { new PreviewSegment { Text = " " } } });

        var shown = header.Concat(lines.Take(MaxPreviewLines).Select(l => new PreviewLine
        {
            Segments = l.Segments.Select(s => new PreviewSegment
            {
                Text = s.Text,
                Chip = s.Swatch is Rgb c && brushes.TryGetValue(c, out var brush) ? brush : null,
            }).ToList(),
        })).ToList();
        if (lines.Count > MaxPreviewLines)
            shown.Add(new PreviewLine { Segments = new[] { new PreviewSegment { Text = $"...and {lines.Count - MaxPreviewLines} more lines" } } });
        PreviewList.ItemsSource = shown;
        PreviewEmpty.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Copy_Click(object sender, RoutedEventArgs e) => await CopyColorsAsync();

    private async Task CopyColorsAsync()
    {
        if (_swatches.Count == 0)
        {
            ShowToast("No colors to copy yet. Click on an image to pick some.");
            return;
        }
        bool ok = await ClipboardTools.SetRichAsync(CopyLines(), SheetTitle, SheetDescription, SheetDate);
        ShowToast(ok
            ? $"Copied {_swatches.Count} color{(_swatches.Count == 1 ? "" : "s")} as {CopyFormats.Label(_copyFormat)}"
            : "Couldn't copy. Another program is using the clipboard, try again.");
    }

    private async void CopyImage_Click(object sender, RoutedEventArgs e) => await CopyImageAsync();

    private async Task CopyImageAsync()
    {
        if (_swatches.Count == 0)
        {
            ShowToast("No colors to copy yet. Click on an image to pick some.");
            return;
        }
        var image = SheetImage.Render(_swatches.Select(s => s.Value).ToList(), SheetTitle, SheetDescription, SheetDate);
        bool ok = await ClipboardTools.SetImageAsync(image);
        ShowToast(ok ? "Copied the sheet as a picture. Paste it anywhere." : "Couldn't copy. Another program is using the clipboard, try again.");
    }

    private async void Paste_Click(object sender, RoutedEventArgs e) => await PasteAsync();

    /// <summary>Paste an image (screenshot, copied picture, copied files) or text containing color codes.</summary>
    private async Task PasteAsync()
    {
        var data = await ClipboardTools.ReadAsync();
        if (data is null)
        {
            ShowToast("Couldn't read the clipboard. Try again in a moment.");
            return;
        }

        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await AddFilesAsync(files);
            return;
        }

        var image = ClipboardTools.GetImage(data);
        if (image is not null)
        {
            AddPasted(image);
            return;
        }

        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text)
        {
            var colors = ColorText.FindColors(text).Distinct().Where(c => _swatches.All(s => s.Value != c)).ToList();
            if (colors.Count > 0)
            {
                _history.Record(Snapshot());
                foreach (var c in colors) _swatches.Add(new SwatchItem(c, "pasted"));
                ShowToast(colors.Count == 1 ? $"Added {colors[0].Hex}" : $"Added {colors.Count} colors from the clipboard", colors[0]);
                return;
            }
            if (ColorText.FindColors(text).Count > 0)
            {
                ShowToast("Those colors are already in the sheet");
                return;
            }
        }

        ShowToast("Nothing to paste. Copy an image, a screenshot, or some #hex codes first.");
    }

    // ------------------------------------------------------------------ drag and drop files, keyboard

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) await AddFilesAsync(files);
    }

    /// <summary>
    /// Clicking anywhere outside the title, date or description boxes (the image area, the image
    /// list, empty parts of the sheet...) stops typing, so the shortcut keys work again.
    /// </summary>
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox) return;
        if (FindParent<TextBox>(e.OriginalSource as DependencyObject) is not null) return;
        Root.Focus();
    }

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = mods == ModifierKeys.Control;

        // These work even while typing, since text boxes have no use for them.
        if (ctrl && e.Key == Key.O) { e.Handled = true; Root.Focus(); await BrowseAsync(); return; }
        if (ctrl && e.Key == Key.S) { e.Handled = true; Root.Focus(); SavePalette(); return; }

        // While typing a title or description, other keys belong to the text box (Ctrl+Z undoes typing, R types an R...).
        if (Keyboard.FocusedElement is TextBox) return;

        bool ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);
        bool none = mods == ModifierKeys.None;

        switch (e.Key)
        {
            case Key.V when ctrl: e.Handled = true; await PasteAsync(); break;
            case Key.Z when ctrl: e.Handled = true; Undo(); break;
            case Key.Y when ctrl:
            case Key.Z when ctrlShift: e.Handled = true; Redo(); break;
            case Key.C when ctrl: e.Handled = true; await CopyColorsAsync(); break;
            case Key.C when ctrlShift: e.Handled = true; await CopyImageAsync(); break;
            case Key.R when none: e.Handled = true; RainbowSortSheet(); break;
            case Key.A when none: e.Handled = true; AddAverage(); break;
            case Key.F when none: e.Handled = true; Fit(); break;
            case Key.D1 when none:
            case Key.NumPad1 when none: e.Handled = true; ActualSize(); break;
        }
    }

    // ------------------------------------------------------------------ little pop-up messages

    private void ShowToast(string message, Rgb? color = null)
    {
        ToastText.Text = message;
        if (color is Rgb c)
        {
            ToastSwatch.Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            ToastSwatch.Visibility = Visibility.Visible;
        }
        else
        {
            ToastSwatch.Visibility = Visibility.Collapsed;
        }

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1900))));
        fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(2300))));
        Toast.BeginAnimation(OpacityProperty, fade);
    }
}
