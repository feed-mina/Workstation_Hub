package com.minadesk.workstationhub

import android.annotation.SuppressLint
import android.content.Context
import android.content.pm.ApplicationInfo
import android.graphics.Color
import android.net.Uri
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.webkit.ConsoleMessage
import android.webkit.JavascriptInterface
import android.webkit.PermissionRequest
import android.webkit.WebChromeClient
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebView
import android.webkit.WebViewClient
import org.json.JSONObject
import java.io.ByteArrayInputStream
import java.io.FileNotFoundException

/**
 * 데스크톱 DeskWindow 의 휴대폰판. 창 하나 대신 탭 하나이며, 같은 `desk.html?panel=키` 를
 * `https://workstation-hub.invalid/` 가상 호스트에서 assets 로 서빙합니다.
 * - 다른 주소로의 이동·새 창·다운로드·권한 요청은 모두 막습니다(데스크톱과 동일).
 * - desk.html 에는 hub-bridge.js(WebView2 호환 브리지)와 mobile.css 를 끼워 넣습니다.
 */
@SuppressLint("SetJavaScriptEnabled")
internal class PanelView(context: Context, val key: String, private val hub: Hub) : WebView(context) {
    var ready = false
        private set
    private val main = Handler(Looper.getMainLooper())
    private val address = "https://$HOST/desk.html?panel=$key"

    init {
        setBackgroundColor(Color.TRANSPARENT)
        overScrollMode = OVER_SCROLL_NEVER
        with(settings) {
            javaScriptEnabled = true
            domStorageEnabled = true // 키 이름·선택한 방 등 화면 쪽 localStorage
            allowFileAccess = false
            allowContentAccess = false
            setSupportMultipleWindows(false)
            setSupportZoom(false)
            builtInZoomControls = false
            displayZoomControls = false
            mediaPlaybackRequiresUserGesture = true
            setGeolocationEnabled(false)
            cacheMode = android.webkit.WebSettings.LOAD_NO_CACHE
        }
        if ((context.applicationInfo.flags and ApplicationInfo.FLAG_DEBUGGABLE) != 0) setWebContentsDebuggingEnabled(true)
        addJavascriptInterface(Bridge(), "AndroidHub")
        webViewClient = object : WebViewClient() {
            override fun shouldOverrideUrlLoading(view: WebView, request: WebResourceRequest): Boolean = request.url.toString() != address
            override fun shouldInterceptRequest(view: WebView, request: WebResourceRequest): WebResourceResponse? = serve(request.url)
            override fun onPageStarted(view: WebView, url: String?, favicon: android.graphics.Bitmap?) { ready = false }
            override fun onReceivedError(view: WebView, request: WebResourceRequest, error: android.webkit.WebResourceError) {
                if (request.isForMainFrame) Log.e(TAG, "$key 화면을 열 수 없습니다: ${error.description}")
            }
        }
        webChromeClient = object : WebChromeClient() {
            override fun onConsoleMessage(message: ConsoleMessage): Boolean {
                Log.d(TAG, "[$key] ${message.message()} (${message.sourceId()}:${message.lineNumber()})"); return true
            }
            override fun onPermissionRequest(request: PermissionRequest) { request.deny() }
            override fun onCreateWindow(view: WebView, isDialog: Boolean, isUserGesture: Boolean, resultMsg: android.os.Message?): Boolean = false
        }
        loadUrl(address)
    }

    /** 허브 → 화면. 데스크톱 PostWebMessageAsJson 과 같은 역할. */
    fun send(message: JSONObject) {
        if (!ready) return
        evaluateJavascript("window.__hubDeliver && window.__hubDeliver(" + JSONObject.quote(message.toString()) + ")", null)
    }

    /** 열린 <dialog> 가 있으면 닫고 true. 뒤로 가기 처리에 사용합니다. */
    fun closeDialog(done: (Boolean) -> Unit) {
        evaluateJavascript("(function(){var d=document.querySelector('dialog[open]');if(!d)return false;d.close();return true;})()") { done(it == "true") }
    }

    fun reopen() { ready = false; loadUrl(address) }

    private inner class Bridge {
        @JavascriptInterface
        fun postMessage(json: String?) {
            if (json == null || json.length > 16384) return
            main.post {
                if (json.contains("\"type\":\"ready\"")) {
                    val message = try { JSONObject(json) } catch (_: Exception) { null }
                    if (message?.text("type") == "ready") ready = true
                }
                hub.receive(this@PanelView, json)
            }
        }
    }

    private fun serve(uri: Uri): WebResourceResponse? {
        if (uri.host != HOST) return WebResourceResponse("text/plain", "utf-8", 403, "Blocked", emptyMap(), ByteArrayInputStream(ByteArray(0)))
        val path = (uri.path ?: "/").trimStart('/').ifEmpty { "desk.html" }
        if (path.contains("..")) return notFound()
        return try {
            if (path == "desk.html") {
                val html = context.assets.open(path).bufferedReader(Charsets.UTF_8).use { it.readText() }
                WebResourceResponse("text/html", "utf-8", ByteArrayInputStream(inject(html).toByteArray(Charsets.UTF_8)))
            } else {
                val stream = context.assets.open(path)
                val type = mime(path)
                if (type.startsWith("text/") || type == "application/json") WebResourceResponse(type, "utf-8", stream) else WebResourceResponse(type, null, stream)
            }
        } catch (_: FileNotFoundException) { notFound() }
    }

    private fun notFound() = WebResourceResponse("text/plain", "utf-8", 404, "Not Found", emptyMap(), ByteArrayInputStream(ByteArray(0)))

    companion object {
        const val HOST = "workstation-hub.invalid"
        private const val TAG = "WorkstationHub"
        private const val BRIDGE_TAG = "<script src=\"hub-bridge.js\"></script>"
        private const val STYLE_TAG = "<link rel=\"stylesheet\" href=\"mobile.css\">"

        /** 데스크톱 화면 파일에 브리지와 휴대폰 스타일을 끼워 넣습니다. 표식이 없어도 동작하도록 두 번째 위치를 준비합니다. */
        fun inject(html: String): String {
            var result = html
            val gsap = "<script src=\"vendor/gsap.min.js\">"
            result = if (result.contains(gsap)) result.replaceFirst(gsap, BRIDGE_TAG + gsap)
                else result.replaceFirst("<script", "$BRIDGE_TAG<script")
            val head = "</head>"
            result = if (result.contains(head)) result.replaceFirst(head, STYLE_TAG + head) else result + STYLE_TAG
            return result
        }

        fun mime(path: String): String = when (path.substringAfterLast('.', "").lowercase()) {
            "html" -> "text/html"; "css" -> "text/css"; "js", "mjs", "cjs" -> "text/javascript"; "json" -> "application/json"
            "webp" -> "image/webp"; "png" -> "image/png"; "jpg", "jpeg" -> "image/jpeg"; "svg" -> "image/svg+xml"; "ico" -> "image/x-icon"
            "woff2" -> "font/woff2"; "woff" -> "font/woff"; "txt", "md" -> "text/plain"; else -> "application/octet-stream"
        }
    }
}
