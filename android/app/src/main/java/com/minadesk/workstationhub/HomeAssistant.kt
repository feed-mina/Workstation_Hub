package com.minadesk.workstationhub

import android.os.Handler
import android.os.Looper
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.net.HttpURLConnection
import java.net.URI
import java.net.URL
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale
import java.util.TimeZone
import java.util.UUID
import java.util.concurrent.Executors

/** 데스크톱 RoomBinding 과 같은 방·기기 연결. 카메라·영상 보관 항목은 휴대폰에서 쓰지 않습니다. */
internal class RoomBinding(
    var id: String = UUID.randomUUID().toString().replace("-", ""),
    var name: String = "새 방",
    var vacuum: String? = null, var cleanScript: String? = null, var speaker: String? = null,
    var classic: String? = null, var pop: String? = null, var window: String? = null, var dryer: String? = null
) {
    fun toJson(): JSONObject = obj(
        "Id" to id, "Name" to name, "Vacuum" to vacuum, "CleanScript" to cleanScript, "Speaker" to speaker,
        "Classic" to classic, "Pop" to pop, "Window" to window, "Dryer" to dryer
    )

    companion object {
        fun from(json: JSONObject) = RoomBinding(
            json.text("Id") ?: UUID.randomUUID().toString().replace("-", ""), json.text("Name") ?: "새 방",
            json.text("Vacuum"), json.text("CleanScript"), json.text("Speaker"), json.text("Classic"), json.text("Pop"), json.text("Window"), json.text("Dryer")
        )
    }
}

internal class SmartSettings(
    var haAddress: String = "http://homeassistant.local:8123",
    var haTokenProtected: String = "",
    var rooms: MutableList<RoomBinding> = mutableListOf(RoomBinding(name = "내 방"), RoomBinding(name = "침실"), RoomBinding(name = "거실"))
) {
    fun toJson(): JSONObject = obj("HaAddress" to haAddress, "HaTokenProtected" to haTokenProtected, "Rooms" to rooms.map { it.toJson() })

    companion object {
        fun from(json: JSONObject): SmartSettings {
            val rooms = ArrayList<RoomBinding>()
            val list = json.optJSONArray("Rooms") ?: JSONArray()
            for (index in 0 until list.length()) list.optJSONObject(index)?.let { rooms.add(RoomBinding.from(it)) }
            if (rooms.isEmpty()) rooms.add(RoomBinding(name = "내 방"))
            return SmartSettings(json.text("HaAddress") ?: "http://homeassistant.local:8123", json.text("HaTokenProtected") ?: "", rooms)
        }
    }
}

internal class HaEntity(val id: String, val name: String, val state: String, val features: Int) {
    val available: Boolean get() = state != "unavailable" && state != "unknown"
}

/** Home Assistant REST 클라이언트. 데스크톱 HomeAssistantClient 와 같은 검사(지원 기능 비트·서비스 목록·20초 신선도)를 적용합니다. */
internal class HaClient {
    @Volatile var entities: Map<String, HaEntity> = emptyMap()
    @Volatile var services: Set<String> = emptySet()
    @Volatile var status: String = "Home Assistant 연결 전"
    @Volatile var updatedUtc: Long? = null

    companion object {
        fun baseAddress(address: String?): URL {
            val uri = try { URI(address?.trim() ?: "") } catch (_: Exception) { null }
            if (uri == null || !uri.isAbsolute || (uri.scheme != "http" && uri.scheme != "https") || uri.host.isNullOrEmpty() ||
                uri.userInfo != null || uri.rawQuery != null || uri.rawFragment != null)
                throw IllegalArgumentException("Home Assistant 주소를 http://집주소:8123 형식으로 입력하세요.")
            return URL(address!!.trim().trimEnd('/') + "/")
        }
        private val entityPattern = Regex("^[a-z_]+\\.[a-z0-9_]+$")
    }

    @Throws(IOException::class)
    fun request(settings: SmartSettings, path: String, data: JSONObject?): Any? {
        val token = Secrets.unprotect(settings.haTokenProtected)
        if (token.isEmpty()) throw IllegalStateException("설정에서 Home Assistant 토큰을 입력하세요.")
        val connection = URL(baseAddress(settings.haAddress), "api/$path").openConnection() as HttpURLConnection
        try {
            connection.connectTimeout = 8000; connection.readTimeout = 8000; connection.instanceFollowRedirects = false
            connection.setRequestProperty("Authorization", "Bearer $token")
            connection.setRequestProperty("Accept", "application/json")
            if (data != null) {
                connection.requestMethod = "POST"; connection.doOutput = true
                connection.setRequestProperty("Content-Type", "application/json; charset=utf-8")
                connection.outputStream.use { it.write(data.toString().toByteArray(Charsets.UTF_8)) }
            } else connection.requestMethod = "GET"
            val code = connection.responseCode
            if (code == 401) throw IllegalStateException("Home Assistant 연결 토큰을 확인하세요.")
            if (code < 200 || code >= 300) throw IllegalStateException("Home Assistant 응답 오류 · $code")
            val body = connection.inputStream.bufferedReader(Charsets.UTF_8).use { it.readText() }
            val trimmed = body.trim()
            return when {
                trimmed.startsWith("[") -> JSONArray(trimmed)
                trimmed.startsWith("{") -> JSONObject(trimmed)
                else -> trimmed
            }
        } finally { connection.disconnect() }
    }

    /** 작업 스레드에서 호출. 성공·실패 모두 status 에 반영합니다. */
    fun refresh(settings: SmartSettings) {
        if (settings.haTokenProtected.isEmpty()) { status = "설정에서 Home Assistant 를 연결하세요."; updatedUtc = null; return }
        try {
            val states = request(settings, "states", null) as? JSONArray ?: throw IllegalStateException("기기 목록 형식을 읽을 수 없습니다.")
            val found = HashMap<String, HaEntity>()
            for (index in 0 until states.length()) {
                val state = states.optJSONObject(index) ?: continue
                val attributes = state.optJSONObject("attributes") ?: JSONObject()
                val id = state.text("entity_id") ?: continue
                found[id] = HaEntity(id, attributes.optString("friendly_name", id), state.optString("state", "unknown"), attributes.optInt("supported_features", 0))
            }
            val names = HashSet<String>()
            val domains = request(settings, "services", null) as? JSONArray ?: JSONArray()
            for (index in 0 until domains.length()) {
                val domain = domains.optJSONObject(index) ?: continue
                val list = domain.optJSONObject("services") ?: continue
                for (name in list.keys()) names.add(domain.optString("domain") + "." + name)
            }
            entities = found; services = names; updatedUtc = System.currentTimeMillis(); status = "Home Assistant 연결됨"
        } catch (error: IOException) {
            status = "Home Assistant 에 연결할 수 없습니다."; updatedUtc = null
        } catch (error: Exception) {
            status = error.message ?: "Home Assistant 확인 필요"; updatedUtc = null
        }
    }

    fun find(id: String?): HaEntity? = if (id == null) null else entities[id]

    fun view(id: String?): JSONObject? {
        val entity = find(id) ?: return null
        return obj("id" to entity.id, "name" to entity.name, "state" to entity.state, "available" to (updatedUtc != null && entity.available))
    }

    private fun fresh(): Boolean { val at = updatedUtc ?: return false; return System.currentTimeMillis() - at < 20_000 }

    fun can(id: String?, feature: Int): Boolean {
        val entity = find(id) ?: return false
        return fresh() && entity.available && (entity.features and feature) != 0
    }

    fun scriptReady(id: String?): Boolean {
        val entity = find(id) ?: return false
        return fresh() && entity.available && id!!.startsWith("script.") && services.contains("script.turn_on")
    }

    /** 작업 스레드에서 호출. 데스크톱과 같은 서비스·지원 기능 검사를 거친 뒤 서비스를 호출합니다. */
    fun execute(settings: SmartSettings, room: RoomBinding, action: String) {
        val data = JSONObject()
        val service: String; val entity: String?
        when (action) {
            "start" -> { service = "vacuum.start"; entity = room.vacuum; if (!can(entity, 8192)) throw IllegalStateException("청소 시작 지원과 기기 연결을 확인하세요.") }
            "pause" -> { service = "vacuum.pause"; entity = room.vacuum; if (!can(entity, 4)) throw IllegalStateException("일시정지 지원과 기기 연결을 확인하세요.") }
            "return" -> { service = "vacuum.return_to_base"; entity = room.vacuum; if (!can(entity, 16)) throw IllegalStateException("복귀 지원과 기기 연결을 확인하세요.") }
            "room-clean" -> { service = "script.turn_on"; entity = room.cleanScript; if (!scriptReady(entity)) throw IllegalStateException("이 방의 청소 스크립트를 먼저 연결하세요.") }
            "classic", "pop" -> {
                service = "music_assistant.play_media"; entity = room.speaker
                if (!can(entity, 512)) throw IllegalStateException("음악 재생 장치 연결을 확인하세요.")
                val playlist = if (action == "classic") room.classic else room.pop
                if (playlist.isNullOrBlank()) throw IllegalStateException("재생목록을 설정에서 연결하세요.")
                data.put("media_id", playlist); data.put("media_type", "playlist"); data.put("enqueue", "replace")
            }
            "music-stop" -> { service = "media_player.media_stop"; entity = room.speaker; if (!can(entity, 4096)) throw IllegalStateException("이 장치의 정지 지원과 연결을 확인하세요.") }
            else -> throw IllegalArgumentException("지원하지 않는 집 명령입니다.")
        }
        if (!services.contains(service) || entity == null || !entityPattern.matches(entity)) throw IllegalStateException("Home Assistant 에서 해당 동작을 사용할 수 없습니다.")
        data.put("entity_id", entity)
        request(settings, "services/" + service.replace('.', '/'), data)
    }
}

/**
 * 스마트홈 창의 상태 소유자. 설정 파일(`smart-home.json`)과 HA 폴링(10초), 명령 실행을 맡습니다.
 * 데스크톱의 영상 보관·YouTube 업로드는 PC 의 ffmpeg·폴더가 필요해 휴대폰 버전에는 없습니다.
 */
internal class SmartHome(directory: File, private val changed: () -> Unit) {
    private val file = File(directory, "smart-home.json")
    private val main = Handler(Looper.getMainLooper())
    private val worker = Executors.newSingleThreadExecutor()
    val ha = HaClient()
    var settings: SmartSettings = SmartSettings.from(LocalData.readObject(file) { JSONObject() })
        private set
    private var polling = false
    private var watching = false
    var executing = false
        private set
    var error: String? = null
        private set
    private val poll = object : Runnable { override fun run() { refresh(); if (watching) main.postDelayed(this, 10_000) } }

    fun watch(on: Boolean) {
        if (watching == on) return
        watching = on
        main.removeCallbacks(poll)
        if (on) main.post(poll)
    }

    @Throws(IOException::class)
    fun save(next: SmartSettings) {
        LocalData.write(file, next.toJson())
        settings = next
        error = null
        changed()
        refresh()
    }

    fun refresh(done: (() -> Unit)? = null) {
        if (polling) { done?.invoke(); return }
        polling = true
        val current = settings
        worker.execute {
            ha.refresh(current)
            main.post { polling = false; changed(); done?.invoke() }
        }
    }

    /** 결과 문구를 메인 스레드 콜백으로 돌려줍니다. */
    fun execute(action: String?, roomId: String?, itemId: String?, done: (String) -> Unit) {
        if (executing) { done("이전 집 명령이 끝난 뒤 다시 누르세요."); return }
        val room = settings.rooms.firstOrNull { it.id == roomId }
        if (room == null) { done("설정에서 방을 선택하세요."); return }
        if (action in archiveActions) { done("영상 보관·업로드는 Windows 허브에서 진행합니다."); return }
        if (action == null) { done("지원하지 않는 집 명령입니다."); return }
        executing = true; changed()
        val current = settings
        worker.execute {
            val result = try {
                ha.execute(current, room, action)
                ha.refresh(current)
                "${room.name} · ${labels[action] ?: action} 요청 완료"
            } catch (e: Exception) { "스마트홈 확인 필요 · " + (e.message ?: e.javaClass.simpleName) }
            main.post { executing = false; changed(); done(result) }
        }
    }

    /** 설정 화면의 '연결 확인'. 임시 설정으로 한 번 조회해 결과 문구를 돌려주고, 기기 목록은 선택 대화상자에 쓰도록 남겨 둡니다. */
    fun test(draft: SmartSettings, done: (String) -> Unit) {
        worker.execute {
            ha.refresh(draft)
            val text = if (ha.updatedUtc != null) ha.status + " · 기기 " + ha.entities.size + "개 · 서비스 " + ha.services.size + "개" else ha.status
            main.post { changed(); done(text) }
        }
    }

    fun entityIds(prefixes: List<String>): List<String> = ha.entities.keys.filter { id -> prefixes.any { id.startsWith(it) } }.sorted()

    fun snapshot(): JSONObject {
        val config = settings
        return obj(
            "status" to ha.status, "updatedUtc" to ha.updatedUtc?.let { iso.format(Date(it)) }, "error" to error,
            "connected" to (ha.updatedUtc != null), "busy" to executing, "archive" to null,
            "watch" to false, "daily" to false, "autoUpload" to false, "capture" to false, "youtubeConnected" to false,
            "rooms" to config.rooms.map { r ->
                obj(
                    "id" to r.id, "name" to r.name, "vacuum" to ha.view(r.vacuum), "speaker" to ha.view(r.speaker), "window" to ha.view(r.window), "dryer" to ha.view(r.dryer), "camera" to null,
                    "start" to ha.can(r.vacuum, 8192), "pause" to ha.can(r.vacuum, 4), "home" to ha.can(r.vacuum, 16), "roomClean" to ha.scriptReady(r.cleanScript),
                    "classic" to (ha.can(r.speaker, 512) && ha.services.contains("music_assistant.play_media") && !r.classic.isNullOrEmpty()),
                    "pop" to (ha.can(r.speaker, 512) && ha.services.contains("music_assistant.play_media") && !r.pop.isNullOrEmpty()),
                    "stopMusic" to ha.can(r.speaker, 4096)
                )
            }
        )
    }

    companion object {
        private val archiveActions = setOf("import", "open-inbox", "open-archive", "date", "prepare", "prepare-extra", "retry", "upload", "resolve-upload", "open-job")
        private val labels = mapOf("start" to "청소 시작", "pause" to "일시정지", "return" to "복귀", "room-clean" to "이 방 다시 청소", "classic" to "클래식 재생", "pop" to "인기가요 재생", "music-stop" to "음악 정지")
        private val iso = SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss'Z'", Locale.US).apply { timeZone = TimeZone.getTimeZone("UTC") }
    }
}
