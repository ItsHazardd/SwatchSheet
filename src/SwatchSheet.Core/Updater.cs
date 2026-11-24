using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwatchSheet.Core;

/// <summary>A newer version found on the GitHub Releases page.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string ZipUrl, string ShaUrl, string PageUrl);

/// <summary>
/// Updates from GitHub Releases:
///  1. Ask GitHub for the newest release (sends nothing about the user).
///  2. If it's newer, download its zip and its .sha256 fingerprint file.
///  3. Refuse the download unless its fingerprint matches exactly.
///  4. Swap the program: the running exe is renamed to "SwatchSheet.exe.old" (Windows allows
///     renaming a running program), the new one takes its place, and the app restarts.
///     The .old copy is deleted once the new version has started properly.
/// User data (data\, Palettes\) is never touched.
/// </summary>
public static class Updater
{
    public const string LatestReleaseApi = "https://api.github.com/repos/ItsHazardd/SwatchSheet/releases/latest";
    public const string ReleasesPage = "https://github.com/ItsHazardd/SwatchSheet/releases";
    public const string ExeName = "SwatchSheet.exe";

    /// <summary>"v1.1.4" or "1.1.4" -> 1.1.4. Null if it isn't a version.</summary>
    public static Version? ParseVersion(string? tag)
    {
        var text = (tag ?? "").Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        if (!Version.TryParse(text, out var v)) return null;
        // Compare Release.Batch.Day only.
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static bool IsNewer(Version remote, string currentVersion) =>
        ParseVersion(currentVersion) is { } current && remote > current;

    /// <summary>
    /// Reads GitHub's "latest release" answer. Returns null if it has no usable zip and
    /// fingerprint, so a half-finished release is never offered.
    /// </summary>
    public static UpdateInfo? ParseLatestRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True) return null;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var version = ParseVersion(tag);
        if (version is null) return null;
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;

        string? zip = null, sha = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (url is null || !IsSafeUrl(url)) continue;
                if (name.EndsWith(".zip.sha256", StringComparison.OrdinalIgnoreCase)) sha ??= url;
                else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip ??= url;
            }
        }
        return zip is null || sha is null ? null : new UpdateInfo(version, tag!, zip, sha, page);
    }

    /// <summary>Downloads only over https (plain http is allowed for this PC itself, for testing).</summary>
    public static bool IsSafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        (u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback));

    /// <summary>The 64-character fingerprint from a .sha256 file ("HASH  name" or just "HASH").</summary>
    public static string? ParseSha256(string text)
    {
        var first = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        first = first.TrimStart('﻿');
        return first.Length == 64 && first.All(Uri.IsHexDigit) ? first.ToUpperInvariant() : null;
    }

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromMinutes(10);
        // GitHub requires a user agent. A plain program name: nothing about the user.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SwatchSheet/{AppInfo.Version}");
        return http;
    }

    /// <summary>Asks GitHub for the newest release. Null if there's nothing newer.</summary>
    public static async Task<UpdateInfo?> CheckAsync(HttpClient http, string currentVersion,
        string apiUrl = LatestReleaseApi, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();
        var info = ParseLatestRelease(await response.Content.ReadAsStringAsync(cancel));
        return info is not null && IsNewer(info.Version, currentVersion) ? info : null;
    }

    /// <summary>
    /// Downloads the release into workFolder, checks its fingerprint and pulls out the new exe.
    /// Throws (and leaves nothing half-installed) if anything is wrong.
    /// </summary>
    public static async Task<string> DownloadAsync(HttpClient http, UpdateInfo info, string workFolder,
        IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true);
        Directory.CreateDirectory(workFolder);

        var expected = ParseSha256(await http.GetStringAsync(info.ShaUrl, cancel))
                       ?? throw new InvalidDataException("The update's fingerprint file is missing or damaged.");

        var zipPath = Path.Combine(workFolder, "update.zip");
        using (var response = await http.GetAsync(info.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancel))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancel);
            await using var file = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0) progress?.Report((double)done / total.Value);
            }
        }

        var actual = Sha256Of(zipPath);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "The downloaded update doesn't match its fingerprint, so it wasn't installed. " +
                "It may have been damaged on the way. Try again later.");

        // Only the program file is taken from the zip, and only from the top of it.
        var newExe = Path.Combine(workFolder, ExeName + ".new");
        using (var zip = ZipFile.OpenRead(zipPath))
        {
            var entry = zip.Entries.FirstOrDefault(e => string.Equals(e.FullName, ExeName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidDataException($"The update doesn't contain {ExeName}.");
            entry.ExtractToFile(newExe, overwrite: true);
        }
        if (new FileInfo(newExe).Length < 1024)
            throw new InvalidDataException("The update's program file is too small to be real.");
        return newExe;
    }

    /// <summary>
    /// Swaps in the new exe. The current one is kept as "...exe.old" until the new version has
    /// started (see Cleanup). If anything fails, the original is put back.
    /// </summary>
    public static void Install(string currentExe, string newExe)
    {
        var old = currentExe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(currentExe, old);
        try
        {
            File.Move(newExe, currentExe);
        }
        catch
        {
            if (!File.Exists(currentExe)) File.Move(old, currentExe);
            throw;
        }
    }

    /// <summary>After a successful start: removes the previous version and the download folder.</summary>
    public static void Cleanup(string currentExe, string workFolder)
    {
        try { if (File.Exists(currentExe + ".old")) File.Delete(currentExe + ".old"); }
        catch (Exception ex) { Log.Warn($"Couldn't remove the old version yet: {ex.Message}"); }
        try { if (Directory.Exists(workFolder)) Directory.Delete(workFolder, recursive: true); }
        catch (Exception ex) { Log.Warn($"Couldn't remove the update download folder: {ex.Message}"); }
    }

    /// <summary>True if the program's own folder can be written to (needed to update in place).</summary>
    public static bool CanWriteNextTo(string exePath)
    {
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(exePath)!, ".update-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
