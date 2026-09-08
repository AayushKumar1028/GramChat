using System.Threading;
using System.Windows;

namespace InstaChat;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Two instances cannot share one WebView2 user-data folder, so keep it single-instance.
        _singleInstanceMutex = new Mutex(true, @"Local\InstaChat-SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "InstaChat is already running.",
                "InstaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                $"Something went wrong: {args.Exception.Message}",
                "InstaChat",
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
