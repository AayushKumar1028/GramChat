using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using InstaChat.Services;
using Microsoft.Web.WebView2.Core;

namespace InstaChat;

public partial class MainWindow : Window
{
    private const string AppTitle = "InstaChat";
    private const string AppDataFolderName = "InstaChat";

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataFolderName);

    private static readonly string WindowSettingsPath = Path.Combine(AppDataDir, "window.json");

    private bool _webviewReady;
    private bool _canceledByPolicy;

    public ICommand OpenInboxCommand { get; }
    public ICommand NewMessageCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand ExitCommand { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        OpenInboxCommand = new RelayCommand(() => NavigateTo(DmNavigationPolicy.InboxUrl), () => _webviewReady);
        NewMessageCommand = new RelayCommand(() => NavigateTo(DmNavigationPolicy.NewMessageUrl), () => _webviewReady);
        ReloadCommand = new RelayCommand(Reload, () => _webviewReady);
        RetryCommand = new RelayCommand(() => NavigateTo(DmNavigationPolicy.InboxUrl));
        LogoutCommand = new RelayCommand(LogoutAndClearData, () => _webviewReady);
        AboutCommand = new RelayCommand(() => new AboutWindow { Owner = this }.ShowDialog());
        ExitCommand = new RelayCommand(Close);

        Loaded += OnLoaded;
        Closing += (_, _) => SaveWindowBounds();

        RestoreWindowBounds();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // All Instagram session data (cookies etc.) stays in this folder. Chromium encrypts
            // cookies with DPAPI on Windows, so nothing sensitive is written in plain text.
            var userDataFolder = Path.Combine(AppDataDir, "WebView2");
            Directory.CreateDirectory(userDataFolder);

            // Let Instagram's call UI start the incoming-call ringtone/video without
            // requiring a prior click inside the page (Chromium's default autoplay
            // policy would otherwise block it).
            var environmentOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
            };

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: environmentOptions);

            await Web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "InstaChat could not start its embedded browser.\n\n"
                + "Please make sure the Microsoft Edge WebView2 runtime is installed.\n\n"
                + $"Details: {ex.Message}",
                AppTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

        SetupWebView(Web.CoreWebView2);
        _webviewReady = true;
        RefreshCommandStates();

        NavigateTo(DmNavigationPolicy.InboxUrl);
    }

    private void SetupWebView(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDevToolsEnabled =
            System.Diagnostics.Debugger.IsAttached
            || Environment.GetEnvironmentVariable("INSTACHAT_DEVTOOLS") == "1";
        settings.IsZoomControlEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
        settings.IsBuiltInErrorPageEnabled = true;

        core.AddScriptToExecuteOnDocumentCreatedAsync(PageHardening.Script);

        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.PermissionRequested += OnPermissionRequested;
    }

    private void NavigateTo(string url)
    {
        if (!_webviewReady)
        {
            return;
        }

        Web.CoreWebView2.Navigate(url);
    }

    private void Reload()
    {
        if (_webviewReady)
        {
            Web.CoreWebView2.Reload();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        Uri uri;
        try
        {
            uri = new Uri(e.Uri, UriKind.RelativeOrAbsolute);
        }
        catch (UriFormatException)
        {
            e.Cancel = true;
            _canceledByPolicy = true;
            return;
        }

        switch (DmNavigationPolicy.Classify(uri))
        {
            case DmNavigationPolicy.NavAction.Allow:
                return;

            case DmNavigationPolicy.NavAction.BounceToInbox:
                e.Cancel = true;
                _canceledByPolicy = true;
                NavigateTo(DmNavigationPolicy.InboxUrl);
                break;

            case DmNavigationPolicy.NavAction.Block:
            default:
                e.Cancel = true;
                _canceledByPolicy = true;
                break;
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var canceled = _canceledByPolicy;
        _canceledByPolicy = false;

        if (e.IsSuccess)
        {
            OfflineOverlay.Visibility = Visibility.Collapsed;
        }
        else if (!canceled)
        {
            OfflineDetail.Text = $"Check your internet connection and try again. ({e.WebErrorStatus})";
            OfflineOverlay.Visibility = Visibility.Visible;
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // Never open popup windows (they would escape the guard); allowed URLs are
        // redirected into the main chat view instead.
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && DmNavigationPolicy.IsAllowed(uri))
        {
            NavigateTo(e.Uri);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var message = JsonSerializer.Deserialize<HostMessage>(e.WebMessageAsJson);
            if (message?.Type == "title" && !string.IsNullOrWhiteSpace(message.Title))
            {
                Title = message.Title.StartsWith(AppTitle, StringComparison.Ordinal)
                    ? message.Title
                    : $"{message.Title}  –  {AppTitle}";
            }
        }
        catch (JsonException)
        {
            // Ignore malformed messages from the page.
        }
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        // Auto-grant everything the chat experience needs so voice/video calls work
        // out of the box (same behaviour as the Android client): desktop
        // notifications (new-message and incoming-call toasts) plus the camera and
        // microphone Instagram's WebRTC call UI uses. The page only accesses the
        // camera/mic after the user actually starts or accepts a call; everything
        // else (geolocation, sensors, ...) keeps WebView2's standard prompt.
        switch (e.PermissionKind)
        {
            case CoreWebView2PermissionKind.Notifications:
            case CoreWebView2PermissionKind.Camera:
            case CoreWebView2PermissionKind.Microphone:
                e.State = CoreWebView2PermissionState.Allow;
                e.SavesInProfile = true;
                e.Handled = true;
                break;
        }
    }

    private async void LogoutAndClearData()
    {
        var confirm = MessageBox.Show(
            this,
            "Log out and delete all locally stored Instagram data?\n\n"
            + "This removes the session cookies, cached files and site data from this computer. "
            + "You will need to log in again next time.",
            AppTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes || !_webviewReady)
        {
            return;
        }

        try
        {
            var kinds = CoreWebView2BrowsingDataKinds.Cookies
                | CoreWebView2BrowsingDataKinds.AllDomStorage
                | CoreWebView2BrowsingDataKinds.DiskCache
                | CoreWebView2BrowsingDataKinds.CacheStorage
                | CoreWebView2BrowsingDataKinds.ServiceWorkers
                | CoreWebView2BrowsingDataKinds.GeneralAutofill
                | CoreWebView2BrowsingDataKinds.PasswordAutosave;

            await Web.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds);
        }
        catch
        {
            // Clearing is best-effort; navigating to the inbox (which becomes the login
            // page once the session is gone) is always safe.
        }

        NavigateTo(DmNavigationPolicy.InboxUrl);
    }

    private void RefreshCommandStates()
    {
        foreach (var command in new[] { OpenInboxCommand, NewMessageCommand, ReloadCommand, LogoutCommand })
        {
            ((RelayCommand)command).RaiseCanExecuteChanged();
        }
    }

    private void SaveWindowBounds()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            var state = new WindowStateInfo
            {
                Width = ActualWidth,
                Height = ActualHeight,
                Maximized = WindowState == WindowState.Maximized,
            };
            File.WriteAllText(WindowSettingsPath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }

    private void RestoreWindowBounds()
    {
        try
        {
            if (!File.Exists(WindowSettingsPath))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<WindowStateInfo>(File.ReadAllText(WindowSettingsPath));
            if (state is null)
            {
                return;
            }

            if (state.Width is >= 640 and <= 10000)
            {
                Width = state.Width.Value;
            }

            if (state.Height is >= 480 and <= 10000)
            {
                Height = state.Height.Value;
            }

            if (state.Maximized)
            {
                WindowState = WindowState.Maximized;
            }
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }

    private sealed record HostMessage(string? Type, string? Title);

    private sealed class WindowStateInfo
    {
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool Maximized { get; set; }
    }
}
