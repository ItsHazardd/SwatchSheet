using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>Checking GitHub for a new version, and installing it when the user says so.</summary>
public partial class MainWindow
{
    private readonly string _settingsPath = Path.Combine(AppPaths.DataFolder, "settings.json");
    private AppSettings _settings = new();
    private HttpClient? _http;
    private UpdateInfo? _update;
    private bool _updating;
    private string _updateApi = Updater.LatestReleaseApi; // the self-test points this at a fake server

    private HttpClient Http => _http ??= Updater.CreateClient();
    private static string UpdateWorkFolder => Path.Combine(AppPaths.DataFolder, "update");

    /// <summary>On a normal start: tidy up after an update, then check GitHub if it's time.</summary>
    private async Task StartUpdatesAsync()
    {
        _settings = SettingsStore.Load(_settingsPath);
        AutoUpdateBox.IsChecked = _settings.CheckForUpdates;

        if (Environment.ProcessPath is string exe) Updater.Cleanup(exe, UpdateWorkFolder);
        if (Environment.GetCommandLineArgs().Contains("--updated"))
            ShowToast($"Updated to {AppInfo.Version}. Your colors and palettes are all here.");

        if (_settings.DueForCheck(DateTime.UtcNow)) await CheckForUpdatesAsync(manual: false);
    }

    /// <summary>
    /// Developer check: "SwatchSheet.exe --self-test-update http://localhost:port/latest" runs a real
    /// update against a fake release server on this PC (download, fingerprint check, swap, restart).
    /// Only accepts an address on this PC, so it can't be pointed anywhere else.
    /// </summary>
    private async Task RunUpdateSelfTestAsync(string apiUrl)
    {
        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var u) || !u.IsLoopback)
        {
            Log.Warn("Update self-test refused: not an address on this PC.");
            Application.Current.Shutdown(2);
            return;
        }
        await RestoreAsync();
        _settings = SettingsStore.Load(_settingsPath);
        _updateApi = apiUrl;
        await CheckForUpdatesAsync(manual: true);
        if (_update is null)
        {
            Log.Warn("Update self-test: no update found.");
            Application.Current.Shutdown(3);
            return;
        }
        await InstallUpdateAsync(_update);
    }

    private void Version_Click(object sender, RoutedEventArgs e)
    {
        AboutTitle.Text = $"{AppInfo.Name} {AppInfo.Version}";
        AboutPopup.IsOpen = true;
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    private void AutoUpdate_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _settings.CheckForUpdates = AutoUpdateBox.IsChecked == true;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try { SettingsStore.Save(_settingsPath, _settings); }
        catch (Exception ex) { Log.Warn($"Couldn't save settings: {ex.Message}"); }
    }

    private void SetUpdateStatus(string text)
    {
        UpdateStatusText.Text = text;
        UpdateStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>Asks GitHub for the newest version. Quiet when automatic; says what happened when clicked.</summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updating) return;
        CheckUpdatesButton.IsEnabled = false;
        if (manual) SetUpdateStatus("Checking...");
        try
        {
            var info = await Updater.CheckAsync(Http, AppInfo.Version, _updateApi);
            _settings.LastUpdateCheckUtc = DateTime.UtcNow;
            SaveSettings();
            if (info is not null)
            {
                Log.Info($"Update available: {info.Tag}");
                ShowUpdateBar(info);
                if (manual) SetUpdateStatus($"Version {info.Version} is available.");
            }
            else if (manual)
            {
                SetUpdateStatus($"You have the newest version ({AppInfo.Version}).");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Update check failed: {ex.Message}");
            if (manual) SetUpdateStatus("Couldn't reach GitHub. Check your internet connection and try again.");
        }
        finally
        {
            CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void ShowUpdateBar(UpdateInfo info)
    {
        _update = info;
        UpdateText.Text = $"Swatch Sheet {info.Version} is available";
        UpdateNowButton.IsEnabled = NotNowButton.IsEnabled = true;
        UpdateBar.Visibility = Visibility.Visible;
    }

    private void NotNow_Click(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private void WhatsNew_Click(object sender, RoutedEventArgs e) =>
        OpenInBrowser(_update?.PageUrl ?? Updater.ReleasesPage);

    private void OpenInBrowser(string url)
    {
        // Only ever our own GitHub pages.
        if (!url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) url = Updater.ReleasesPage;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warn($"Couldn't open {url}: {ex.Message}"); }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_update is not null) await InstallUpdateAsync(_update);
    }

    /// <summary>Downloads, checks and installs the update, then restarts. Anything wrong: nothing changes.</summary>
    private async Task InstallUpdateAsync(UpdateInfo info)
    {
        var exe = Environment.ProcessPath;
        if (exe is null || !string.Equals(Path.GetFileName(exe), Updater.ExeName, StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "SwatchSheet.dll")))
        {
            // A developer build (lots of files) or a renamed exe: don't swap anything, send them to the page.
            MessageBox.Show(this, "This copy of Swatch Sheet can't update itself. Download the new version from the release page instead.",
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            OpenInBrowser(info.PageUrl);
            return;
        }
        if (!Updater.CanWriteNextTo(exe))
        {
            var answer = MessageBox.Show(this,
                "Swatch Sheet can't update itself in this folder (Windows won't let it write here).\n\n" +
                "Open the release page to download the new version instead?",
                AppInfo.Name, MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes) OpenInBrowser(info.PageUrl);
            return;
        }

        _updating = true;
        UpdateNowButton.IsEnabled = NotNowButton.IsEnabled = false;
        UpdateText.Text = $"Downloading {info.Version}...";
        try
        {
            var progress = new Progress<double>(p => UpdateText.Text = $"Downloading {info.Version}...  {p * 100:0}%");
            var newExe = await Updater.DownloadAsync(Http, info, UpdateWorkFolder, progress);
            UpdateText.Text = "Installing...";
            SaveNow(); // the sheet is safe on disk before anything is swapped
            Updater.Install(exe, newExe);
            _saveBlocked = true; // the new version takes over the sheet from here; don't save again on close
            Log.Info($"Installed {info.Tag}, restarting");
            Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Log.Error($"Update to {info.Tag} failed", ex);
            _updating = false;
            UpdateBar.Visibility = Visibility.Collapsed;
            ErrorWindow.ShowError(
                "The update couldn't be installed. Nothing was changed: you're still on " + AppInfo.Version + ". " +
                "You can try again later, or download it from the release page.", ex);
        }
    }
}
