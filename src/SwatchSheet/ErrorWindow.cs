using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwatchSheet.Core;

namespace SwatchSheet;

/// <summary>Error window with a "Copy error" button, so the details can be pasted straight into a chat.</summary>
public sealed class ErrorWindow : Window
{
    private static bool _showing;

    public static void ShowError(string what, Exception ex)
    {
        Log.Error(what, ex);
        if (_showing) return; // don't stack up dozens of windows if something keeps failing
        _showing = true;
        try
        {
            var window = new ErrorWindow(what, Log.Describe(what, ex));
            if (Application.Current?.MainWindow is { IsLoaded: true } owner && owner != window) window.Owner = owner;
            window.ShowDialog();
        }
        catch (Exception showEx)
        {
            Log.Error("Couldn't show the error window.", showEx);
        }
        finally
        {
            _showing = false;
        }
    }

    private ErrorWindow(string what, string details)
    {
        Title = "Something went wrong";
        Width = 640;
        Height = 420;
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = (Brush)FindResource("BgBrush");
        Foreground = (Brush)FindResource("TextBrush");
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);

        var message = new TextBlock
        {
            Text = $"{what}\n\nClick \"Copy error\" and paste it to get help. The details are also saved in the log file:\n{Log.FilePath}",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };

        var box = new TextBox
        {
            Text = details,
            IsReadOnly = true,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 11.5,
            Background = (Brush)FindResource("PanelBrush"),
            Foreground = (Brush)FindResource("TextBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            Padding = new Thickness(6),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var style = (Style)FindResource("ToolButton");
        var copy = new Button { Content = "Copy error", Style = style };
        copy.Click += async (_, _) =>
        {
            var result = await ClipboardTools.SetTextAsync(details);
            copy.Content = result ? "Copied!" : "Copy failed - try again";
        };
        var openLogs = new Button { Content = "Open log folder", Style = style };
        openLogs.Click += (_, _) => OpenFolder(Log.Folder);
        var close = new Button { Content = "Close", Style = style, Margin = new Thickness(0) };
        close.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(openLogs);
        buttons.Children.Add(close);

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(message, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(message);
        root.Children.Add(buttons);
        root.Children.Add(box);
        Content = root;
    }

    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Couldn't open {folder}: {ex.Message}");
        }
    }
}
