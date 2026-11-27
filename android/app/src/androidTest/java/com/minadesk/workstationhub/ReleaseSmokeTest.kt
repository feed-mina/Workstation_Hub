package com.minadesk.workstationhub

import android.app.Activity
import android.content.Intent
import android.content.ContentValues
import android.graphics.Bitmap
import android.graphics.Rect
import android.net.Uri
import android.os.Build
import android.os.ParcelFileDescriptor
import android.os.SystemClock
import android.provider.MediaStore
import android.view.View
import android.view.ViewGroup
import android.webkit.WebView
import android.widget.TextView
import androidx.test.ext.junit.runners.AndroidJUnit4
import androidx.test.platform.app.InstrumentationRegistry
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import java.io.File
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit

@RunWith(AndroidJUnit4::class)
class ReleaseSmokeTest {
    private val instrumentation = InstrumentationRegistry.getInstrumentation()

    private fun start(type: Class<out Activity>): Activity = instrumentation.startActivitySync(
        Intent(instrumentation.targetContext, type).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
    )

    private fun views(view: View): List<View> = listOf(view) + if (view is ViewGroup)
        (0 until view.childCount).flatMap { views(view.getChildAt(it)) } else emptyList()

    private fun js(web: WebView, code: String): String {
        val latch = CountDownLatch(1); var result = ""
        instrumentation.runOnMainSync { web.evaluateJavascript(code) { result = it; latch.countDown() } }
        assertTrue("WebView did not answer", latch.await(10, TimeUnit.SECONDS))
        return result
    }

    private fun until(message: String, predicate: () -> Boolean) {
        val deadline = SystemClock.uptimeMillis() + 20_000
        while (!predicate()) {
            if (SystemClock.uptimeMillis() >= deadline) fail(message)
            SystemClock.sleep(100)
        }
    }

    private fun visibleWeb(activity: Activity): WebView {
        var web: WebView? = null
        instrumentation.runOnMainSync { web = views(activity.window.decorView).filterIsInstance<WebView>().first { it.visibility == View.VISIBLE } }
        until("Panel did not finish booting") {
            js(web!!, "document.readyState === 'complete' && typeof window.__hubListenerCount === 'function' && window.__hubListenerCount() > 0 && !document.getElementById(new URLSearchParams(location.search).get('panel')).hidden") == "true"
        }
        val drawn = CountDownLatch(1)
        instrumentation.runOnMainSync { web!!.postVisualStateCallback(1L, object : WebView.VisualStateCallback() {
            override fun onComplete(requestId: Long) { drawn.countDown() }
        }) }
        assertTrue("WebView did not commit its display", drawn.await(10, TimeUnit.SECONDS))
        return web!!
    }

    private fun screenshot(name: String) {
        instrumentation.waitForIdleSync()
        // Let the native window/compositor publish the committed WebView or Activity frame.
        SystemClock.sleep(350)
        val bitmap = instrumentation.uiAutomation.takeScreenshot()
        assertNotNull(bitmap)
        // Gradle uninstalls the test app after the run. Keep test-only captures in shared media.
        if (Build.VERSION.SDK_INT >= 29) {
            val resolver = instrumentation.targetContext.contentResolver
            val values = ContentValues().apply {
                put(MediaStore.Images.Media.DISPLAY_NAME, "${name}-api${Build.VERSION.SDK_INT}.png")
                put(MediaStore.Images.Media.MIME_TYPE, "image/png")
                put(MediaStore.Images.Media.RELATIVE_PATH, "Pictures/WorkstationHubSmoke")
                put(MediaStore.Images.Media.IS_PENDING, 1)
            }
            val uri = requireNotNull(resolver.insert(MediaStore.Images.Media.EXTERNAL_CONTENT_URI, values))
            requireNotNull(resolver.openOutputStream(uri)).use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
            values.clear(); values.put(MediaStore.Images.Media.IS_PENDING, 0)
            resolver.update(uri, values, null, null)
        } else {
            val directory = File(instrumentation.targetContext.cacheDir, "release-smoke").apply { mkdirs() }
            File(directory, "${name}-api${Build.VERSION.SDK_INT}.png").outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        }
        bitmap.recycle()
    }

    @Test fun everyTabLoadsWithoutHorizontalOverflow() {
        val activity = start(MainActivity::class.java) as MainActivity
        try {
            for (key in listOf("macro", "pc", "home", "log", "dev", "widgets")) {
                instrumentation.runOnMainSync { activity.showPanel(key) }
                val web = visibleWeb(activity)
                assertEquals("$key overflows", "true", js(web, "document.documentElement.scrollWidth <= innerWidth"))
                if (key == "macro" || key == "home") screenshot(key)
            }
        } finally { instrumentation.runOnMainSync { activity.finish() } }
    }

    @Test fun systemBackClosesWebDialogBeforeLeavingApp() {
        val activity = start(MainActivity::class.java) as MainActivity
        try {
            val web = visibleWeb(activity)
            assertEquals("true", js(web, "(function(){var d=document.getElementById('editor');d.showModal();return d.open;})()"))
            ParcelFileDescriptor.AutoCloseInputStream(instrumentation.uiAutomation.executeShellCommand("input keyevent KEYCODE_BACK")).use { it.readBytes() }
            until("System back did not close the dialog") { js(web, "!document.getElementById('editor').open") == "true" }
            assertFalse(activity.isFinishing)
        } finally { instrumentation.runOnMainSync { activity.finish() } }
    }

    @Test fun aboutScreenOpensFromMainAndContainsOfflineNotice() {
        val activity = start(MainActivity::class.java)
        val monitor = instrumentation.addMonitor(AboutActivity::class.java.name, null, false)
        try {
            instrumentation.runOnMainSync {
                views(activity.window.decorView).filterIsInstance<TextView>().first { it.text.toString() == "앱 정보" }.performClick()
            }
            val about = monitor.waitForActivityWithTimeout(10_000)
            assertNotNull("About screen did not open", about)
            try {
                instrumentation.runOnMainSync {
                    val texts = views(about.window.decorView).filterIsInstance<TextView>().map { it.text.toString() }
                    assertTrue(texts.contains("Android 개인정보처리방침"))
                    assertTrue(texts.any { it.contains("버전 ${BuildConfig.VERSION_NAME}") })
                    assertTrue(texts.contains("보관·삭제와 선택권"))
                    assertTrue(texts.contains("공개 개인정보처리방침"))
                    assertEquals("https", Uri.parse(BuildConfig.PRIVACY_POLICY_URL).scheme)
                }
                screenshot("privacy")
            } finally { instrumentation.runOnMainSync { about.finish() } }
        } finally {
            instrumentation.removeMonitor(monitor)
            instrumentation.runOnMainSync { activity.finish() }
        }
    }

    @Test fun settingsContentStaysBelowStatusBar() {
        val activity = start(SmartSettingsActivity::class.java)
        try {
            instrumentation.waitForIdleSync()
            instrumentation.runOnMainSync {
                val content = activity.findViewById<ViewGroup>(android.R.id.content).getChildAt(0)
                val rect = Rect(); assertTrue(content.getGlobalVisibleRect(rect))
                val insets = activity.window.decorView.rootWindowInsets
                @Suppress("DEPRECATION")
                val top = if (Build.VERSION.SDK_INT >= 30) insets.getInsets(android.view.WindowInsets.Type.statusBars()).top else insets.systemWindowInsetTop
                assertTrue("Status bar overlaps content", rect.top + content.paddingTop >= top)
            }
            screenshot("settings")
        } finally { instrumentation.runOnMainSync { activity.finish() } }
    }
}
