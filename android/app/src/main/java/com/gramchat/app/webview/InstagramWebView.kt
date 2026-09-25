package com.gramchat.app.webview

import android.annotation.SuppressLint
import android.content.Context
import android.content.Intent
import android.net.Uri
import android.webkit.CookieManager
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceError
import android.webkit.WebView
import android.webkit.WebViewClient
import com.gramchat.app.security.SecureSessionStore
import com.gramchat.app.security.StoredSession
import org.json.JSONObject
import org.json.JSONTokener

/**
 * Callback interface for WebView events.
 */
interface GramWebViewClient {
    fun onNavigationError(errorStatus: String)
    fun onNavigationSuccess()
    fun onTitleChanged(title: String)
    fun onPageFinished()
}

/**
 * Configured WebView for Instagram DM experience.
 * Handles navigation policy, JS injection, and permission management.
 */
class InstagramWebView(
    context: Context,
    private val callback: GramWebViewClient
) : WebView(context) {

    private var canceledByPolicy = false
    private val permissionHandler = WebViewPermissionHandler(context as android.app.Activity)

    // Encrypted session database (SQLCipher + Android Keystore master key).
    private val sessionStore: SecureSessionStore by lazy { SecureSessionStore(context) }
    private var lastSnapshotMs = 0L

    @SuppressLint("SetJavaScriptEnabled")
    fun setup() {
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        settings.databaseEnabled = true
        settings.mediaPlaybackRequiresUserGesture = false
        settings.allowContentAccess = true
        settings.loadWithOverviewMode = true
        settings.useWideViewPort = true
        settings.builtInZoomControls = false
        settings.displayZoomControls = false
        settings.setSupportZoom(false)
        settings.javaScriptCanOpenWindowsAutomatically = false
        settings.allowFileAccess = false

        // Set user agent to desktop Chrome for best Instagram web compatibility
        settings.userAgentString = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

        // Add JS interface for title relay
        addJavascriptInterface(TitleRelayInterface(), "GramChatHost")

        webViewClient = GramWebViewClientImpl()
        webChromeClient = permissionHandler

        // Restore the most recent login from the encrypted session database
        // before the first page load, unless the WebView already has a session.
        restoreStoredSession()
    }

    fun handlePermissionResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ): Boolean {
        return permissionHandler.handlePermissionResult(requestCode, permissions, grantResults)
    }

    fun navigateHome() {
        loadUrl(NavigationPolicy.HOME_URL)
    }

    fun navigateToInbox() {
        loadUrl(NavigationPolicy.INBOX_URL)
    }

    fun navigateToNewMessage() {
        loadUrl(NavigationPolicy.NEW_MESSAGE_URL)
    }

    fun navigateToActivity() {
        loadUrl(NavigationPolicy.ACTIVITY_URL)
    }

    fun reloadPage() {
        reload()
    }

    suspend fun clearAllData() {
        android.webkit.WebStorage.getInstance().deleteAllData()
        clearCache(true)
        clearHistory()
        clearFormData()
        CookieManager.getInstance().removeAllCookies(null)
        CookieManager.getInstance().flush()
    }

    /** Deletes every login stored in the encrypted session database. */
    fun clearStoredSessions() {
        runCatching { sessionStore.clearAll() }
    }

    // ------------------------------------------------------------------
    // Encrypted session database: restore + snapshot
    // ------------------------------------------------------------------

    /**
     * Injects the most recent stored session's cookies when the WebView has no
     * live session, so the user stays signed in after clearing app data or
     * reinstalling (without ever storing the Instagram password).
     */
    private fun restoreStoredSession() {
        runCatching {
            val cookieManager = CookieManager.getInstance()
            val current = cookieManager.getCookie(INSTAGRAM_ORIGIN) ?: ""
            if (current.contains("sessionid=")) return // live session already

            val stored = sessionStore.latestSession() ?: return
            val pairs = stored.sessionData.split(";").map { it.trim() }.filter { it.contains("=") }
            if (pairs.isEmpty()) return

            pairs.forEach { pair -> cookieManager.setCookie(INSTAGRAM_ORIGIN, pair) }
            cookieManager.flush()
        }
    }

    /**
     * Captures the current login (session cookies + profile) into the encrypted
     * database whenever an authenticated Instagram page finishes loading.
     */
    private fun snapshotSessionIfAuthenticated() {
        if (System.currentTimeMillis() - lastSnapshotMs < SNAPSHOT_COOLDOWN_MS) return
        lastSnapshotMs = System.currentTimeMillis()

        runCatching {
            val cookieManager = CookieManager.getInstance()
            val cookieHeader = cookieManager.getCookie(INSTAGRAM_ORIGIN) ?: return
            if (!cookieHeader.contains("sessionid=")) return // not logged in

            val dsUserId = extractCookieValue(cookieHeader, "ds_user_id")
            // Profile metadata comes from Instagram's own viewer object. The JS
            // returns a JSON string, and evaluateJavascript JSON-encodes the
            // result, so the callback receives a quoted JSON string.
            evaluateJavascript(VIEWER_PROFILE_JS) { json ->
                val viewer = parseViewer(json)
                val userId = viewer?.userId?.takeIf { it.isNotBlank() } ?: dsUserId ?: ""
                if (userId.isBlank()) return@evaluateJavascript

                runCatching {
                    sessionStore.saveSession(
                        StoredSession(
                            userId = userId,
                            username = viewer?.username ?: "",
                            displayName = viewer?.displayName ?: "",
                            avatarUrl = viewer?.avatarUrl ?: "",
                            sessionData = cookieHeader,
                            lastLoginUtc = System.currentTimeMillis() / 1000
                        )
                    )
                }
            }
        }
    }

    private fun extractCookieValue(header: String, name: String): String? {
        for (pair in header.split(";")) {
            val trimmed = pair.trim()
            if (trimmed.startsWith("$name=")) return trimmed.substringAfter("=").trim()
        }
        return null
    }

    private data class ViewerProfile(
        val userId: String,
        val username: String,
        val displayName: String,
        val avatarUrl: String
    )

    private fun parseViewer(json: String?): ViewerProfile? {
        if (json.isNullOrBlank() || json == "null") return null
        return try {
            val decoded = JSONTokener(json).nextValue()
            if (decoded !is String) return null
            val obj = JSONObject(decoded)
            ViewerProfile(
                userId = obj.optString("userId", ""),
                username = obj.optString("username", ""),
                displayName = obj.optString("displayName", ""),
                avatarUrl = obj.optString("avatarUrl", "")
            )
        } catch (_: Exception) {
            null
        }
    }

    private inner class GramWebViewClientImpl : WebViewClient() {

        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            when (NavigationPolicy.classify(request.url.toString())) {
                NavigationPolicy.NavAction.Allow -> return false
                NavigationPolicy.NavAction.BounceToHome -> {
                    canceledByPolicy = true
                    view.loadUrl(NavigationPolicy.HOME_URL)
                    return true
                }
                NavigationPolicy.NavAction.OpenExternal -> {
                    canceledByPolicy = true
                    openExternally(request.url)
                    return true
                }
                NavigationPolicy.NavAction.Block -> {
                    canceledByPolicy = true
                    return true
                }
            }
        }

        override fun onPageStarted(view: WebView, url: String?, favicon: android.graphics.Bitmap?) {
            super.onPageStarted(view, url, favicon)
            // Inject the page hardening script on every page load
            url?.let {
                if (it.startsWith("https://www.instagram.com") || it.startsWith("https://instagram.com")) {
                    view.evaluateJavascript(PageHardening.getScript(), null)
                }
            }
        }

        override fun onPageFinished(view: WebView, url: String?) {
            super.onPageFinished(view, url)
            callback.onPageFinished()
            // Re-inject to ensure it runs even if the SPA replaced the DOM
            url?.let {
                if (it.startsWith("https://www.instagram.com") || it.startsWith("https://instagram.com")) {
                    view.evaluateJavascript(PageHardening.getScript(), null)
                }
            }
            // Capture the login into the encrypted session database.
            snapshotSessionIfAuthenticated()
        }

        override fun onReceivedError(
            view: WebView,
            request: WebResourceRequest?,
            error: WebResourceError?
        ) {
            if (request?.isForMainFrame == true && !canceledByPolicy) {
                val description = error?.description?.toString() ?: "Unknown error"
                callback.onNavigationError(description)
            }
        }

        override fun onPageCommitVisible(view: WebView, url: String?) {
            super.onPageCommitVisible(view, url)
            if (!canceledByPolicy) {
                callback.onNavigationSuccess()
            }
            canceledByPolicy = false
        }
    }

    private companion object {
        const val INSTAGRAM_ORIGIN = "https://www.instagram.com"
        const val SNAPSHOT_COOLDOWN_MS = 60_000L
        const val VIEWER_PROFILE_JS =
            "(function() { try { var v = window._sharedData && window._sharedData.config && window._sharedData.config.viewer; " +
                "if (!v) return 'null'; " +
                "return JSON.stringify({ userId: String(v.id || ''), username: v.username || '', " +
                "displayName: v.full_name || '', avatarUrl: (v.profile_pic_url || '').replace(/^http:\\/\\//, 'https://') }); " +
                "} catch (e) { return 'null'; } })()"
    }

    /**
     * Hands a link that leaves Instagram (or a mailto:/tel: link) to the system
     * browser, so external pages never run inside the guarded WebView.
     */
    private fun openExternally(uri: Uri) {
        try {
            context.startActivity(Intent(Intent.ACTION_VIEW, uri).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
        } catch (_: Exception) {
            // No installed app can handle the link - ignore.
        }
    }

    /**
     * Keeps the screen awake for the duration of a voice/video call (and lets
     * it sleep again once the call ends). The injected script reports the call
     * state through the same bridge the title uses.
     */
    private fun setCallActive(active: Boolean) {
        val window = (context as? android.app.Activity)?.window ?: return
        if (active) {
            window.addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        } else {
            window.clearFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        }
    }

    private inner class TitleRelayInterface {
        @JavascriptInterface
        fun postMessage(json: String) {
            try {
                val jsonObj = JSONObject(json)
                when (jsonObj.optString("type", "")) {
                    "title" -> {
                        val title = jsonObj.optString("title", "")
                        if (title.isNotBlank()) {
                            post { callback.onTitleChanged(title) }
                        }
                    }
                    "call" -> {
                        val active = jsonObj.optBoolean("active", false)
                        post { setCallActive(active) }
                    }
                }
            } catch (_: Exception) {
                // Ignore malformed messages
            }
        }
    }
}
