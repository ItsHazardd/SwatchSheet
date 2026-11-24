using System.Windows;
using System.Windows.Threading;
using SwatchSheet.Core;

namespace SwatchSheet;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Info($"Started {AppInfo.Name} {AppInfo.Version}");

        // Anything that slips through shows the "Copy error" window instead of silently crashing.
        DispatcherUnhandledException += (_, ev) =>
        {
            ev.Handled = true;
            ErrorWindow.ShowError("Something went wrong.", ev.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
            Log.Error("The program crashed.", ev.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Log.Error("A background task failed.", ev.Exception);
            ev.SetObserved();
        };

        base.OnStartup(e);
    }
}
