package com.instachat.app.webview

import android.annotation.SuppressLint
import android.content.Context
import android.webkit.JavascriptInterface
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceError
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONObject

/**
 * Callback interface for WebView events.
 */
interface InstaWebViewClient {
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
    private val callback: InstaWebViewClient
) : WebView(context) {

    private var canceledByPolicy = false
    private val permissionHandler = WebViewPermissionHandler(context as android.app.Activity)

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

        // Set white background to prevent dark flash before page loads
        setBackgroundColor(android.graphics.Color.WHITE)

        // Add JS interface for title relay
        addJavascriptInterface(TitleRelayInterface(), "InstaChatHost")

        webViewClient = InstaWebViewClientImpl()
        webChromeClient = permissionHandler

        // Inject page hardening script on every document start/finish
    }

    fun handlePermissionResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ): Boolean {
        return permissionHandler.handlePermissionResult(requestCode, permissions, grantResults)
    }

    fun navigateToInbox() {
        loadUrl(DmNavigationPolicy.INBOX_URL)
    }

    fun navigateToNewMessage() {
        loadUrl(DmNavigationPolicy.NEW_MESSAGE_URL)
    }

    fun reloadPage() {
        reload()
    }

    suspend fun clearAllData() {
        android.webkit.WebStorage.getInstance().deleteAllData()
        clearCache(true)
        clearHistory()
        clearFormData()
        android.webkit.CookieManager.getInstance().removeAllCookies(null)
        android.webkit.CookieManager.getInstance().flush()
    }

    private inner class InstaWebViewClientImpl : WebViewClient() {

        override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean {
            val url = request.url.toString()
            when (DmNavigationPolicy.classify(url)) {
                DmNavigationPolicy.NavAction.Allow -> return false
                DmNavigationPolicy.NavAction.BounceToInbox -> {
                    canceledByPolicy = true
                    view.loadUrl(DmNavigationPolicy.INBOX_URL)
                    return true
                }
                DmNavigationPolicy.NavAction.Block -> {
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

    private inner class TitleRelayInterface {
        @JavascriptInterface
        fun postMessage(json: String) {
            try {
                val jsonObj = JSONObject(json)
                val type = jsonObj.optString("type", "")
                if (type == "title") {
                    val title = jsonObj.optString("title", "")
                    if (title.isNotBlank()) {
                        post { callback.onTitleChanged(title) }
                    }
                }
            } catch (_: Exception) {
                // Ignore malformed messages
            }
        }
    }
}
