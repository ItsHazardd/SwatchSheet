using System.Text.Json;

namespace SwatchSheet.Core;

/// <summary>App settings, kept in data\settings.json (separate from the sheet).</summary>
public sealed class AppSettings
{
    /// <summary>Look for a new version on GitHub when the app opens (at most once a day).</summary>
    public bool CheckForUpdates { get; set; } = true;
    public DateTime? LastUpdateCheckUtc { get; set; }

    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(20);

    public bool DueForCheck(DateTime nowUtc) =>
        CheckForUpdates && (LastUpdateCheckUtc is not DateTime last || nowUtc - last >= CheckEvery || last > nowUtc);
}

public static class SettingsStore
{
    /// <summary>Missing or unreadable settings just mean the defaults; nothing the user made is at stake.</summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), SheetStore.Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Warn($"Settings couldn't be read, using defaults: {ex.Message}");
            return new AppSettings();
        }
    }

    public static void Save(string path, AppSettings settings)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, SheetStore.Options));
        File.Move(temp, path, overwrite: true);
    }
}
