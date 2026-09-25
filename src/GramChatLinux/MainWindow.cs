using System.IO;
using System.Text.Json;
using GObject;
using Gtk;
using GramChat.Services;
using WebKit;

using Value = GObject.Value;

namespace GramChat;

/// <summary>
/// Main window of the Linux client. Functional twin of the Windows WPF
/// <c>MainWindow</c> built on GTK4 + WebKitGTK (6.0): the same
/// Reels/Explore-free navigation policy, the same in-page hardening script, automatic camera /
/// microphone / notification permissions for voice and video calls, an offline
/// overlay, window-state persistence, and a single-instance application.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>Release label shown in the About dialog.</summary>
    public const string VersionLabel = "v1.0-Linux-beta1";

    private const string AppTitle = "GramChat";
    private const string AppDataFolderName = "GramChat";

    // Same desktop Chrome user agent as the Android client, for the best
    // Instagram web compatibility.
    private const string DesktopUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataFolderName);

    private static readonly string WindowSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppDataFolderName,
        "window.json");

    private static MainWindow? _instance;

    private readonly Application _app;
    private readonly WebView _webView;
    private readonly WebsiteDataManager _dataManager;
    private readonly Box _offlinePanel;
    private Label _offlineDetail = null!;

    private bool _loadFailed;

    // Minimum gap between two session snapshots while the user stays logged in.
    private static readonly TimeSpan SessionSnapshotCooldown = TimeSpan.FromSeconds(60);
    private DateTimeOffset _lastSessionSnapshotUtc = DateTimeOffset.MinValue;

    private MainWindow(Application app)
    {
        _app = app;

        Title = AppTitle;
        SetDefaultSize(1080, 760);
        SetSizeRequest(640, 480);

        // Before WebKitGTK's network process opens its cookie database, restore
        // the most recent login from the encrypted session store (SQLCipher +
        // 0600 key file, Chromium's Linux model). If the profile already holds a
        // live session, this is a no-op.
        RestoreStoredSessionFromEncryptedStore();

        _dataManager = CreateWebsiteDataManager();
        var webContext = WebContext.NewWithProperties(new[]
        {
            new ConstructArgument("website-data-manager", new Value(_dataManager)),
        });
        WebKitNative.WireNotificationPermission(webContext);

        _webView = CreateWebView(webContext);
        _webView.OnDecidePolicy += OnDecidePolicy;
        _webView.OnLoadChanged += OnLoadChanged;
        _webView.OnLoadFailed += OnLoadFailed;
        _webView.OnPermissionRequest += OnPermissionRequest;
        _webView.OnCreate += OnCreate;
        _webView.OnNotify += OnTitleChanged;

        RegisterActions();

        var root = Gtk.Box.New(Orientation.Vertical, 0);
        root.Append(CreateMenuBar());

        var overlay = new Gtk.Overlay();
        overlay.SetChild(_webView);
        root.Append(overlay);

        _offlinePanel = CreateOfflinePanel();
        overlay.AddOverlay(_offlinePanel);

        SetChild(root);
        InstallOfflinePanelCss();

        Application = app;
        RestoreWindowState();

        OnCloseRequest += (_, _) =>
        {
            SaveWindowState();
            SnapshotSessionOnExit();
            return false; // allow the window to close
        };
        OnDestroy += (_, _) => _app.Quit();

        // Start on the Home feed - GramChat is the full Instagram experience now,
        // with Reels and Explore the only places it refuses to go.
        LoadUri(NavigationPolicy.HomeUrl);
    }

    public static void ShowOrFocus(Application app)
    {
        if (_instance is null)
        {
            _instance = new MainWindow(app);
        }

        _instance.Present();
    }

    // ------------------------------------------------------------------
    // WebView construction
    // ------------------------------------------------------------------

    private WebsiteDataManager CreateWebsiteDataManager()
    {
        // All Instagram session data (cookies, DOM storage, cache) stays in a
        // dedicated profile under ~/.local/share/GramChat, mirroring the
        // isolated WebView2 user-data folder on Windows.
        var baseDataDir = Path.Combine(DataDir, "WebKitGTK");
        var baseCacheDir = Path.Combine(GetCacheHome(), AppDataFolderName);

        Directory.CreateDirectory(baseDataDir);
        Directory.CreateDirectory(baseCacheDir);

        return WebsiteDataManager.NewWithProperties(new[]
        {
            new ConstructArgument("base-data-directory", new Value(baseDataDir)),
            new ConstructArgument("base-cache-directory", new Value(baseCacheDir)),
        });
    }

    private WebView CreateWebView(WebContext webContext)
    {
        var webView = WebView.NewWithProperties(new[]
        {
            new ConstructArgument("web-context", new Value(webContext)),
        });

        var settings = webView.GetSettings();
        settings.UserAgent = DesktopUserAgent;
        settings.EnableMedia = true;
        settings.EnableMediaStream = true;
        settings.EnableFullscreen = true;
        // Let Instagram's call UI start the incoming-call ringtone/video without
        // a prior click (same as --autoplay-policy=no-user-gesture-required on
        // Windows and the auto-granted camera/mic on Android).
        settings.MediaPlaybackRequiresUserGesture = false;
        settings.EnableDeveloperExtras =
            System.Diagnostics.Debugger.IsAttached
            || Environment.GetEnvironmentVariable("GRAMCHAT_DEVTOOLS") == "1";

        // Second layer of the guard: the SPA route watcher and page hardening
        // script, identical to the one the Windows client injects.
        webView.UserContentManager!.AddScript(UserScript.New(
            PageHardening.Script,
            UserContentInjectedFrames.AllFrames,
            UserScriptInjectionTime.Start,
            Array.Empty<string>(),
            Array.Empty<string>()));

        return webView;
    }

    private static string GetCacheHome()
    {
        var env = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        return string.IsNullOrWhiteSpace(env)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache")
            : env;
    }

    // ------------------------------------------------------------------
    // Navigation guard (same two layers as Windows / Android)
    // ------------------------------------------------------------------

    private bool OnDecidePolicy(WebView sender, WebView.DecidePolicySignalArgs args)
    {
        var decision = args.Decision;

        switch (args.DecisionType)
        {
            case PolicyDecisionType.NavigationAction:
                if (decision is NavigationPolicyDecision nav
                    && Uri.TryCreate(nav.NavigationAction.GetRequest()?.Uri, UriKind.Absolute, out var uri))
                {
                    switch (NavigationPolicy.Classify(uri))
                    {
                        case NavigationPolicy.NavAction.Allow:
                            decision.Use();
                            break;

                        case NavigationPolicy.NavAction.BounceToHome:
                            decision.Ignore();
                            LoadUri(NavigationPolicy.HomeUrl);
                            break;

                        case NavigationPolicy.NavAction.OpenExternal:
                            decision.Ignore();
                            OpenInSystemBrowser(uri);
                            break;

                        default:
                            decision.Ignore();
                            break;
                    }
                }
                else
                {
                    decision.Ignore();
                }

                return true;

            case PolicyDecisionType.NewWindowAction:
                // Never open popup windows (they would escape the guard);
                // allowed URLs are redirected into the main view, external ones
                // are handed to the system browser.
                if (decision is NavigationPolicyDecision popup
                    && Uri.TryCreate(popup.NavigationAction.GetRequest()?.Uri, UriKind.Absolute, out var popupUri))
                {
                    switch (NavigationPolicy.Classify(popupUri))
                    {
                        case NavigationPolicy.NavAction.Allow:
                            LoadUri(popupUri.ToString());
                            break;

                        case NavigationPolicy.NavAction.BounceToHome:
                            LoadUri(NavigationPolicy.HomeUrl);
                            break;

                        case NavigationPolicy.NavAction.OpenExternal:
                            OpenInSystemBrowser(popupUri);
                            break;
                    }
                }

                decision.Ignore();
                return true;

            default:
                // Response decisions and anything else: let WebKit decide.
                return false;
        }
    }

    /// <summary>
    /// Hands a link that leaves Instagram (or a mailto:/tel: link) to the
    /// system browser, so external pages never run inside the guarded view.
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

    private void OnLoadChanged(WebView sender, WebView.LoadChangedSignalArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished && !_loadFailed)
        {
            _offlinePanel.Visible = false;

            if ((_webView.Uri ?? string.Empty).StartsWith("https://www.instagram.com", StringComparison.OrdinalIgnoreCase))
            {
                _ = TrySnapshotSessionAsync();
            }
        }
    }

    private bool OnLoadFailed(WebView sender, WebView.LoadFailedSignalArgs args)
    {
        // Only main-frame failures (Started/Committed) count as "offline";
        // failing subresources must not flip the overlay. Guard cancellations
        // never reach this signal because blocked navigations are ignored in
        // the policy decision, not aborted.
        if (args.LoadEvent is LoadEvent.Started or LoadEvent.Committed)
        {
            _loadFailed = true;
            _offlineDetail.SetLabel("Check your internet connection and try again.");
            _offlinePanel.Visible = true;
        }

        return true; // handled - we show our own overlay instead of WebKit's error page
    }

    private Gtk.Widget OnCreate(WebView sender, WebView.CreateSignalArgs args)
    {
        // Popups never get a real window; the new-window policy decision
        // already redirected allowed URLs into this view. Returning null
        // blocks anything that slipped through.
        return null!;
    }

    private void LoadUri(string url)
    {
        _webView.LoadUri(url);
    }

    // ------------------------------------------------------------------
    // Window title: relay Instagram's unread count like Windows/Android
    // ------------------------------------------------------------------

    private void OnTitleChanged(GObject.Object sender, GObject.Object.NotifySignalArgs args)
    {
        var title = _webView.Title ?? string.Empty;
        Title = title.StartsWith(AppTitle, StringComparison.Ordinal)
            ? title
            : $"{title}  –  {AppTitle}";
    }

    // ------------------------------------------------------------------
    // Offline overlay
    // ------------------------------------------------------------------

    private Box CreateOfflinePanel()
    {
        var title = new Label { Wrap = true, Halign = Align.Center };
        title.SetMarkup("<span size='large' weight='bold'>Can't reach Instagram</span>");

        _offlineDetail = new Label { Wrap = true, Halign = Align.Center };
        _offlineDetail.SetLabel("Check your internet connection and try again.");
        _offlineDetail.AddCssClass("dim-label");

        var retry = Gtk.Button.NewWithLabel("Retry");
        retry.Halign = Align.Center;
        retry.OnClicked += (_, _) =>
        {
            _loadFailed = false;
            _offlinePanel.Visible = false;
            LoadUri(NavigationPolicy.HomeUrl);
        };

        var panel = Gtk.Box.New(Orientation.Vertical, 12);
        panel.Halign = Align.Center;
        panel.Valign = Align.Center;
        panel.AddCssClass("offline-panel");
        panel.Append(title);
        panel.Append(_offlineDetail);
        panel.Append(retry);
        panel.Visible = false;
        return panel;
    }

    private static void InstallOfflinePanelCss()
    {
        var css = new Gtk.CssProvider();
        css.LoadFromString("""
            .offline-panel {
                background-color: #fafafa;
                border-radius: 12px;
                padding: 24px;
                box-shadow: 0 4px 24px rgba(0, 0, 0, 0.12);
            }
            .dim-label {
                color: #666666;
            }
            """);
        // 600 == Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION
        Gtk.StyleContext.AddProviderForDisplay(Gdk.Display.GetDefault()!, css, 600);
    }

    // ------------------------------------------------------------------
    // Permissions (voice/video calls + notifications)
    // ------------------------------------------------------------------

    private bool OnPermissionRequest(WebView sender, WebView.PermissionRequestSignalArgs args)
    {
        // Auto-grant the camera and microphone Instagram's WebRTC call UI uses,
        // like Windows and Android. The page only accesses them after the user
        // starts or accepts a call. Notification permission is granted through
        // WebKitNative.WireNotificationPermission (WebKitGTK raises that
        // request on the WebContext, which the GirCore binding does not expose).
        if (args.Request is UserMediaPermissionRequest mediaRequest)
        {
            mediaRequest.Allow();
            return true;
        }

        return false; // everything else keeps WebKit's default (deny) behaviour
    }

    // ------------------------------------------------------------------
    // Commands (menu + keyboard accelerators)
    // ------------------------------------------------------------------

    private void RegisterActions()
    {
        AddAction("home", "<Ctrl>h", () => LoadUri(NavigationPolicy.HomeUrl));
        AddAction("open-inbox", "<Ctrl>d", () => LoadUri(NavigationPolicy.InboxUrl));
        AddAction("new-message", "<Ctrl>n", () => LoadUri(NavigationPolicy.NewMessageUrl));
        AddAction("notifications", null, () => LoadUri(NavigationPolicy.ActivityUrl));
        AddAction("reload", "F5", () => _webView.Reload());
        AddAction("logout", "<Ctrl><Shift>l", LogoutAndClearData);
        AddAction("about", null, () => new AboutDialog(this).Present());
        AddAction("exit", null, () => _app.Quit());
    }

    private void AddAction(string name, string? accel, Action handler)
    {
        var action = Gio.SimpleAction.New(name, null);
        action.OnActivate += (_, _) => handler();
        _app.AddAction(action);

        if (accel is not null)
        {
            _app.SetAccelsForAction($"app.{name}", new[] { accel });
        }
    }

    private Widget CreateMenuBar()
    {
        var go = Gio.Menu.New();
        go.Append("Home feed", "app.home");
        go.Append("Open inbox", "app.open-inbox");
        go.Append("New message", "app.new-message");
        go.Append("Notifications", "app.notifications");
        go.Append("Reload", "app.reload");

        var exitSection = Gio.Menu.New();
        exitSection.Append("Exit", "app.exit");
        go.AppendSection(null, exitSection);

        var privacy = Gio.Menu.New();
        privacy.Append("Log out & clear local data…", "app.logout");

        var aboutSection = Gio.Menu.New();
        aboutSection.Append("About GramChat", "app.about");
        privacy.AppendSection(null, aboutSection);

        var root = Gio.Menu.New();
        root.AppendSubmenu("Go", go);
        root.AppendSubmenu("Privacy", privacy);

        return Gtk.PopoverMenuBar.NewFromModel(root);
    }

    // ------------------------------------------------------------------
    // Privacy: log out & clear local data
    // ------------------------------------------------------------------

    private async void LogoutAndClearData()
    {
        var dialog = new AlertDialog
        {
            Message = "Log out and delete all locally stored Instagram data?",
            Detail = "This removes the session cookies, cached files and site data from this computer. You will need to log in again next time.",
            Buttons = new[] { "Cancel", "Log out & clear data" },
        };

        int choice;
        try
        {
            choice = await dialog.ChooseAsync(this);
        }
        catch
        {
            return; // dialog dismissed (e.g. Escape)
        }

        if (choice != 1)
        {
            return;
        }

        try
        {
            WebKitNative.ClearWebsiteData(_dataManager);
        }
        catch
        {
            // Clearing is best-effort; navigating to the inbox (which becomes
            // the login page once the session is gone) is always safe.
        }

        // The encrypted session database must not keep any login behind after
        // the user asked to log out.
        try
        {
            Task.Run(() => SecureSessionStore.ClearAllAsync(DataDir)).GetAwaiter().GetResult();
        }
        catch
        {
            // Best-effort.
        }

        LoadUri(NavigationPolicy.HomeUrl);
    }

    // ------------------------------------------------------------------
    // Encrypted session database (shared with the Windows client)
    // ------------------------------------------------------------------

    private static string WebKitCookieDbPath => Path.Combine(Path.Combine(DataDir, "WebKitGTK"), "cookies.sqlite");

    /// <summary>
    /// Restores WebKitGTK's cookie database (plain SQLite on disk) from the
    /// encrypted store. Must run before the network process opens the cookie DB
    /// (i.e. before the first load) - the WebsiteDataManager only creates the
    /// directory, so restoring the files here is safe.
    /// </summary>
    private static void RestoreStoredSessionFromEncryptedStore()
    {
        try
        {
            // Run on a worker thread: this blocks the GTK thread while waiting,
            // so the async continuations must never be marshalled back to the
            // main loop (Task.Run keeps them on the thread pool).
            var ready = Task.Run(() => SecureSessionStore.InitializeAsync(DataDir)).GetAwaiter().GetResult();
            if (!ready)
            {
                return;
            }

            // The WebKitGTK profile already has a live session - nothing to do.
            if (SecureSessionStore.ReadWebKitCookieDb(WebKitCookieDbPath) is not null)
            {
                return;
            }

            var stored = Task.Run(() => SecureSessionStore.GetLatestSessionAsync(DataDir)).GetAwaiter().GetResult();
            if (stored is null || string.IsNullOrWhiteSpace(stored.SessionData))
            {
                return;
            }

            var files = JsonSerializer.Deserialize<Dictionary<string, string>>(stored.SessionData);
            if (files is null || !files.TryGetValue("cookies.sqlite", out var mainB64))
            {
                return;
            }

            var mainBytes = Convert.FromBase64String(mainB64);
            var mainPath = WebKitCookieDbPath;
            if (!IsValidCookieDb(mainBytes))
            {
                return; // corrupt snapshot - never restore garbage
            }

            var dir = Path.GetDirectoryName(mainPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(mainPath, mainBytes);

            foreach (var suffix in new[] { "cookies.sqlite-wal", "cookies.sqlite-shm" })
            {
                if (files.TryGetValue(suffix, out var b64))
                {
                    File.WriteAllBytes(Path.Combine(dir, suffix), Convert.FromBase64String(b64));
                }
            }
        }
        catch (Exception ex)
        {
            // Restore is best-effort: worst case the user signs in again.
            System.Diagnostics.Debug.WriteLine($"Session restore failed: {ex}");
        }
    }

    private async Task TrySnapshotSessionAsync()
    {
        // Let WebKit flush the cookies it just accepted before copying the DB.
        // Everything after this point is plain file/SQLite work (no WebKit/GTK
        // objects), so continuing on a thread-pool thread is safe.
        await Task.Delay(1200).ConfigureAwait(false);

        if (!TryBuildSessionSnapshot(out var record))
        {
            return;
        }

        try
        {
            await SecureSessionStore.SaveSessionAsync(DataDir, record).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Snapshot is best-effort; never let it break the chat experience.
            System.Diagnostics.Debug.WriteLine($"Session snapshot failed: {ex}");
        }
    }

    /// <summary>Best-effort snapshot at window close (cookies only, no JS).</summary>
    private void SnapshotSessionOnExit()
    {
        try
        {
            if (TryBuildSessionSnapshot(out var record))
            {
                Task.Run(() => SecureSessionStore.SaveSessionAsync(DataDir, record)).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    /// <summary>
    /// Copies the WebKitGTK cookie database (plus WAL/SHM, when present) into a
    /// snapshot record. Returns false when not logged in or within the cooldown.
    /// </summary>
    private bool TryBuildSessionSnapshot(out StoredSession record)
    {
        record = null!;
        if (DateTime.UtcNow - _lastSessionSnapshotUtc < SessionSnapshotCooldown)
        {
            return false;
        }

        _lastSessionSnapshotUtc = DateTime.UtcNow;

        try
        {
            var cookieDb = WebKitCookieDbPath;
            var session = SecureSessionStore.ReadWebKitCookieDb(cookieDb);
            if (session is null)
            {
                return false; // not logged in (login page or logged out)
            }

            record = new StoredSession(
                UserId: session.Value.UserId,
                Username: string.Empty,
                DisplayName: string.Empty,
                AvatarUrl: string.Empty,
                SessionData: SerializeCookieDbSnapshot(Path.GetDirectoryName(cookieDb)!),
                LastLogin: DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session snapshot failed: {ex}");
            return false;
        }
    }

    private static string SerializeCookieDbSnapshot(string dir)
    {
        var snapshot = new Dictionary<string, string>();
        foreach (var file in new[] { "cookies.sqlite", "cookies.sqlite-wal", "cookies.sqlite-shm" })
        {
            var path = Path.Combine(dir, file);
            if (File.Exists(path))
            {
                snapshot[file] = Convert.ToBase64String(File.ReadAllBytes(path));
            }
        }

        return JsonSerializer.Serialize(snapshot);
    }

    private static bool IsValidCookieDb(byte[] bytes)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"gramchat-validate-{Guid.NewGuid():N}.db");
            try
            {
                File.WriteAllBytes(path, bytes);
                using var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Mode=ReadOnly");
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT count(*) FROM cookies;";
                cmd.ExecuteScalar();
                return true;
            }
            finally
            {
                try { File.Delete(path); } catch { /* best effort */ }
            }
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Window-state persistence (~/.config/GramChat/window.json)
    // ------------------------------------------------------------------

    private void SaveWindowState()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(WindowSettingsPath)!);
            var state = new WindowStateInfo
            {
                Width = GetWidth(),
                Height = GetHeight(),
                Maximized = IsMaximized(),
            };
            File.WriteAllText(WindowSettingsPath, JsonSerializer.Serialize(state));
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }

    private void RestoreWindowState()
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

            if (state.Width is >= 640 and <= 10000 && state.Height is >= 480 and <= 10000)
            {
                SetDefaultSize(state.Width.Value, state.Height.Value);
            }

            if (state.Maximized)
            {
                Maximize();
            }
        }
        catch
        {
            // Settings persistence is best-effort.
        }
    }

    private sealed class WindowStateInfo
    {
        public int? Width { get; set; }
        public int? Height { get; set; }
        public bool Maximized { get; set; }
    }
}