using System.Text;

namespace SwatchSheet.Core;

/// <summary>A saved palette as shown in the Palettes list.</summary>
public sealed record PaletteInfo(string FilePath, string Title, string Date, IReadOnlyList<Rgb> Colors, DateTime Modified)
{
    public string Name => Path.GetFileNameWithoutExtension(FilePath);
}

/// <param name="Unreadable">How many files in the folder couldn't be read (they are left alone).</param>
public sealed record PaletteList(IReadOnlyList<PaletteInfo> Palettes, int Unreadable);

/// <summary>
/// Saved palettes: one small .json file per palette in the Palettes folder next to the program,
/// named after the sheet's title. Same format as data\sheet.json, minus the open images.
/// </summary>
public static class PaletteStore
{
    public const string Extension = ".json";
    private const int MaxNameLength = 60;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Turns a title into a safe file name: characters Windows doesn't allow are swapped for
    /// spaces, and the result is trimmed and kept short. Empty titles become "Untitled palette".
    /// </summary>
    public static string FileNameFor(string? title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var ch in title ?? "")
            sb.Append(invalid.Contains(ch) || char.IsControl(ch) ? ' ' : ch);
        var name = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (name.Length > MaxNameLength) name = name[..MaxNameLength];
        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0) name = "Untitled palette";
        if (ReservedNames.Contains(name)) name += " palette";
        return name + Extension;
    }

    public static string PathFor(string folder, string? title) => Path.Combine(folder, FileNameFor(title));

    /// <summary>
    /// Saves the palette (colors, title, date, description; never the image list) and returns
    /// where it went. Overwrites a palette with the same name, so ask the user first.
    /// </summary>
    public static string Save(string folder, SavedSheet sheet)
    {
        var path = PathFor(folder, sheet.Title);
        var palette = new SavedSheet
        {
            Title = sheet.Title,
            Date = sheet.Date,
            ShowDate = sheet.ShowDate,
            Description = sheet.Description,
            Colors = sheet.Colors.Select(c => new SavedSwatch { Hex = c.Hex, Source = c.Source }).ToList(),
        };
        SheetStore.Write(path, palette, keepBackup: false);
        return path;
    }

    /// <summary>Reads a palette. Throws if it can't be read. Never changes the file.</summary>
    public static SavedSheet Load(string path)
    {
        var sheet = SheetStore.Read(path);
        sheet.Images.Clear();
        return sheet;
    }

    /// <summary>Every palette in the folder, newest first. Broken files are counted and skipped.</summary>
    public static PaletteList List(string folder)
    {
        var found = new List<PaletteInfo>();
        int unreadable = 0;
        if (!Directory.Exists(folder)) return new(found, 0);

        foreach (var file in Directory.EnumerateFiles(folder, "*" + Extension))
        {
            try
            {
                var sheet = SheetStore.Read(file);
                var colors = sheet.Colors.Select(c => { Rgb.TryParseHex(c.Hex, out var rgb); return rgb; }).ToList();
                var title = sheet.Title.Trim().Length > 0 ? sheet.Title.Trim() : Path.GetFileNameWithoutExtension(file);
                found.Add(new PaletteInfo(file, title, sheet.Date.Trim(), colors, File.GetLastWriteTime(file)));
            }
            catch (Exception ex)
            {
                unreadable++;
                Log.Warn($"Skipped palette that couldn't be read: {file} ({ex.Message})");
            }
        }
        return new(found.OrderByDescending(p => p.Modified).ToList(), unreadable);
    }
}
