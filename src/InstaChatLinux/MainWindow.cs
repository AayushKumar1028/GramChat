using System.IO;
using System.Text.Json;
using GObject;
using Gtk;
using InstaChat.Services;
using WebKit;

namespace InstaChat;

/// <summary>
/// Main window of the Linux client. Functional twin of the Windows WPF
/// <c>MainWindow</c> built on GTK4 + WebKitGTK (6.0): the same DM-only
/// navigation policy, the same in-page hardening script, automatic camera /
/// microphone / notification permissions for voice and video calls, an offline
/// overlay, window-state persistence, and a single-instance application.
/// </summary>
public sealed class MainWindow : Window
{
    /// <summary>Release label shown in the About dialog.</summary>
    public const string VersionLabel = "v1.0-Linux-beta1";

    private const string AppTitle = "InstaChat";
    private const string AppDataFolderName = "InstaChat";

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

    private MainWindow(Application app)
    {
        _app = app;

        Title = AppTitle;
        SetDefaultSize(1080, 760);
        SetSizeRequest(640, 480);

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
            return false; // allow the window to close
        };
        OnDestroy += (_, _) => _app.Quit();

        LoadUri(DmNavigationPolicy.InboxUrl);
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
        // dedicated profile under ~/.local/share/InstaChat, mirroring the
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
            || Environment.GetEnvironmentVariable("INSTACHAT_DEVTOOLS") == "1";

        // Second layer of the DM-only guard: the SPA route watcher and page
        // hardening script, identical to the one the Windows client injects.
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
                    switch (DmNavigationPolicy.Classify(uri))
                    {
                        case DmNavigationPolicy.NavAction.Allow:
                            decision.Use();
                            break;

                        case DmNavigationPolicy.NavAction.BounceToInbox:
                            decision.Ignore();
                            LoadUri(DmNavigationPolicy.InboxUrl);
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
                // allowed URLs are redirected into the main chat view instead.
                if (decision is NavigationPolicyDecision popup
                    && Uri.TryCreate(popup.NavigationAction.GetRequest()?.Uri, UriKind.Absolute, out var popupUri)
                    && DmNavigationPolicy.IsAllowed(popupUri))
                {
                    LoadUri(popupUri.ToString());
                }

                decision.Ignore();
                return true;

            default:
                // Response decisions and anything else: let WebKit decide.
                return false;
        }
    }

    private void OnLoadChanged(WebView sender, WebView.LoadChangedSignalArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished && !_loadFailed)
        {
            _offlinePanel.Visible = false;
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
            LoadUri(DmNavigationPolicy.InboxUrl);
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
        AddAction("open-inbox", "<Ctrl>d", () => LoadUri(DmNavigationPolicy.InboxUrl));
        AddAction("new-message", "<Ctrl>n", () => LoadUri(DmNavigationPolicy.NewMessageUrl));
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
        var messages = Gio.Menu.New();
        messages.Append("Open inbox", "app.open-inbox");
        messages.Append("New message", "app.new-message");
        messages.Append("Reload", "app.reload");

        var exitSection = Gio.Menu.New();
        exitSection.Append("Exit", "app.exit");
        messages.AppendSection(null, exitSection);

        var privacy = Gio.Menu.New();
        privacy.Append("Log out & clear local data…", "app.logout");

        var aboutSection = Gio.Menu.New();
        aboutSection.Append("About InstaChat", "app.about");
        privacy.AppendSection(null, aboutSection);

        var root = Gio.Menu.New();
        root.AppendSubmenu("Messages", messages);
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

        LoadUri(DmNavigationPolicy.InboxUrl);
    }

    // ------------------------------------------------------------------
    // Window-state persistence (~/.config/InstaChat/window.json)
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