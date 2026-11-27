package com.minadesk.workstationhub

import android.content.SharedPreferences
import android.os.Handler
import android.os.Looper
import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.concurrent.Executors

/** 보조 위젯의 주간 날씨. 데스크톱 DeveloperWeather 와 같은 Open-Meteo 요청·설명 규칙입니다. */
internal class Weather(private val prefs: SharedPreferences, private val changed: () -> Unit) {
    private val main = Handler(Looper.getMainLooper())
    private val worker = Executors.newSingleThreadExecutor()
    private var current: JSONObject? = null
    private var days: JSONArray? = null
    private var readAt: Long? = null
    private var nextRequest = 0L
    private var status: String? = null
    private var busy = false

    val city: String? get() = prefs.getString("city", null)?.takeIf { CITIES.containsKey(it) }

    fun snapshot(): JSONObject = obj(
        "kind" to "widget-state", "city" to city, "cities" to CITIES.keys.toList(), "weather" to current, "days" to days, "busy" to busy,
        "status" to (status ?: if (city == null) "도시를 선택하면 주간 예보를 조회합니다." else "날씨 새로고침을 눌러 예보를 확인하세요."),
        "updated" to readAt?.let { SimpleDateFormat("MM-dd HH:mm", Locale.KOREA).format(Date(it)) }
    )

    fun choose(name: String?) {
        if (busy) return
        if (!name.isNullOrEmpty() && !CITIES.containsKey(name)) return
        prefs.edit().putString("city", name?.takeIf { it.isNotEmpty() }).apply()
        current = null; days = null; readAt = null; status = null; nextRequest = 0L; changed()
        if (city != null) refresh()
    }

    fun refresh() {
        val name = city ?: return
        if (busy || System.currentTimeMillis() < nextRequest) return
        busy = true; status = "예보 조회 중"; changed()
        worker.execute {
            var latest: JSONObject? = null; var parsed: JSONArray? = null; var failure: Exception? = null
            try {
                val connection = URL(FORECAST + CITIES[name] + FIELDS).openConnection() as HttpURLConnection
                try {
                    connection.connectTimeout = 12000; connection.readTimeout = 12000
                    if (connection.responseCode != 200) throw IllegalStateException("Open-Meteo 응답 " + connection.responseCode)
                    val data = JSONObject(connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() })
                    val now = data.getJSONObject("current")
                    val daily = data.getJSONObject("daily")
                    val dates = daily.getJSONArray("time"); val codes = daily.getJSONArray("weather_code")
                    val highs = daily.getJSONArray("temperature_2m_max"); val lows = daily.getJSONArray("temperature_2m_min")
                    val count = minOf(dates.length(), codes.length(), highs.length(), lows.length(), 7)
                    val list = JSONArray()
                    for (i in 0 until count) list.put(obj("date" to formatDate(dates.optString(i)), "description" to description(codes.opt(i)), "high" to temperature(highs.opt(i)), "low" to temperature(lows.opt(i))))
                    latest = obj("temperature" to temperature(now.opt("temperature_2m")), "description" to description(now.opt("weather_code")))
                    parsed = list
                } finally { connection.disconnect() }
            } catch (e: Exception) { failure = e }
            main.post {
                if (failure == null) {
                    current = latest; days = parsed; readAt = System.currentTimeMillis(); nextRequest = readAt!! + 5 * 60_000; status = "Open-Meteo 예보"
                } else {
                    status = if (readAt != null) "날씨 갱신 실패 · 마지막 예보 표시" else "날씨를 불러오지 못했습니다. 잠시 후 다시 시도하세요."
                    nextRequest = System.currentTimeMillis() + 30_000
                }
                busy = false; changed()
            }
        }
    }

    companion object {
        val CITIES = linkedMapOf(
            "서울" to "latitude=37.5665&longitude=126.9780", "인천" to "latitude=37.4563&longitude=126.7052",
            "수원" to "latitude=37.2636&longitude=127.0286", "대전" to "latitude=36.3504&longitude=127.3845",
            "대구" to "latitude=35.8714&longitude=128.6014", "광주" to "latitude=35.1595&longitude=126.8526",
            "부산" to "latitude=35.1796&longitude=129.0756", "제주" to "latitude=33.4996&longitude=126.5312"
        )
        private const val FORECAST = "https://api.open-meteo.com/v1/forecast?"
        private const val FIELDS = "&current=temperature_2m,weather_code&daily=weather_code,temperature_2m_max,temperature_2m_min&timezone=Asia%2FSeoul&forecast_days=7"

        private fun formatDate(value: String?): String = try {
            val date = SimpleDateFormat("yyyy-MM-dd", Locale.US).parse(value ?: "") ?: return "—"
            SimpleDateFormat("MM.dd (E)", Locale.KOREA).format(date)
        } catch (_: Exception) { "—" }

        private fun temperature(value: Any?): String {
            val number = (value as? Number)?.toDouble() ?: value?.toString()?.toDoubleOrNull() ?: return "—"
            if (number.isNaN() || number.isInfinite()) return "—"
            return Math.round(number).toString() + "°"
        }

        private fun description(value: Any?): String {
            val code = (value as? Number)?.toInt() ?: value?.toString()?.toIntOrNull() ?: return "확인 중"
            return when {
                code == 0 -> "맑음"; code <= 3 -> "구름"; code <= 48 -> "안개"; code >= 95 -> "뇌우"
                (code in 71..77) || code >= 85 -> "눈"; else -> "비"
            }
        }
    }
}
