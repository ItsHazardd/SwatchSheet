namespace SwatchSheet.Core;

public static class AppInfo
{
    public const string Name = "Swatch Sheet";

    /// <summary>Release.Batch.Day, read from the VERSION file at build time.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var v = typeof(AppInfo).Assembly.GetName().Version;
        return v is null ? "?" : $"{v.Major}.{v.Minor}.{v.Build}";
    }
}

/// <summary>
/// Where the program keeps its files. Everything lives next to the .exe so the folder can be
/// moved to another PC or sent to someone:
///   Palettes\   saved palettes
///   data\       the current sheet, its backup and the log
/// If that folder can't be written to, it falls back to %LOCALAPPDATA%\Swatch Sheet.
/// </summary>
public static class AppPaths
{
    public static string DataFolder { get; } = Writable(Path.Combine(AppContext.BaseDirectory, "data"), "data");
    public static string LogFolder => DataFolder;
    public static string PalettesFolder { get; } = Writable(Path.Combine(AppContext.BaseDirectory, "Palettes"), "Palettes");

    private static string Writable(string preferred, string name)
    {
        try
        {
            Directory.CreateDirectory(preferred);
            var probe = Path.Combine(preferred, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return preferred;
        }
        catch
        {
            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.Name, name);
            try { Directory.CreateDirectory(fallback); } catch { /* reported when something is actually saved */ }
            return fallback;
        }
    }
}
