package com.instachat.app.webview

import android.net.Uri

/**
 * Central policy that decides which web locations InstaChat may load.
 * Everything outside the allowlist is cancelled and bounced back to the DM inbox,
 * which is what keeps Reels, Explore, Feed and Stories out of reach.
 */
object DmNavigationPolicy {

    const val INBOX_URL = "https://www.instagram.com/direct/inbox/"
    const val NEW_MESSAGE_URL = "https://www.instagram.com/direct/new/"

    enum class NavAction {
        Allow,
        BounceToInbox,
        Block
    }

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

    private val instagramAllowedPrefixes = arrayOf(
        "/direct",
        "/accounts",
        "/challenge",
        "/ajax",
        "/api",
        "/graphql",
        "/oauth"
    )

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

        if (scheme != "https") {
            return NavAction.Block
        }

        val host = uri.host?.lowercase() ?: return NavAction.Block
        val path = uri.path ?: "/"

        if (isInstagramHost(host)) {
            for (prefix in instagramAllowedPrefixes) {
                if (path.startsWith(prefix, ignoreCase = true)) {
                    return NavAction.Allow
                }
            }
            return NavAction.BounceToInbox
        }

        if (loginFlowHosts.contains(host)) {
            for (prefix in loginFlowPrefixes) {
                if (path.startsWith(prefix, ignoreCase = true)) {
                    return NavAction.Allow
                }
            }
            return NavAction.Block
        }

        return NavAction.Block
    }

    private fun isInstagramHost(host: String): Boolean {
        return host.equals("instagram.com", ignoreCase = true)
            || host.endsWith(".instagram.com", ignoreCase = true)
    }
}
