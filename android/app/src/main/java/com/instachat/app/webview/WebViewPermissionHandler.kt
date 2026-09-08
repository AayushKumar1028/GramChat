package com.instachat.app.webview

import android.Manifest
import android.app.Activity
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.webkit.PermissionRequest
import android.webkit.WebChromeClient
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

/**
 * WebChromeClient that handles WebRTC permission requests (camera, microphone)
 * for voice/video calls through Instagram's web interface.
 */
class WebViewPermissionHandler(
    private val activity: Activity
) : WebChromeClient() {

    private var pendingPermissionRequest: PermissionRequest? = null

    companion object {
        private const val PERMISSION_REQUEST_CODE = 1001
    }

    override fun onPermissionRequest(request: PermissionRequest) {
        val resources = request.resources
        val neededPermissions = mutableListOf<String>()

        for (resource in resources) {
            when (resource) {
                PermissionRequest.RESOURCE_VIDEO_CAPTURE -> {
                    if (ContextCompat.checkSelfPermission(activity, Manifest.permission.CAMERA)
                        != PackageManager.PERMISSION_GRANTED
                    ) {
                        neededPermissions.add(Manifest.permission.CAMERA)
                    }
                }
                PermissionRequest.RESOURCE_AUDIO_CAPTURE -> {
                    if (ContextCompat.checkSelfPermission(activity, Manifest.permission.RECORD_AUDIO)
                        != PackageManager.PERMISSION_GRANTED
                    ) {
                        neededPermissions.add(Manifest.permission.RECORD_AUDIO)
                    }
                }
            }
        }

        if (neededPermissions.isEmpty()) {
            // All permissions already granted
            request.grant(resources)
            return
        }

        pendingPermissionRequest = request
        ActivityCompat.requestPermissions(
            activity,
            neededPermissions.toTypedArray(),
            PERMISSION_REQUEST_CODE
        )
    }

    /**
     * Call this from Activity.onRequestPermissionsResult to handle the result.
     */
    fun handlePermissionResult(
        requestCode: Int,
        permissions: Array<out String>,
        grantResults: IntArray
    ): Boolean {
        if (requestCode != PERMISSION_REQUEST_CODE) return false

        val request = pendingPermissionRequest ?: return true
        pendingPermissionRequest = null

        val allGranted = grantResults.isNotEmpty() && grantResults.all {
            it == PackageManager.PERMISSION_GRANTED
        }

        if (allGranted) {
            request.grant(request.resources)
        } else {
            request.deny()
        }

        return true
    }

    override fun onPermissionRequestCanceled(request: PermissionRequest) {
        pendingPermissionRequest = null
        super.onPermissionRequestCanceled(request)
    }
}
