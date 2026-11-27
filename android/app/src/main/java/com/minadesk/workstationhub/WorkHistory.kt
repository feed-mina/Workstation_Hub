package com.minadesk.workstationhub

import android.os.Build
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID

internal class WorkEntry(val id: String, val at: Long, val device: String, val source: String, val detail: String) {
    fun toJson(): JSONObject = obj("Id" to id, "AtMillis" to at, "Pc" to device, "Source" to source, "Detail" to detail)

    companion object {
        fun from(json: JSONObject): WorkEntry? {
            val id = json.text("Id") ?: return null
            return WorkEntry(id, json.optLong("AtMillis", 0L), json.text("Pc") ?: "휴대폰", json.text("Source") ?: "", json.text("Detail") ?: "")
        }
    }
}

/** 데스크톱 WorkHistory 와 같은 보존 규칙(5000개)으로 이 기기의 기록을 `work-history.json` 에 저장합니다. 공유 폴더 동기화는 Windows 전용입니다. */
internal class WorkHistory(directory: File) {
    private val file = File(directory, "work-history.json")
    private val entries = ArrayList<WorkEntry>()
    val deviceName: String = (Build.MODEL ?: "휴대폰").trim().ifEmpty { "휴대폰" }.take(24)

    init {
        val stored = LocalData.readArray(file)
        for (index in 0 until stored.length()) stored.optJSONObject(index)?.let { WorkEntry.from(it) }?.let(entries::add)
    }

    fun snapshot(): List<WorkEntry> = entries.toList()

    @Throws(IOException::class)
    fun add(source: String, detail: String) {
        entries.add(WorkEntry(UUID.randomUUID().toString().replace("-", ""), System.currentTimeMillis(), deviceName, source, detail))
        while (entries.size > RETENTION) entries.removeAt(0)
        val json = JSONArray()
        for (entry in entries) json.put(entry.toJson())
        LocalData.write(file, json)
    }

    companion object {
        const val RETENTION = 5000
        private val stamp = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.KOREA)

        /** 화면에는 최근 60개만, 오래된 것부터 보냅니다(화면이 prepend 하므로 최신이 위에 옵니다). */
        fun recent(rows: List<WorkEntry>): List<WorkEntry> =
            rows.distinctBy { it.id }.sortedWith(compareByDescending<WorkEntry> { it.at }.thenByDescending { it.id }).take(60).reversed()

        fun time(entry: WorkEntry): String = stamp.format(Date(entry.at))
    }
}
