using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using GramChat.Services;
using Microsoft.Web.WebView2.Core;

namespace GramChat;

public partial class MainWindow : Window
{
    private const string AppTitle = "GramChat";
    private const string AppDataFolderName = "GramChat";

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataFolderName);

    private static readonly string WindowSettingsPath = Path.Combine(AppDataDir, "window.json");

    private bool _webviewReady;
    private bool _canceledByPolicy;

    // Minimum gap between two session snapshots while the user stays logged in.
    private static readonly TimeSpan SessionSnapshotCooldown = TimeSpan.FromSeconds(60);
    private DateTimeOffset _lastSessionSnapshotUtc = DateTimeOffset.MinValue;
    private readonly object _sessionLock = new();

    public ICommand HomeCommand { get; }
    public ICommand OpenInboxCommand { get; }
    public ICommand NewMessageCommand { get; }
    public ICommand NotificationsCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand ExitCommand { get; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        HomeCommand = new RelayCommand(() => NavigateTo(NavigationPolicy.HomeUrl), () => _webviewReady);
        OpenInboxCommand = new RelayCommand(() => NavigateTo(NavigationPolicy.InboxUrl), () => _webviewReady);
        NewMessageCommand = new RelayCommand(() => NavigateTo(NavigationPolicy.NewMessageUrl), () => _webviewReady);
        NotificationsCommand = new RelayCommand(() => NavigateTo(NavigationPolicy.ActivityUrl), () => _webviewReady);
        ReloadCommand = new RelayCommand(Reload, () => _webviewReady);
        RetryCommand = new RelayCommand(() => NavigateTo(NavigationPolicy.HomeUrl));
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

            // Browser flags tuned for calls and for backgrounding:
            //  * autoplay-policy lets Instagram's incoming-call ringtone/video start
            //    without a prior click (Chromium's default policy would block it);
            //  * the throttling/backgrounding flags keep WebRTC running at full rate
            //    when the window is minimised, occluded or unfocused - Chromium
            //    otherwise slows timers and renderers to a crawl mid-call.
            var environmentOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = string.Join(' ', new[]
                {
                    "--autoplay-policy=no-user-gesture-required",
                    "--disable-background-timer-throttling",
                    "--disable-renderer-backgrounding",
                    "--disable-backgrounding-occluded-windows",
                }),
            };

            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: environmentOptions);

            await Web.EnsureCoreWebView2Async(environment);

            // Encrypted session database (SQLCipher + DPAPI key): opens the
            // store and, when the browser profile holds no session, restores
            // the most recent login's cookies before the first navigation so
            // the user stays signed in.
            await SecureSessionStore.InitializeAsync(AppDataDir);
            await RestoreStoredSessionAsync(Web.CoreWebView2);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "GramChat could not start its embedded browser.\n\n"
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

        // Start on the Home feed - GramChat is the full Instagram experience now,
        // with Reels and Explore the only places it refuses to go.
        NavigateTo(NavigationPolicy.HomeUrl);
    }

    private void SetupWebView(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreDevToolsEnabled =
            System.Diagnostics.Debugger.IsAttached
            || Environment.GetEnvironmentVariable("GRAMCHAT_DEVTOOLS") == "1";
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

        switch (NavigationPolicy.Classify(uri))
        {
            case NavigationPolicy.NavAction.Allow:
                return;

            case NavigationPolicy.NavAction.BounceToHome:
                e.Cancel = true;
                _canceledByPolicy = true;
                NavigateTo(NavigationPolicy.HomeUrl);
                break;

            case NavigationPolicy.NavAction.OpenExternal:
                e.Cancel = true;
                _canceledByPolicy = true;
                OpenInSystemBrowser(uri);
                break;

            case NavigationPolicy.NavAction.Block:
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

            // Once a real Instagram page is up, capture the login (session
            // cookies + profile) into the encrypted database so it survives a
            // cleared browser profile and can be restored on the next start.
            var source = Web.Source?.ToString() ?? string.Empty;
            if (source.StartsWith("https://www.instagram.com", StringComparison.OrdinalIgnoreCase))
            {
                _ = TrySnapshotSessionAsync(Web.CoreWebView2);
            }
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

        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            return;
        }

        switch (NavigationPolicy.Classify(uri))
        {
            case NavigationPolicy.NavAction.Allow:
                NavigateTo(e.Uri);
                break;

            case NavigationPolicy.NavAction.BounceToHome:
                NavigateTo(NavigationPolicy.HomeUrl);
                break;

            case NavigationPolicy.NavAction.OpenExternal:
                OpenInSystemBrowser(uri);
                break;
        }
    }

    /// <summary>
    /// Hands a link that leaves Instagram (or a mailto:/tel: link) to the system
    /// browser, so external pages never run inside the guarded view.
    /// </summary>
    private static void OpenInSystemBrowser(Uri uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Opening externally is best-effort.
            System.Diagnostics.Debug.WriteLine($"Failed to open {uri}: {ex.Message}");
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

        // The encrypted session database must not keep any login behind after
        // the user asked to log out.
        try
        {
            await SecureSessionStore.ClearAllAsync(AppDataDir);
        }
        catch
        {
            // Best-effort.
        }

        NavigateTo(NavigationPolicy.HomeUrl);
    }

    // ------------------------------------------------------------------
    // Encrypted session database: restore + snapshot
    // ------------------------------------------------------------------

    private async Task RestoreStoredSessionAsync(CoreWebView2 core)
    {
        try
        {
            // The browser profile already has a live session - nothing to do.
            var existing = await core.CookieManager.GetCookiesAsync(NavigationPolicy.HomeUrl);
            if (existing.Any(c => c.Name == "sessionid" && !string.IsNullOrEmpty(c.Value)))
            {
                return;
            }

            var stored = await SecureSessionStore.GetLatestSessionAsync(AppDataDir);
            if (stored is null || string.IsNullOrWhiteSpace(stored.SessionData))
            {
                return;
            }

            foreach (var cookie in SecureSessionStore.DeserializeCookies(stored.SessionData))
            {
                var wc = core.CookieManager.CreateCookie(cookie.Name, cookie.Value, cookie.Domain, cookie.Path);
                if (wc is null)
                {
                    continue;
                }

                wc.IsHttpOnly = cookie.HttpOnly;
                wc.IsSecure = cookie.Secure;
                // CreateCookie defaults to a session cookie; only make it
                // persistent when the original had an expiry.
                if (cookie.ExpiresUnixSeconds != 0)
                {
                    wc.Expires = DateTimeOffset.FromUnixTimeSeconds(cookie.ExpiresUnixSeconds).LocalDateTime;
                }

                try
                {
                    wc.SameSite = (CoreWebView2CookieSameSiteKind)cookie.SameSite;
                }
                catch
                {
                    // Older WebView2 runtimes may not expose SameSite; ignore.
                }

                core.CookieManager.AddOrUpdateCookie(wc);
            }
        }
        catch (Exception ex)
        {
            // Restore is best-effort: worst case the user signs in again.
            System.Diagnostics.Debug.WriteLine($"Session restore failed: {ex}");
        }
    }

    private async Task TrySnapshotSessionAsync(CoreWebView2 core)
    {
        lock (_sessionLock)
        {
            if (DateTime.UtcNow - _lastSessionSnapshotUtc < SessionSnapshotCooldown)
            {
                return;
            }

            _lastSessionSnapshotUtc = DateTime.UtcNow;
        }

        try
        {
            var cookies = await core.CookieManager.GetCookiesAsync(NavigationPolicy.HomeUrl);
            var sessionCookie = cookies.FirstOrDefault(c => c.Name == "sessionid" && !string.IsNullOrEmpty(c.Value));
            if (sessionCookie is null)
            {
                return; // not logged in (login page or logged out)
            }

            var viewer = await GetViewerProfileAsync(core);
            var record = new StoredSession(
                UserId: viewer?.UserId ?? string.Empty,
                Username: viewer?.Username ?? string.Empty,
                DisplayName: viewer?.DisplayName ?? string.Empty,
                AvatarUrl: viewer?.AvatarUrl ?? string.Empty,
                SessionData: SecureSessionStore.SerializeCookies(cookies.Select(ToCookieRecord)),
                LastLogin: DateTimeOffset.UtcNow);

            if (string.IsNullOrEmpty(record.UserId))
            {
                // Fall back to Instagram's numeric user id cookie.
                var dsUserId = cookies.FirstOrDefault(c => c.Name == "ds_user_id")?.Value ?? string.Empty;
                record = record with { UserId = dsUserId };
            }

            await SecureSessionStore.SaveSessionAsync(AppDataDir, record);
        }
        catch (Exception ex)
        {
            // Snapshot is best-effort; never let it break the chat experience.
            System.Diagnostics.Debug.WriteLine($"Session snapshot failed: {ex}");
        }
    }

    private async Task<ViewerProfile?> GetViewerProfileAsync(CoreWebView2 core)
    {
        try
        {
            var json = await core.ExecuteScriptAsync(SecureSessionStore.ViewerProfileScript);
            if (string.IsNullOrWhiteSpace(json) || json == "null")
            {
                return null;
            }

            // ExecuteScriptAsync returns the expression's JSON encoding, and the
            // script itself returns a JSON string - so the result is a JSON
            // string containing a JSON object.
            using var doc = JsonDocument.Parse(json);
            var inner = doc.RootElement.GetString();
            if (string.IsNullOrWhiteSpace(inner))
            {
                return null;
            }

            return JsonSerializer.Deserialize<ViewerProfile>(inner);
        }
        catch
        {
            return null;
        }
    }

    private static CookieRecord ToCookieRecord(CoreWebView2Cookie c) => new(
        Name: c.Name,
        Value: c.Value,
        Domain: c.Domain,
        Path: c.Path,
        HttpOnly: c.IsHttpOnly,
        Secure: c.IsSecure,
        ExpiresUnixSeconds: c.IsSession ? 0 : new DateTimeOffset(c.Expires.ToUniversalTime()).ToUnixTimeSeconds(),
        SameSite: (int)c.SameSite);

    private void RefreshCommandStates()
    {
        foreach (var command in new[] { HomeCommand, OpenInboxCommand, NewMessageCommand, NotificationsCommand, ReloadCommand, LogoutCommand })
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

    private sealed class ViewerProfile
    {
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
    }

    private sealed class WindowStateInfo
    {
        public double? Width { get; set; }
        public double? Height { get; set; }
        public bool Maximized { get; set; }
    }
}
