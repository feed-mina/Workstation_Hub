package com.minadesk.workstationhub

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
import java.util.concurrent.Executor

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

/** All capability checks and UI fields read the same completed response. */
internal data class HaSnapshot(
    val entities: Map<String, HaEntity> = emptyMap(),
    val services: Set<String> = emptySet(),
    val status: String = "Home Assistant 연결 전",
    val updatedUtc: Long? = null
) {
    fun fresh(): Boolean = updatedUtc?.let { System.currentTimeMillis() - it in 0L until 20_000L } == true
    fun find(id: String?): HaEntity? = if (id == null) null else entities[id]
    fun view(id: String?): JSONObject? = find(id)?.let {
        obj("id" to it.id, "name" to it.name, "state" to it.state, "available" to (fresh() && it.available))
    }
    fun can(id: String?, feature: Int): Boolean = fresh() && find(id)?.let { it.available && (it.features and feature) != 0 } == true
    fun scriptReady(id: String?): Boolean = fresh() && id?.startsWith("script.") == true && find(id)?.available == true && "script.turn_on" in services
    fun entityIds(prefixes: List<String>): List<String> = entities.keys.filter { id -> prefixes.any { id.startsWith(it) } }.sorted()
}

/** One client belongs to one saved connection, or one isolated connection test. */
internal class HaClient(private val transport: ((SmartSettings, String, JSONObject?) -> Any?)? = null) {
    @Volatile var state = HaSnapshot()
        private set
    private val connectionLock = Any()
    private var cancelled = false
    private var activeConnection: HttpURLConnection? = null
    val status: String get() = state.status

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
        synchronized(connectionLock) { check(!cancelled) { "연결 확인을 취소했습니다." } }
        transport?.let { return it(settings, path, data) }
        val token = Secrets.unprotect(settings.haTokenProtected)
        if (token.isEmpty()) throw IllegalStateException("설정에서 Home Assistant 토큰을 입력하세요.")
        val connection = URL(baseAddress(settings.haAddress), "api/$path").openConnection() as HttpURLConnection
        synchronized(connectionLock) {
            check(!cancelled) { "연결 확인을 취소했습니다." }
            activeConnection = connection
        }
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
        } finally {
            connection.disconnect()
            synchronized(connectionLock) { if (activeConnection === connection) activeConnection = null }
        }
    }

    fun cancel() = synchronized(connectionLock) {
        cancelled = true
        activeConnection?.disconnect()
    }

    /** 작업 스레드에서 호출. 성공·실패 모두 status 에 반영합니다. */
    fun refresh(settings: SmartSettings) {
        if (settings.haTokenProtected.isEmpty()) { state = HaSnapshot(status = "설정에서 Home Assistant 를 연결하세요."); return }
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
            state = HaSnapshot(found.toMap(), names.toSet(), "Home Assistant 연결됨", System.currentTimeMillis())
        } catch (error: IOException) {
            state = HaSnapshot(status = "Home Assistant 에 연결할 수 없습니다.")
        } catch (error: Exception) {
            state = HaSnapshot(status = error.message ?: "Home Assistant 확인 필요")
        }
    }

    /** 작업 스레드에서 호출. 데스크톱과 같은 서비스·지원 기능 검사를 거친 뒤 서비스를 호출합니다. */
    fun execute(settings: SmartSettings, room: RoomBinding, action: String) {
        val snapshot = state
        fun can(id: String?, feature: Int) = snapshot.can(id, feature)
        val data = JSONObject()
        val service: String; val entity: String?
        when (action) {
            "start" -> { service = "vacuum.start"; entity = room.vacuum; if (!can(entity, 8192)) throw IllegalStateException("청소 시작 지원과 기기 연결을 확인하세요.") }
            "pause" -> { service = "vacuum.pause"; entity = room.vacuum; if (!can(entity, 4)) throw IllegalStateException("일시정지 지원과 기기 연결을 확인하세요.") }
            "return" -> { service = "vacuum.return_to_base"; entity = room.vacuum; if (!can(entity, 16)) throw IllegalStateException("복귀 지원과 기기 연결을 확인하세요.") }
            "room-clean" -> { service = "script.turn_on"; entity = room.cleanScript; if (!snapshot.scriptReady(entity)) throw IllegalStateException("이 방의 청소 스크립트를 먼저 연결하세요.") }
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
        if (!snapshot.services.contains(service) || entity == null || !entityPattern.matches(entity)) throw IllegalStateException("Home Assistant 에서 해당 동작을 사용할 수 없습니다.")
        data.put("entity_id", entity)
        request(settings, "services/" + service.replace('.', '/'), data)
    }
}

/**
 * 스마트홈 창의 상태 소유자. 설정 파일(`smart-home.json`)과 HA 폴링(10초), 명령 실행을 맡습니다.
 * 데스크톱의 영상 보관·YouTube 업로드는 PC 의 ffmpeg·폴더가 필요해 휴대폰 버전에는 없습니다.
 */
internal class SmartHome(
    directory: File,
    private val ui: UiScheduler = AndroidUiScheduler(),
    private val worker: Executor = Executors.newSingleThreadExecutor(),
    private val clientFactory: () -> HaClient = { HaClient() },
    private val changed: () -> Unit
) {
    private val file = File(directory, "smart-home.json")
    @Volatile var ha = clientFactory()
        private set
    var settings: SmartSettings = SmartSettings.from(LocalData.readObject(file) { JSONObject() })
        private set
    private var polling: HaClient? = null
    private var watching = false
    var executing = false
        private set
    var error: String? = null
        private set
    private val poll = object : Runnable { override fun run() { refresh(); if (watching) ui.later(this, 10_000) } }

    fun watch(on: Boolean) {
        if (watching == on) return
        watching = on
        ui.remove(poll)
        if (on) ui.post { poll.run() }
    }

    @Throws(IOException::class)
    fun save(next: SmartSettings) {
        check(!executing) { "집 명령이 끝난 뒤 설정을 저장하세요." }
        LocalData.write(file, next.toJson())
        settings = SmartSettings.from(next.toJson())
        // Replacing the client invalidates every capability immediately. Old workers own only the old client.
        ha = clientFactory()
        error = null
        changed()
        refresh()
    }

    fun refresh() {
        val client = ha
        if (polling === client) return
        polling = client
        val current = settings
        worker.execute {
            if (client !== ha) return@execute
            client.refresh(current)
            ui.post {
                if (client === ha) {
                    if (polling === client) polling = null
                    changed()
                }
            }
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
        val client = ha
        worker.execute {
            val result = try {
                check(client === ha) { "새 연결 상태를 확인한 뒤 다시 실행하세요." }
                client.execute(current, room, action)
                client.refresh(current)
                "${room.name} · ${labels[action] ?: action} 요청 완료"
            } catch (e: Exception) { "스마트홈 확인 필요 · " + (e.message ?: e.javaClass.simpleName) }
            ui.post { executing = false; changed(); done(result) }
        }
    }

    fun snapshot(): JSONObject {
        val config = settings
        val state = ha.state
        return obj(
            "status" to state.status, "updatedUtc" to state.updatedUtc?.let { iso.format(Date(it)) }, "error" to error,
            "connected" to state.fresh(), "busy" to executing, "archive" to null,
            "watch" to false, "daily" to false, "autoUpload" to false, "capture" to false, "youtubeConnected" to false,
            "rooms" to config.rooms.map { r ->
                obj(
                    "id" to r.id, "name" to r.name, "vacuum" to state.view(r.vacuum), "speaker" to state.view(r.speaker), "window" to state.view(r.window), "dryer" to state.view(r.dryer), "camera" to null,
                    "start" to state.can(r.vacuum, 8192), "pause" to state.can(r.vacuum, 4), "home" to state.can(r.vacuum, 16), "roomClean" to state.scriptReady(r.cleanScript),
                    "classic" to (state.can(r.speaker, 512) && state.services.contains("music_assistant.play_media") && !r.classic.isNullOrEmpty()),
                    "pop" to (state.can(r.speaker, 512) && state.services.contains("music_assistant.play_media") && !r.pop.isNullOrEmpty()),
                    "stopMusic" to state.can(r.speaker, 4096)
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
