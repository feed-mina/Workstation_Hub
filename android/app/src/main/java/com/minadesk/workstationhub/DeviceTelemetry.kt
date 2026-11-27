package com.minadesk.workstationhub

import android.app.ActivityManager
import android.content.Context
import android.net.TrafficStats
import android.os.Environment
import android.os.StatFs
import android.os.SystemClock
import org.json.JSONObject
import java.io.File

/**
 * PC 성능 창의 `sample` 메시지를 이 휴대폰의 실측값으로 채웁니다.
 * - CPU: /proc/stat 을 읽을 수 있는 기기에서만 계산합니다(Android 8 이후 대부분 차단되어 '—' 로 표시).
 * - 메모리·저장공간·네트워크: ActivityManager, StatFs, TrafficStats 의 실제 값입니다.
 */
internal class DeviceTelemetry(context: Context) {
    private val activity = context.getSystemService(Context.ACTIVITY_SERVICE) as ActivityManager
    private var lastTotal = 0L
    private var lastIdle = 0L
    private var lastTick = 0L
    private var lastRx = -1L
    private var lastTx = -1L

    fun reset() { lastTotal = 0; lastIdle = 0; lastTick = 0; lastRx = -1; lastTx = -1 }

    fun sample(): JSONObject {
        var cpu: Double? = null
        readCpu()?.let { (total, idle) ->
            if (lastTotal > 0 && total > lastTotal && idle >= lastIdle)
                cpu = (100.0 * (1.0 - (idle - lastIdle).toDouble() / (total - lastTotal))).coerceIn(0.0, 100.0)
            lastTotal = total; lastIdle = idle
        }
        var ram: Double? = null; var usedGb: Double? = null; var totalGb: Double? = null
        try {
            val memory = ActivityManager.MemoryInfo()
            activity.getMemoryInfo(memory)
            if (memory.totalMem > 0) {
                totalGb = memory.totalMem / GIB
                usedGb = (memory.totalMem - memory.availMem) / GIB
                ram = 100.0 * (memory.totalMem - memory.availMem) / memory.totalMem
            }
        } catch (_: Exception) { }
        var driveFree: Double? = null; var driveTotal: Double? = null
        try {
            val stats = StatFs(Environment.getDataDirectory().path)
            driveFree = stats.availableBytes / GIB
            driveTotal = stats.totalBytes / GIB
        } catch (_: Exception) { }
        var down: Double? = null; var up: Double? = null
        val now = SystemClock.elapsedRealtime()
        val rx = TrafficStats.getTotalRxBytes(); val tx = TrafficStats.getTotalTxBytes()
        if (rx != TrafficStats.UNSUPPORTED.toLong() && tx != TrafficStats.UNSUPPORTED.toLong()) {
            val elapsed = (now - lastTick) / 1000.0
            if (lastTick != 0L && lastRx >= 0 && rx >= lastRx && tx >= lastTx && elapsed > 0 && elapsed < 8) {
                down = (rx - lastRx) * 8 / elapsed / 1_000_000
                up = (tx - lastTx) * 8 / elapsed / 1_000_000
            }
            lastRx = rx; lastTx = tx
        }
        lastTick = now
        return obj(
            "kind" to "sample", "time" to System.currentTimeMillis(), "cpu" to cpu, "ram" to ram, "usedGb" to usedGb, "totalGb" to totalGb,
            "driveFree" to driveFree, "driveTotal" to driveTotal, "volume" to "내부 저장공간", "down" to down, "up" to up
        )
    }

    /** GPU 카운터·온도 센서는 일반 Android 앱에 공개되지 않아 상태 문구만 보냅니다. */
    fun gpu(): JSONObject = obj(
        "kind" to "gpu-sample", "load" to null, "dedicated" to null, "shared" to null, "cpuTemperature" to null, "gpuTemperature" to null,
        "status" to "Android 는 앱에 GPU 사용률·온도 센서를 제공하지 않습니다. 메모리·저장공간·네트워크는 이 휴대폰의 실측값입니다."
    )

    private fun readCpu(): Pair<Long, Long>? {
        return try {
            val line = File("/proc/stat").bufferedReader().use { it.readLine() } ?: return null
            val parts = line.trim().split(Regex("\\s+"))
            if (parts.size < 5 || parts[0] != "cpu") return null
            val values = parts.drop(1).mapNotNull { it.toLongOrNull() }
            if (values.size < 4) return null
            val idle = values[3] + (values.getOrNull(4) ?: 0L)
            Pair(values.sum(), idle)
        } catch (_: Exception) { null }
    }

    companion object { private const val GIB = 1073741824.0 }
}
