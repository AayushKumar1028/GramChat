# Default ProGuard rules
-keepattributes *Annotation*
-keep class * extends android.webkit.WebViewClient {
    void onReceivedSslError(android.webkit.WebView, android.webkit.SslErrorHandler, android.net.http.SslError);
}
