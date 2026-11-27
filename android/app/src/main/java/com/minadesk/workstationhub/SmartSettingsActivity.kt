package com.minadesk.workstationhub

import android.app.Activity
import android.app.AlertDialog
import android.graphics.Color
import android.os.Bundle
import android.text.InputType
import android.util.TypedValue
import android.view.Gravity
import android.view.ViewGroup
import android.widget.Button
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import java.io.IOException

/**
 * 데스크톱 SmartSettingsDialog 의 집 연결·방과 기기 탭에 해당합니다.
 * HA 주소와 장기 액세스 토큰을 저장하고, 방마다 entity_id 를 연결합니다. 토큰은 Android Keystore 로 보호해 저장합니다.
 */
class SmartSettingsActivity : Activity() {
    private lateinit var hub: Hub
    private lateinit var address: EditText
    private lateinit var token: EditText
    private lateinit var status: TextView
    private lateinit var roomsLayout: LinearLayout
    private val rooms = ArrayList<RoomForm>()

    private class RoomForm(val binding: RoomBinding, val view: LinearLayout, val name: EditText, val fields: Map<String, EditText>)

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        hub = (application as HubApplication).hub
        val settings = hub.smart.settings
        val scroll = ScrollView(this)
        val page = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL; setPadding(dp(16), dp(16), dp(16), dp(24)) }
        scroll.addView(page)
        page.addView(heading("집 연결"))
        page.addView(hint("Home Assistant 주소와 사용자가 만든 장기 액세스 토큰을 입력합니다. 토큰은 이 휴대폰 안에만 보호 저장하며 화면에는 다시 표시하지 않습니다."))
        address = input("http://homeassistant.local:8123", settings.haAddress)
        page.addView(label("Home Assistant 주소")); page.addView(address)
        token = input(if (settings.haTokenProtected.isEmpty()) "장기 액세스 토큰" else "저장된 토큰 유지 · 바꿀 때만 입력", "").apply { inputType = InputType.TYPE_CLASS_TEXT or InputType.TYPE_TEXT_VARIATION_PASSWORD }
        page.addView(label("장기 액세스 토큰")); page.addView(token)
        status = hint(hub.smart.ha.status)
        page.addView(status)
        page.addView(Button(this).apply { text = "연결 확인 · 기기 목록 읽기"; setOnClickListener { test() } })

        page.addView(heading("방과 기기"))
        page.addView(hint("각 방에 HA 의 entity_id 를 연결합니다. 연결 확인을 먼저 누르면 '목록' 버튼으로 기기를 고를 수 있습니다. '이 방 다시 청소' 는 HA 에서 검증한 스크립트를, 음악은 Music Assistant 플레이어와 재생목록 URI 를 사용합니다."))
        roomsLayout = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
        page.addView(roomsLayout)
        for (room in settings.rooms) addRoom(room)
        page.addView(Button(this).apply { text = "방 추가"; setOnClickListener { addRoom(RoomBinding()) } })

        val actions = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.END; setPadding(0, dp(16), 0, 0) }
        actions.addView(Button(this).apply { text = "취소"; setOnClickListener { finish() } })
        actions.addView(Button(this).apply { text = "저장"; setOnClickListener { save() } })
        page.addView(actions)
        setContentView(scroll)
    }

    private fun addRoom(binding: RoomBinding) {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL; setPadding(dp(12), dp(12), dp(12), dp(12))
            setBackgroundColor(Color.parseColor("#1D4639"))
        }
        val params = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT).apply { topMargin = dp(10) }
        val head = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        val name = input("방 이름", binding.name)
        head.addView(name, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
        val remove = Button(this).apply { text = "삭제" }
        head.addView(remove)
        box.addView(head)
        val fields = LinkedHashMap<String, EditText>()
        for ((key, meta) in FIELDS) {
            val (title, prefixes, value) = Triple(meta.first, meta.second, meta.third(binding))
            box.addView(label(title))
            val row = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL }
            val field = input(if (prefixes.isEmpty()) "재생목록 URI (예: library://playlist/1)" else prefixes.joinToString(" · ") { it + "…" }, value ?: "")
            row.addView(field, LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f))
            if (prefixes.isNotEmpty()) row.addView(Button(this).apply { text = "목록"; setOnClickListener { pick(field, prefixes) } })
            box.addView(row)
            fields[key] = field
        }
        val form = RoomForm(binding, box, name, fields)
        remove.setOnClickListener {
            if (rooms.size <= 1) { Toast.makeText(this, "방은 하나 이상 필요합니다.", Toast.LENGTH_SHORT).show(); return@setOnClickListener }
            rooms.remove(form); roomsLayout.removeView(box)
        }
        rooms.add(form)
        roomsLayout.addView(box, params)
    }

    private fun pick(target: EditText, prefixes: List<String>) {
        val ids = hub.smart.entityIds(prefixes)
        if (ids.isEmpty()) { Toast.makeText(this, "먼저 '연결 확인' 으로 기기 목록을 읽으세요. 해당 종류의 기기가 없을 수도 있습니다.", Toast.LENGTH_LONG).show(); return }
        val names = ids.map { id -> val entity = hub.smart.ha.find(id); if (entity != null && entity.name != id) "${entity.name}\n$id" else id }.toTypedArray()
        AlertDialog.Builder(this).setTitle("기기 선택").setItems(names) { _, index -> target.setText(ids[index]) }.setNeutralButton("비우기") { _, _ -> target.setText("") }.setNegativeButton("닫기", null).show()
    }

    /** 입력한 주소·토큰으로 한 번 조회해 상태와 기기 수를 보여 줍니다. 저장은 하지 않습니다. */
    private fun test() {
        val draft = try { draft() } catch (e: IllegalArgumentException) { status.text = e.message; return }
        if (draft.haTokenProtected.isEmpty()) { status.text = "토큰을 입력하세요."; return }
        status.text = "Home Assistant 확인 중…"
        hub.smart.test(draft) { result -> status.text = result }
    }

    @Throws(IllegalArgumentException::class)
    private fun draft(): SmartSettings {
        val current = hub.smart.settings
        val text = address.text.toString().trim()
        HaClient.baseAddress(text)
        val typed = token.text.toString().trim()
        val protectedToken = if (typed.isNotEmpty()) Secrets.protect(typed) else current.haTokenProtected
        val list = rooms.map { form ->
            val clean = { key: String -> form.fields[key]!!.text.toString().trim().takeIf { it.isNotEmpty() } }
            for (key in listOf("vacuum", "cleanScript", "speaker", "window", "dryer")) {
                val value = clean(key)
                if (value != null && !ENTITY.matches(value)) throw IllegalArgumentException("'${form.name.text}' 의 ${FIELDS[key]!!.first} entity_id 형식을 확인하세요: $value")
            }
            RoomBinding(form.binding.id, form.name.text.toString().trim().ifEmpty { "새 방" }.take(24),
                clean("vacuum"), clean("cleanScript"), clean("speaker"), clean("classic"), clean("pop"), clean("window"), clean("dryer"))
        }
        return SmartSettings(text, protectedToken, list.toMutableList())
    }

    private fun save() {
        val next = try { draft() } catch (e: IllegalArgumentException) { Toast.makeText(this, e.message, Toast.LENGTH_LONG).show(); return }
        try { hub.smart.save(next) } catch (e: IOException) { Toast.makeText(this, "설정을 저장하지 못했습니다. " + e.message, Toast.LENGTH_LONG).show(); return }
        Toast.makeText(this, "스마트홈 설정을 저장했습니다.", Toast.LENGTH_SHORT).show()
        finish()
    }

    private fun heading(text: String) = TextView(this).apply { this.text = text; setTextSize(TypedValue.COMPLEX_UNIT_SP, 18f); setTextColor(Color.parseColor("#F5F2DE")); setPadding(0, dp(18), 0, dp(6)) }
    private fun label(text: String) = TextView(this).apply { this.text = text; setTextSize(TypedValue.COMPLEX_UNIT_SP, 12f); setTextColor(Color.parseColor("#B5CDBB")); setPadding(0, dp(8), 0, dp(2)) }
    private fun hint(text: String) = TextView(this).apply { this.text = text; setTextSize(TypedValue.COMPLEX_UNIT_SP, 13f); setTextColor(Color.parseColor("#D7ECBF")); setPadding(0, dp(4), 0, dp(8)) }
    private fun input(placeholder: String, value: String) = EditText(this).apply { hint = placeholder; setText(value); setSingleLine(true); setTextColor(Color.parseColor("#F5F2DE")); setHintTextColor(Color.parseColor("#7F9C88")) }
    private fun dp(value: Int): Int = (value * resources.displayMetrics.density + 0.5f).toInt()

    companion object {
        private val ENTITY = Regex("^[a-z_]+\\.[a-z0-9_]+$")
        /** 표시 이름, 목록 버튼의 도메인 접두어, 현재 값 읽기 */
        private val FIELDS: Map<String, Triple<String, List<String>, (RoomBinding) -> String?>> = linkedMapOf(
            "vacuum" to Triple("청소기", listOf("vacuum."), { r: RoomBinding -> r.vacuum }),
            "cleanScript" to Triple("이 방 청소 스크립트", listOf("script."), { r: RoomBinding -> r.cleanScript }),
            "speaker" to Triple("스피커 (Music Assistant 플레이어)", listOf("media_player."), { r: RoomBinding -> r.speaker }),
            "classic" to Triple("클래식 재생목록 URI", emptyList(), { r: RoomBinding -> r.classic }),
            "pop" to Triple("인기가요 재생목록 URI", emptyList(), { r: RoomBinding -> r.pop }),
            "window" to Triple("창문 센서", listOf("binary_sensor.", "sensor."), { r: RoomBinding -> r.window }),
            "dryer" to Triple("건조기", listOf("sensor.", "binary_sensor.", "switch."), { r: RoomBinding -> r.dryer })
        )
    }
}
