package com.minadesk.workstationhub

import android.app.Activity
import android.content.ActivityNotFoundException
import android.content.Intent
import android.graphics.Color
import android.os.Bundle
import android.net.Uri
import android.view.ViewGroup
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import org.json.JSONObject

/** An offline notice stays available even without a browser or network connection. */
class AboutActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val scroll = ScrollView(this)
        val page = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(dp(20), dp(20), dp(20), dp(28))
        }
        scroll.addView(page)
        fun paragraph(text: String, heading: Boolean = false) {
            page.addView(TextView(this).apply {
                this.text = text; textSize = if (heading) 20f else 16f
                setTextColor(Color.parseColor("#F5F2DE"))
                setPadding(0, if (heading) dp(18) else dp(8), 0, dp(8))
                setTextIsSelectable(true)
            })
        }
        paragraph("Mini Workstation Hub", true)
        paragraph("버전 ${BuildConfig.VERSION_NAME} · Android")
        fun button(label: String, url: String) {
            page.addView(Button(this).apply { text = label; setOnClickListener { open(url) } },
                LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT))
        }
        button("공개 개인정보처리방침", BuildConfig.PRIVACY_POLICY_URL)
        button("지원 문의", BuildConfig.SUPPORT_URL)
        val notice = JSONObject(assets.open("privacy-android.json").bufferedReader(Charsets.UTF_8).use { it.readText() })
        paragraph(notice.getString("title"), true)
        paragraph("시행일: ${notice.getString("effective_date")}")
        val sections = notice.getJSONArray("sections")
        for (index in 0 until sections.length()) {
            val section = sections.getJSONObject(index)
            paragraph(section.getString("heading"), true)
            paragraph(section.getString("body"))
        }
        button("Open-Meteo 날씨 데이터 안내", "https://open-meteo.com/")
        page.addView(Button(this).apply { text = "닫기"; setOnClickListener { finish() } })
        setContentView(scroll)
        scroll.applyHubInsets()
    }

    private fun open(url: String) {
        try { startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }
        catch (_: ActivityNotFoundException) { Toast.makeText(this, "링크를 열 브라우저를 찾을 수 없습니다.", Toast.LENGTH_LONG).show() }
    }

    private fun dp(value: Int) = (value * resources.displayMetrics.density + 0.5f).toInt()
}
