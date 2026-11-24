using System.Globalization;
using System.Text.Json;

namespace SwatchSheet.Core;

public sealed class SavedSwatch
{
    public string Hex { get; set; } = "";
    public string? Source { get; set; }
}

/// <summary>
/// A sheet: its colors, title, date and description. The same shape is used for the current
/// sheet (data\sheet.json, which also remembers the open images) and for saved palettes.
/// </summary>
public sealed class SavedSheet
{
    public const int MaxTitleLength = 80;
    public const int MaxDescriptionLength = 500;
    public const int MaxDateLength = 40;

    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "";
    /// <summary>Free text, so people can write it however they like. Empty in saves from before 1.1.3.</summary>
    public string Date { get; set; } = "";
    public bool ShowDate { get; set; } = true;
    public string Description { get; set; } = "";
    public List<SavedSwatch> Colors { get; set; } = new();
    public List<string> Images { get; set; } = new();

    /// <summary>Today's date the way the date line shows it, e.g. "November 24, 2026".</summary>
    public static string Today() => DateTime.Today.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
}

/// <param name="Problem">A plain-English note for the user if the saved sheet couldn't be read.</param>
/// <param name="SaveBlocked">
/// True if the saved file couldn't be read OR backed up. The app must then not save over it,
/// so the user's colors aren't lost.
/// </param>
public sealed record LoadResult(SavedSheet Sheet, string? Problem, bool SaveBlocked);

/// <summary>Saves and loads data\sheet.json.</summary>
public static class SheetStore
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static LoadResult Load(string path)
    {
        if (!File.Exists(path)) return new(new SavedSheet(), null, false);
        try
        {
            return new(Read(path), null, false);
        }
        catch (Exception ex)
        {
            Log.Error($"Couldn't read {path}", ex);
            // Keep the unreadable file so nothing is lost, then start fresh.
            var kept = $"{path}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Copy(path, kept, overwrite: false);
                return new(new SavedSheet(),
                    $"Your saved colors couldn't be read, so a fresh sheet was started.\n\n" +
                    $"The old file was kept here:\n{kept}", false);
            }
            catch (Exception copyEx)
            {
                Log.Error($"Couldn't back up {path}", copyEx);
                return new(new SavedSheet(),
                    $"Your saved colors couldn't be read:\n{path}\n\n" +
                    "To keep them safe, Swatch Sheet won't save anything this time. " +
                    "Close any program that might be using that file and restart.", true);
            }
        }
    }

    /// <summary>Reads and tidies a sheet. Throws if the file can't be read. Never changes the file.</summary>
    internal static SavedSheet Read(string path)
    {
        var sheet = JsonSerializer.Deserialize<SavedSheet>(File.ReadAllText(path), Options)
                    ?? throw new InvalidDataException("The file is empty.");
        sheet.Colors ??= new();
        sheet.Images ??= new();

        int before = sheet.Colors.Count;
        sheet.Colors = sheet.Colors
            .Where(c => c is not null && Rgb.TryParseHex(c.Hex, out _))
            .Select(c =>
            {
                Rgb.TryParseHex(c.Hex, out var rgb);
                return new SavedSwatch { Hex = rgb.Hex, Source = c.Source };
            })
            .ToList();
        if (sheet.Colors.Count != before)
            Log.Warn($"Skipped {before - sheet.Colors.Count} unreadable color(s) in {path}");
        sheet.Images = sheet.Images.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        sheet.Title = Limit(sheet.Title, SavedSheet.MaxTitleLength);
        sheet.Date = Limit(sheet.Date, SavedSheet.MaxDateLength);
        sheet.Description = Limit(sheet.Description, SavedSheet.MaxDescriptionLength);
        return sheet;
    }

    /// <summary>Older saves have no title/description/date; overly long ones are cut to fit the boxes.</summary>
    private static string Limit(string? text, int max)
    {
        text ??= "";
        return text.Length <= max ? text : text[..max];
    }

    /// <summary>
    /// Writes to a temporary file first and then swaps it in, so a crash mid-save can't leave
    /// a half-written sheet. The previous version is kept as sheet.json.bak.
    /// </summary>
    public static void Save(string path, SavedSheet sheet) => Write(path, sheet, keepBackup: true);

    internal static void Write(string path, SavedSheet sheet, bool keepBackup)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(sheet, Options));
        if (keepBackup && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
        File.Move(temp, path, overwrite: true);
    }
}
