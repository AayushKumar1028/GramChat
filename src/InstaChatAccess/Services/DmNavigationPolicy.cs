namespace InstaChatAccess.Services;

/// <summary>
/// Central policy that decides which web locations InstaChat Access may load.
/// Everything outside the allowlist is cancelled and bounced back to the DM inbox,
/// which is what keeps Reels, Explore, Feed and Stories out of reach.
/// </summary>
public static class DmNavigationPolicy
{
    public const string InboxUrl = "https://www.instagram.com/direct/inbox/";
    public const string NewMessageUrl = "https://www.instagram.com/direct/new/";

    public enum NavAction
    {
        Allow,
        BounceToInbox,
        Block
    }

    // Hosts that may only be loaded for login/authentication flows (e.g. "Continue with Facebook").
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

    // Instagram paths that belong to the chat / authentication experience.
    private static readonly string[] InstagramAllowedPrefixes =
    {
        "/direct",    // direct messages
        "/accounts",  // login, two-factor, password reset
        "/challenge", // security challenges
        "/ajax",      // internal endpoints hit as top-level navigations in some flows
        "/api",
        "/graphql",
        "/oauth",
    };

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
            default:
                // http:, file:, javascript:, mailto:, and everything else is out.
                return NavAction.Block;
        }

        var host = uri.Host;
        var path = uri.AbsolutePath;

        if (IsInstagramHost(host))
        {
            foreach (var prefix in InstagramAllowedPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return NavAction.Allow;
                }
            }

            // The root is either the login page (unauthenticated) or the feed (authenticated).
            // Bouncing it to the inbox handles both cases correctly.
            return NavAction.BounceToInbox;
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

        return NavAction.Block;
    }

    private static bool IsInstagramHost(string host)
    {
        return host.Equals("instagram.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".instagram.com", StringComparison.OrdinalIgnoreCase);
    }
}
