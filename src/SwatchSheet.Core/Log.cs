namespace SwatchSheet.Core;

/// <summary>
/// Simple log file in the "logs" folder next to the program. Logging must never crash the app,
/// so every write is wrapped.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1_000_000;

    public static string Folder => AppPaths.LogFolder;
    public static string FilePath => Path.Combine(Folder, "swatch-sheet.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    /// <summary>Everything worth pasting into a bug report.</summary>
    public static string Describe(string what, Exception ex) =>
        $"{what}{Environment.NewLine}{Environment.NewLine}" +
        $"{AppInfo.Name} {AppInfo.Version}{Environment.NewLine}" +
        $"Windows {Environment.OSVersion.Version}{Environment.NewLine}" +
        $"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
        $"Log file: {FilePath}{Environment.NewLine}{Environment.NewLine}" +
        ex;

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    // Keep one old log around instead of growing forever.
                    File.Copy(FilePath, FilePath + ".old", overwrite: true);
                    File.Delete(FilePath);
                }
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
            }
            catch
            {
                // Nowhere to write; carry on without a log rather than crash.
            }
        }
    }
}
