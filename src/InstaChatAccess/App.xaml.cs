using System.Threading;
using System.Windows;

namespace InstaChatAccess;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two instances cannot share one WebView2 user-data folder, so keep it single-instance.
        _singleInstanceMutex = new Mutex(true, @"Local\InstaChatAccess-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "InstaChat Access is already running.",
                "InstaChat Access",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Something went wrong: {args.Exception.Message}",
                "InstaChat Access",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
