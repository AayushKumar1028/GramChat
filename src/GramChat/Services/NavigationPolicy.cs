namespace GramChat.Services;

/// <summary>
/// Central policy that decides which web locations the app may load.
/// </summary>
/// <remarks>
/// GramChat embeds Instagram's real web client, so the whole product is
/// reachable — Home feed, profiles, follow requests, private accounts,
/// notifications, settings, teen/parental supervision and Direct Messages —
/// with exactly one exception: <b>Reels and Explore</b>. Instagram keeps every
/// Reels surface under a <c>reel</c>/<c>reels</c>/<c>clips</c> path segment
/// (the rail tab, the in-feed reel viewer, <c>/reels/audio/…</c> and a
/// profile's <c>/username/reels/</c> tab) and Explore under <c>explore</c>, so
/// matching whole segments catches all of them.
///
/// Blocked routes are cancelled and bounced back to the Home feed. Links that
/// leave Instagram are handed to the system browser rather than the embedded
/// view, so the guard can never be escaped by an external page.
/// </remarks>
public static class NavigationPolicy
{
    public const string HomeUrl = "https://www.instagram.com/";
    public const string InboxUrl = "https://www.instagram.com/direct/inbox/";
    public const string NewMessageUrl = "https://www.instagram.com/direct/new/";
    public const string ActivityUrl = "https://www.instagram.com/accounts/activity/";

    public enum NavAction
    {
        Allow,
        BounceToHome,
        Block,
        OpenExternal,
    }

    // Path segments that must never be reachable.
    private static readonly HashSet<string> BlockedSegments = new(StringComparer.OrdinalIgnoreCase)
    {
        "reel",
        "reels",
        "reelsaudio",
        "clips",
        "explore",
    };

    /// <summary>
    /// True when the URL always has to be loaded for login/authentication flows
    /// (e.g. "Continue with Facebook").
    /// </summary>
    private static readonly HashSet<string> LoginFlowHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "www.facebook.com",
        "m.facebook.com",
        "facebook.com",
        "web.facebook.com",
    };

    // Path prefixes on facebook.com that belong to authentication dialogs only.
    private static readonly string[] LoginFlowPrefixes =
    {
        "/login",
        "/dialog/",
        "/oauth",
        "/checkpoint",
        "/recover",
        "/signup",
    };

    /// <summary>True when the URL is a Reels or Explore location.</summary>
    public static bool IsBlockedContent(Uri uri) =>
        uri.IsAbsoluteUri && HasBlockedSegment(uri.AbsolutePath);

    /// <summary>True when the URL is a location the app may load as-is.</summary>
    public static bool IsAllowed(Uri uri) => Classify(uri) == NavAction.Allow;

    public static NavAction Classify(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
        {
            return NavAction.Block;
        }

        switch (uri.Scheme)
        {
            case "about":
                return string.IsNullOrEmpty(uri.Host) || uri.Host == "blank"
                    ? NavAction.Allow
                    : NavAction.Block;

            case "https":
                break;

            case "http":
                // Instagram is https-only; plain-http Instagram links are sent
                // back to the (secure) Home feed, everything else opens in the
                // system browser.
                return IsInstagramHost(uri.Host) ? NavAction.BounceToHome : NavAction.OpenExternal;

            case "mailto":
            case "tel":
            case "sms":
                return NavAction.OpenExternal;

            default:
                // file:, javascript:, data: and everything else is out.
                return NavAction.Block;
        }

        var host = uri.Host;
        var path = uri.AbsolutePath;

        if (IsInstagramHost(host))
        {
            return HasBlockedSegment(path) ? NavAction.BounceToHome : NavAction.Allow;
        }

        if (LoginFlowHosts.Contains(host))
        {
            foreach (var prefix in LoginFlowPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return NavAction.Allow;
                }
            }

            return NavAction.Block;
        }

        // Anything else leaves Instagram: open it in the system browser.
        return NavAction.OpenExternal;
    }

    private static bool HasBlockedSegment(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (BlockedSegments.Contains(segment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInstagramHost(string host)
    {
        return host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase);
    }
}
