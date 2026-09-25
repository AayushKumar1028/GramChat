using System.Runtime.InteropServices;
using WebKit;

namespace GramChat.Services;

/// <summary>
/// Small P/Invoke bridge for the few WebKitGTK capabilities the GirCore
/// 0.8.1 binding does not expose:
///
///  1. Auto-granting desktop-notification permission. WebKitGTK raises the
///     permission request on the WebContext's "notification-permission-request"
///     signal, which GirCore does not bind, so it is connected here and every
///     request is allowed (same behaviour as the Windows client's auto-granted
///     Notifications permission - new-message and incoming-call toasts).
///
///  2. Clearing website data on logout (cookies, DOM storage, caches). The
///     binding ships only the *finish* method, so the async clear function is
///     invoked directly and left to complete in WebKit's process model.
/// </summary>
internal static class WebKitNative
{
    private const string WebKitLib = "libwebkitgtk-6.0.so.4";
    private const string GObjectLib = "libgobject-2.0.so.0";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NotificationPermissionCallback(IntPtr context, IntPtr request, IntPtr userData);

    // The delegate must outlive every native call site, so keep a static
    // reference (the GC would otherwise collect it while still registered).
    private static readonly NotificationPermissionCallback NotificationHandler = OnNotificationPermissionRequest;

    [DllImport(GObjectLib)]
    private static extern ulong g_signal_connect_data(
        IntPtr instance,
        string detailedSignal,
        NotificationPermissionCallback cHandler,
        IntPtr data,
        IntPtr destroyData,
        uint connectFlags);

    [DllImport(WebKitLib, EntryPoint = "webkit_permission_request_allow")]
    private static extern void webkit_permission_request_allow(IntPtr request);

    [DllImport(WebKitLib, EntryPoint = "webkit_website_data_manager_clear")]
    private static extern void webkit_website_data_manager_clear(
        IntPtr manager,
        int types,
        long timespan,
        IntPtr cancellable,
        IntPtr callback,
        IntPtr userData);

    /// <summary>Connects an always-allow handler for web notification permission requests.</summary>
    public static void WireNotificationPermission(WebContext context)
    {
        g_signal_connect_data(
            context.Handle.DangerousGetHandle(),
            "notification-permission-request",
            NotificationHandler,
            IntPtr.Zero,
            IntPtr.Zero,
            0);
    }

    /// <summary>Clears all website data (cookies, DOM storage, caches) for the app's profile.</summary>
    public static void ClearWebsiteData(WebsiteDataManager manager)
    {
        webkit_website_data_manager_clear(
            manager.Handle.DangerousGetHandle(),
            (int)WebsiteDataTypes.All,
            long.MinValue, // since the beginning of time
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);
    }

    private static void OnNotificationPermissionRequest(IntPtr context, IntPtr request, IntPtr userData)
    {
        if (request != IntPtr.Zero)
        {
            webkit_permission_request_allow(request);
        }
    }
}