package com.gramchat.app.webview

import android.net.Uri

/**
 * Central policy that decides which web locations GramChat may load.
 *
 * GramChat embeds Instagram's real web client, so the whole product is
 * reachable — Home feed, profiles, follow requests, private accounts,
 * notifications, settings, teen/parental supervision and Direct Messages —
 * with exactly one exception: **Reels and Explore**. Instagram keeps every
 * Reels surface under a `reel`/`reels`/`clips` path segment (the rail tab, the
 * in-feed reel viewer, `/reels/audio/…` and a profile's `/username/reels/`
 * tab) and Explore under `explore`, so matching whole segments catches all of
 * them.
 *
 * Blocked routes are cancelled and bounced back to the Home feed. Links that
 * leave Instagram are handed to the system browser rather than the embedded
 * view, so the guard can never be escaped by an external page.
 */
object NavigationPolicy {

    const val HOME_URL = "https://www.instagram.com/"
    const val INBOX_URL = "https://www.instagram.com/direct/inbox/"
    const val NEW_MESSAGE_URL = "https://www.instagram.com/direct/new/"
    const val ACTIVITY_URL = "https://www.instagram.com/accounts/activity/"

    enum class NavAction {
        Allow,
        BounceToHome,
        Block,
        OpenExternal
    }

    // Path segments that must never be reachable.
    private val blockedSegments = setOf("reel", "reels", "reelsaudio", "clips", "explore")

    private val loginFlowHosts = setOf(
        "www.facebook.com",
        "m.facebook.com",
        "facebook.com",
        "web.facebook.com"
    )

    private val loginFlowPrefixes = arrayOf(
        "/login",
        "/dialog/",
        "/oauth",
        "/checkpoint",
        "/recover",
        "/signup"
    )

    /** True when the URL is a Reels or Explore location. */
    fun isBlockedContent(uri: Uri): Boolean = hasBlockedSegment(uri.path ?: "/")

    fun isAllowed(uri: Uri): Boolean = classify(uri) == NavAction.Allow

    fun classify(urlString: String): NavAction {
        val uri = try {
            Uri.parse(urlString)
        } catch (e: Exception) {
            return NavAction.Block
        }
        return classify(uri)
    }

    fun classify(uri: Uri): NavAction {
        val scheme = uri.scheme?.lowercase() ?: return NavAction.Block

        if (scheme == "about") {
            val host = uri.host ?: ""
            return if (host.isEmpty() || host == "blank") NavAction.Allow else NavAction.Block
        }

        when (scheme) {
            "https" -> { /* handled below */ }
            "http" ->
                // Instagram is https-only; plain-http Instagram links go back
                // to the secure Home feed, everything else opens externally.
                return if (isInstagramHost(uri.host ?: "")) NavAction.BounceToHome else NavAction.OpenExternal
            "mailto", "tel", "sms" -> return NavAction.OpenExternal
            else -> return NavAction.Block
        }

        val host = uri.host?.lowercase() ?: return NavAction.Block
        val path = uri.path ?: "/"

        if (isInstagramHost(host)) {
            return if (hasBlockedSegment(path)) NavAction.BounceToHome else NavAction.Allow
        }

        if (loginFlowHosts.contains(host)) {
            for (prefix in loginFlowPrefixes) {
                if (path.startsWith(prefix, ignoreCase = true)) {
                    return NavAction.Allow
                }
            }
            return NavAction.Block
        }

        // Anything else leaves Instagram: open it in the system browser.
        return NavAction.OpenExternal
    }

    private fun hasBlockedSegment(path: String): Boolean =
        path.split('/').any { it.isNotEmpty() && blockedSegments.contains(it.lowercase()) }

    private fun isInstagramHost(host: String): Boolean {
        return host.equals("instagram.com", ignoreCase = true)
            || host.endsWith(".instagram.com", ignoreCase = true)
    }
}
