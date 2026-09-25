package com.gramchat.app.ui

import android.app.Activity
import android.view.ViewGroup
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.viewinterop.AndroidView
import com.gramchat.app.webview.GramWebViewClient
import com.gramchat.app.webview.InstagramWebView
import kotlinx.coroutines.MainScope
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun GramChatScreen() {
    val context = LocalContext.current
    val activity = context as Activity

    var showOfflineOverlay by remember { mutableStateOf(false) }
    var offlineErrorDetail by remember { mutableStateOf("Check your internet connection and try again.") }
    var showAboutDialog by remember { mutableStateOf(false) }
    var showMessagesMenu by remember { mutableStateOf(false) }
    var showPrivacyMenu by remember { mutableStateOf(false) }
    var showLogoutConfirm by remember { mutableStateOf(false) }

    var webViewRef by remember { mutableStateOf<InstagramWebView?>(null) }

    val webViewCallback = remember {
        object : GramWebViewClient {
            override fun onNavigationError(errorStatus: String) {
                offlineErrorDetail = "Check your internet connection and try again. ($errorStatus)"
                showOfflineOverlay = true
            }

            override fun onNavigationSuccess() {
                showOfflineOverlay = false
            }

            override fun onTitleChanged(title: String) {
                // Title relay from web page - could be used for activity title
            }

            override fun onPageFinished() {}
        }
    }

    Scaffold(
        topBar = {
            Surface(
                color = Color(0xFFF5F5F5),
                shadowElevation = 0.dp,
                modifier = Modifier.fillMaxWidth()
            ) {
                Column {
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(40.dp)
                            .padding(horizontal = 4.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        // Messages menu
                        Box {
                            TextButton(
                                onClick = {
                                    showMessagesMenu = !showMessagesMenu
                                    showPrivacyMenu = false
                                },
                                modifier = Modifier.padding(horizontal = 4.dp)
                            ) {
                                Text(
                                    text = "Go",
                                    color = Color.Black,
                                    fontSize = 14.sp
                                )
                            }
                            DropdownMenu(
                                expanded = showMessagesMenu,
                                onDismissRequest = { showMessagesMenu = false },
                                modifier = Modifier.background(Color.White)
                            ) {
                                DropdownMenuItem(
                                    text = { Text("Home feed", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        webViewRef?.navigateHome()
                                    }
                                )
                                DropdownMenuItem(
                                    text = { Text("Open inbox", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        webViewRef?.navigateToInbox()
                                    }
                                )
                                DropdownMenuItem(
                                    text = { Text("New message", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        webViewRef?.navigateToNewMessage()
                                    }
                                )
                                DropdownMenuItem(
                                    text = { Text("Notifications", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        webViewRef?.navigateToActivity()
                                    }
                                )
                                DropdownMenuItem(
                                    text = { Text("Reload", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        webViewRef?.reloadPage()
                                    }
                                )
                                HorizontalDivider(color = Color(0xFFE0E0E0))
                                DropdownMenuItem(
                                    text = { Text("Exit", fontSize = 14.sp) },
                                    onClick = {
                                        showMessagesMenu = false
                                        activity.finish()
                                    }
                                )
                            }
                        }

                        // Privacy menu
                        Box {
                            TextButton(
                                onClick = {
                                    showPrivacyMenu = !showPrivacyMenu
                                    showMessagesMenu = false
                                },
                                modifier = Modifier.padding(horizontal = 4.dp)
                            ) {
                                Text(
                                    text = "Privacy",
                                    color = Color.Black,
                                    fontSize = 14.sp
                                )
                            }
                            DropdownMenu(
                                expanded = showPrivacyMenu,
                                onDismissRequest = { showPrivacyMenu = false },
                                modifier = Modifier.background(Color.White)
                            ) {
                                DropdownMenuItem(
                                    text = { Text("Log out & clear local data\u2026", fontSize = 14.sp) },
                                    onClick = {
                                        showPrivacyMenu = false
                                        showLogoutConfirm = true
                                    }
                                )
                                HorizontalDivider(color = Color(0xFFE0E0E0))
                                DropdownMenuItem(
                                    text = { Text("About GramChat", fontSize = 14.sp) },
                                    onClick = {
                                        showPrivacyMenu = false
                                        showAboutDialog = true
                                    }
                                )
                            }
                        }
                    }
                    HorizontalDivider(color = Color(0xFFE0E0E0), thickness = 1.dp)
                }
            }
        }
    ) { paddingValues ->
        Box(
            modifier = Modifier
                .fillMaxSize()
                .background(Color(0xFFFAFAFA))
                .padding(paddingValues)
        ) {
            AndroidView(
                factory = { ctx ->
                    InstagramWebView(ctx, webViewCallback).also { webView ->
                        webView.layoutParams = ViewGroup.LayoutParams(
                            ViewGroup.LayoutParams.MATCH_PARENT,
                            ViewGroup.LayoutParams.MATCH_PARENT
                        )
                        webView.setup()
                        // Start on the Home feed: GramChat is the full Instagram
                        // experience, with Reels and Explore the only places left out.
                        webView.navigateHome()
                        webViewRef = webView
                    }
                },
                modifier = Modifier.fillMaxSize()
            )

            if (showOfflineOverlay) {
                Box(
                    modifier = Modifier
                        .fillMaxSize()
                        .background(Color(0xF2F7F7F9)),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Text(
                            text = "Can\u2019t reach Instagram",
                            fontSize = 20.sp,
                            fontWeight = FontWeight.SemiBold,
                            color = Color.Black
                        )
                        Text(
                            text = offlineErrorDetail,
                            fontSize = 14.sp,
                            color = Color(0xFF666666),
                            modifier = Modifier.padding(top = 8.dp, bottom = 20.dp)
                        )
                        Button(
                            onClick = { webViewRef?.navigateHome() },
                            colors = ButtonDefaults.buttonColors(containerColor = Color(0xFF1976D2)),
                            modifier = Modifier.padding(horizontal = 32.dp)
                        ) {
                            Text("Retry", color = Color.White)
                        }
                    }
                }
            }
        }
    }

    if (showAboutDialog) {
        AboutDialog(onDismiss = { showAboutDialog = false })
    }

    if (showLogoutConfirm) {
        AlertDialog(
            onDismissRequest = { showLogoutConfirm = false },
            title = { Text("GramChat") },
            text = {
                Text(
                    "Log out and delete all locally stored Instagram data?\n\n" +
                    "This removes the session cookies, cached files and site data from this device. " +
                    "You will need to log in again next time."
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        showLogoutConfirm = false
                        webViewRef?.let { webView ->
                            webView.clearStoredSessions()
                            MainScope().launch {
                                webView.clearAllData()
                                webView.navigateToInbox()
                            }
                        }
                    }
                ) {
                    Text("Yes")
                }
            },
            dismissButton = {
                TextButton(onClick = { showLogoutConfirm = false }) {
                    Text("No")
                }
            }
        )
    }
}
