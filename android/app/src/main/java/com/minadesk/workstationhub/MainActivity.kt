package com.minadesk.workstationhub

import android.app.Activity
import android.content.Intent
import android.graphics.Color
import android.graphics.Typeface
import android.os.Bundle
import android.os.Build
import android.util.TypedValue
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.FrameLayout
import android.widget.LinearLayout
import android.widget.TextView
import android.widget.Button
import android.window.OnBackInvokedCallback
import android.window.OnBackInvokedDispatcher

/**
 * 데스크톱의 여섯 창을 하단 탭 여섯 개로 보여 줍니다. 각 탭은 같은 desk.html 을 `?panel=키` 로 띄운 WebView 이며,
 * 처음 선택할 때 만들고 전환해도 파괴하지 않아 화면 상태(선택한 방·입력 중인 값)를 유지합니다.
 */
class MainActivity : Activity(), HubHost {
    private lateinit var hub: Hub
    private lateinit var root: LinearLayout
    private lateinit var container: FrameLayout
    private lateinit var tabs: LinearLayout
    private val panels = HashMap<String, PanelView>()
    private val tabViews = HashMap<String, TextView>()
    private var current: String? = null
    private var backCallback: OnBackInvokedCallback? = null

    override val activity: Activity get() = this

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        hub = (application as HubApplication).hub
        root = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        val appBar = LinearLayout(this).apply { gravity = Gravity.CENTER_VERTICAL; setPadding(dp(12), 0, dp(8), 0) }
        appBar.addView(TextView(this).apply { text = "Mini Workstation Hub"; setTextColor(Color.WHITE); textSize = 14f }, LinearLayout.LayoutParams(0, dp(48), 1f))
        appBar.addView(Button(this).apply {
            text = "앱 정보"; contentDescription = "앱 정보 및 개인정보처리방침"
            setOnClickListener { startActivity(Intent(this@MainActivity, AboutActivity::class.java)) }
        }, LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(48)))
        root.addView(appBar)
        container = FrameLayout(this)
        root.addView(container, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f))
        tabs = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
        root.addView(tabs, LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        for ((index, key) in Hub.KEYS.withIndex()) {
            val tab = TextView(this).apply {
                text = Hub.TITLES[index]; gravity = Gravity.CENTER
                setTextSize(TypedValue.COMPLEX_UNIT_SP, 12.5f)
                setPadding(dp(4), dp(12), dp(4), dp(12))
                isClickable = true; isFocusable = true
                contentDescription = Hub.TITLES[index] + " 탭"
                setOnClickListener { showPanel(key) }
            }
            tabViews[key] = tab
            tabs.addView(tab, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        }
        setContentView(root)
        root.applyHubInsets()
        applyTheme(hub.theme)
        showPanel(savedInstanceState?.getString("tab") ?: getPreferences(MODE_PRIVATE).getString("tab", "macro") ?: "macro")
        if (Build.VERSION.SDK_INT >= 33) {
            backCallback = OnBackInvokedCallback { handleBack() }.also {
                onBackInvokedDispatcher.registerOnBackInvokedCallback(OnBackInvokedDispatcher.PRIORITY_DEFAULT, it)
            }
        }
    }

    override fun onSaveInstanceState(outState: Bundle) { super.onSaveInstanceState(outState); current?.let { outState.putString("tab", it) } }

    override fun onResume() {
        super.onResume()
        panels.values.firstOrNull()?.resumeTimers()
        hub.host = this
        hub.visible = current
        for (panel in panels.values) panel.onResume()
    }

    override fun onPause() {
        hub.visible = null
        hub.host = null
        panels.values.firstOrNull()?.pauseTimers()
        for (panel in panels.values) panel.onPause()
        super.onPause()
    }

    override fun onDestroy() {
        if (Build.VERSION.SDK_INT >= 33) backCallback?.let { onBackInvokedDispatcher.unregisterOnBackInvokedCallback(it) }
        backCallback = null
        for ((key, panel) in panels) { hub.detach(key); (panel.parent as? ViewGroup)?.removeView(panel); panel.destroy() }
        panels.clear()
        super.onDestroy()
    }

    override fun showPanel(key: String) {
        if (key !in Hub.KEYS) return
        var panel = panels[key]
        if (panel == null) {
            panel = PanelView(this, key, hub)
            panels[key] = panel
            hub.attach(panel)
            container.addView(panel, FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT))
        }
        for ((other, view) in panels) view.visibility = if (other == key) View.VISIBLE else View.GONE
        current = key
        getPreferences(MODE_PRIVATE).edit().putString("tab", key).apply()
        hub.visible = key
        paintTabs()
    }

    override fun openSmartSettings() { startActivity(Intent(this, SmartSettingsActivity::class.java)) }

    override fun applyTheme(theme: String) {
        val neon = theme == "neon"
        val bar = if (neon) Color.parseColor("#102943") else Color.parseColor("#173B32")
        root.setBackgroundColor(bar)
        tabs.setBackgroundColor(bar)
        window.statusBarColor = bar
        window.navigationBarColor = bar
        paintTabs()
    }

    private fun paintTabs() {
        val neon = hub.theme == "neon"
        val lime = if (neon) Color.parseColor("#69EBFF") else Color.parseColor("#B8F392")
        val ink = Color.parseColor("#F5F2DE")
        for ((key, tab) in tabViews) {
            val selected = key == current
            tab.setTextColor(if (selected) lime else ink)
            tab.typeface = if (selected) Typeface.DEFAULT_BOLD else Typeface.DEFAULT
            tab.alpha = if (selected) 1f else 0.78f
            tab.isSelected = selected
        }
    }

    @Deprecated("Deprecated in Java")
    override fun onBackPressed() {
        handleBack()
    }

    private fun handleBack() {
        val panel = current?.let { panels[it] }
        if (panel == null) { moveTaskToBack(true); return }
        panel.closeDialog { closed -> if (!closed && !isFinishing && !isDestroyed) moveTaskToBack(true) }
    }

    private fun dp(value: Int): Int = (value * resources.displayMetrics.density + 0.5f).toInt()
}
