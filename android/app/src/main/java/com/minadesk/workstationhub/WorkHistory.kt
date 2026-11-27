package com.minadesk.workstationhub

import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.UUID
import java.util.concurrent.Executor
import java.util.concurrent.Executors

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
internal class WorkHistory(directory: File, private val write: (File, Any) -> Unit = LocalData::write) {
    private val file = File(directory, "work-history.json")
    private val entries = ArrayList<WorkEntry>()

    init {
        val stored = LocalData.readArray(file)
        for (index in 0 until stored.length()) stored.optJSONObject(index)?.let { WorkEntry.from(it) }?.let(entries::add)
    }

    fun snapshot(): List<WorkEntry> = entries.toList()

    @Throws(IOException::class)
    fun add(entry: WorkEntry) {
        entries.add(entry)
        while (entries.size > RETENTION) entries.removeAt(0)
        val json = JSONArray()
        for (entry in entries) json.put(entry.toJson())
        write(file, json)
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

internal data class HistorySnapshot(val rows: List<WorkEntry> = emptyList(), val status: String = "기록을 읽고 있습니다.")

/** File reads, full-file writes and recent-row sorting all stay on one ordered worker. */
internal class WorkHistoryStore(
    directory: File,
    device: String,
    private val ui: UiScheduler = AndroidUiScheduler(),
    private val worker: Executor = Executors.newSingleThreadExecutor(),
    private val open: (File) -> WorkHistory = { WorkHistory(it) },
    private val changed: () -> Unit
) {
    private val deviceName = device.trim().ifEmpty { "휴대폰" }.take(24)
    private var store: WorkHistory? = null // Owned only by worker.
    @Volatile var snapshot = HistorySnapshot()
        private set

    init {
        worker.execute {
            try {
                store = open(directory)
                publish("이 휴대폰")
            } catch (_: Exception) { publish("작업 기록 파일을 열 수 없습니다.") }
        }
    }

    fun add(source: String, detail: String) {
        val entry = WorkEntry(UUID.randomUUID().toString().replace("-", ""), System.currentTimeMillis(), deviceName, source, detail)
        worker.execute {
            val history = store ?: return@execute
            try { history.add(entry); publish("이 휴대폰") }
            catch (_: Exception) { publish("이 휴대폰에 기록을 저장하지 못했습니다.") }
        }
    }

    private fun publish(status: String) {
        val next = HistorySnapshot(WorkHistory.recent(store?.snapshot() ?: emptyList()), status)
        ui.post { snapshot = next; changed() }
    }
}
