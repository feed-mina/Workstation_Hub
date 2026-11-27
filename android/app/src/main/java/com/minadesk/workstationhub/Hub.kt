package com.minadesk.workstationhub

import android.app.Activity
import android.app.AlertDialog
import android.app.Application
import android.app.DownloadManager
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import org.json.JSONObject
import java.io.File
import java.util.UUID

/** MainActivity 가 구현. 탭 전환과 네이티브 설정 화면 열기를 허브가 요청할 때 사용합니다. */
internal interface HubHost {
    val activity: Activity
    fun showPanel(key: String)
    fun openSmartSettings()
    fun applyTheme(theme: String)
}

/**
 * 데스크톱 Hub.cs 의 휴대폰판. 상태(모드·테마·시연 조명·집중 표시·기록)를 소유하고 화면 메시지를 라우팅합니다.
 * 화면(ui 폴더의 js)은 데스크톱과 같은 파일이므로 메시지 이름·필드 모양을 그대로 맞춥니다.
 */
internal class Hub(private val app: Application) {
    companion object {
        val KEYS = listOf("macro", "pc", "home", "log", "dev", "widgets")
        val TITLES = listOf("매크로", "성능", "스마트홈", "기록", "개발자", "위젯")
        const val VERSION = "0.9.0"
        private val MACRO_ACTIONS = setOf("work_start", "work_end", "volume_mute", "vscode", "terminal", "record", "media", "screenshot", "focus", "light_toggle", "ac_toggle", "pc_lock", "youtube_upload", "naver_blog_upload")
        private val PC_ONLY_MACROS = setOf("vscode", "terminal", "record", "screenshot", "pc_lock")
        private val SKILL_MESSAGES = setOf("skills-open", "skills-refresh", "skill-project-add", "skill-project-select", "skill-start-apps", "skill-save", "skill-remove", "skill-run", "skill-button-options", "github-login", "github-cancel", "github-refresh", "github-logout", "github-share-connect", "github-sync", "github-register", "project-config", "project-bind", "project-conflict", "project-open-app")
        private val DRAFT_CARDS = setOf("codex-new", "codex-compose", "codex-approval", "codex-bug", "codex-feature", "codex-analyze", "codex-refactor", "codex-git", "codex-skill-use", "codex-skill-create", "prompt-1", "prompt-2", "prompt-3")
        private val HELP_CARDS = setOf("codex-palette", "codex-previous", "codex-next", "codex-find-chat", "codex-find-file", "codex-file-tree")
        private val PHONE_CARDS = (DRAFT_CARDS - "codex-skill-use") + HELP_CARDS +
            DeveloperPreferences.BUILT_IN_LINKS.keys.filter { it.startsWith("web-") } + DeveloperPreferences.LINK_NAMES.keys +
            setOf("model", "effort", "codex-model-comparison", "codex-recent-1", "codex-recent-2", "codex-settings", "tool-focus", "tool-accounts")
        private val CARD_LABELS = mapOf(
            "project" to "프로젝트", "open-projects" to "프로젝트 목록", "open-project-settings" to "프로젝트 설정", "codex-skills" to "스킬 목록",
            "validate-types" to "타입 검사", "validate-test" to "테스트", "validate-build" to "빌드", "validate-all" to "전체 검증",
            "tool-record" to "OBS 녹화", "tool-screenshot" to "스크린샷", "tool-lock" to "PC 잠금", "tool-find" to "파일 검색",
            "tool-vscode" to "VS Code", "tool-terminal" to "터미널", "tool-explorer" to "탐색기", "tool-manager" to "작업 관리자", "tool-settings" to "Windows 설정"
        )
        const val PHONE_SKILLS_STATUS = "휴대폰에서는 PC 의 프로젝트·스킬 버튼을 사용하지 않습니다. 프로젝트 관리는 Windows 허브에서 진행합니다."
    }

    private val main = Handler(Looper.getMainLooper())
    private val prefs = app.getSharedPreferences("hub", Context.MODE_PRIVATE)
    private val dataDir = File(app.filesDir, "WorkstationHub").also { it.mkdirs() }
    private val history = WorkHistoryStore(dataDir, android.os.Build.MODEL ?: "휴대폰") { sendHistory() }
    val commands = MacroCommands(app)
    private val telemetry = DeviceTelemetry(app)
    val smart = SmartHome(dataDir) { broadcast() }
    private val weather = Weather(app.getSharedPreferences("widgets", Context.MODE_PRIVATE)) { broadcastDeveloper() }
    private val developer = DeveloperPreferences(app.getSharedPreferences("developer", Context.MODE_PRIVATE))
    private val panels = HashMap<String, PanelView>()
    private val active = HashSet<String>()
    private val studio = linkedMapOf<String, Any>("lamp" to true, "brightness" to 80, "bar" to true, "kelvin" to 4500, "ac" to false, "target" to 23, "presence" to true, "door" to false)
    private val developerScope = UUID.randomUUID().toString().replace("-", "")
    private var lastResultUrls: List<String?> = emptyList()
    private var mode = "대기"
    private var skillsStatus = PHONE_SKILLS_STATUS
    private var sampling = false
    private val sampler = object : Runnable {
        override fun run() {
            val pc = panels["pc"] ?: return
            if (pc.ready) pc.send(telemetry.sample())
            if (sampling) main.postDelayed(this, 1500)
        }
    }

    var host: HubHost? = null
    var visible: String? = null
        set(value) {
            field = value
            for (panel in panels.values) panel.send(obj("kind" to "visibility", "visible" to (panel.key == value)))
            refreshSampling()
        }
    var theme: String
        get() = prefs.getString("theme", "green")?.takeIf { it == "neon" } ?: "green"
        private set(value) { prefs.edit().putString("theme", value).apply() }
    private var startApps: String
        get() = prefs.getString("startApps", "both")?.takeIf { it in setOf("both", "gpt", "claude") } ?: "both"
        set(value) { prefs.edit().putString("startApps", value).apply() }
    private var mediaProvider: String
        get() = prefs.getString("mediaProvider", "youtube")?.takeIf { it == "music" } ?: "youtube"
        set(value) { prefs.edit().putString("mediaProvider", value).apply() }
    val focusEnabled: Boolean get() = active.contains("focus")

    init { addLog("작업실", "휴대폰 앱을 시작했습니다.") }

    // ── 패널 관리 ──────────────────────────────────────────────────────────
    fun attach(panel: PanelView) { panels[panel.key] = panel }
    fun detach(key: String) { panels.remove(key); refreshSampling() }

    private fun refreshSampling() {
        val pc = panels["pc"]
        val shouldSample = pc != null && pc.ready && visible == "pc"
        if (shouldSample != sampling) {
            sampling = shouldSample
            main.removeCallbacks(sampler)
            telemetry.reset()
            if (shouldSample) { main.post(sampler); pc!!.send(telemetry.gpu()) }
        }
        val home = panels["home"]
        smart.watch(home != null && home.ready && visible == "home")
    }

    // ── 상태 전송 ──────────────────────────────────────────────────────────
    private fun snapshot(): JSONObject = obj(
        "kind" to "state", "version" to VERSION, "theme" to theme, "mode" to mode, "home" to studio, "smart" to smart.snapshot(),
        "macro" to obj(
            "muted" to commands.muted(), "recording" to null, "busy" to emptyList<String>(),
            "accounts" to obj("mediaProvider" to mediaProvider, "connections" to listOf("media", "youtube_upload", "naver_blog_upload").map { obj("key" to it, "connected" to true) })
        ),
        "skills" to skillsSnapshot(), "active" to active.toList(), "enabled" to KEYS.associateWith { true }
    )

    private fun skillsSnapshot(): JSONObject = obj(
        "readOnly" to true, "startAppsEditable" to true, "loading" to false, "busy" to false, "status" to skillsStatus, "runningId" to null, "selectedProjectId" to null,
        "startApps" to startApps, "syncStatus" to "", "projects" to emptyList<Any>(), "buttons" to emptyList<Any>(), "catalog" to emptyList<Any>(),
        "warnings" to listOf("휴대폰에는 설치된 스킬 폴더가 없습니다."),
        "github" to obj("available" to false, "connected" to false, "accountId" to 0, "login" to null, "authCode" to null, "authenticating" to false, "shareReady" to false, "shareRepository" to null, "repositories" to emptyList<Any>())
    )

    private fun broadcast() {
        val state = snapshot()
        for (panel in panels.values) if (panel.key == "dev" || panel.key == "widgets") panel.send(obj("kind" to "theme", "theme" to theme)) else panel.send(state)
        broadcastDeveloper()
    }

    private fun broadcastDeveloper() {
        panels["dev"]?.takeIf { it.ready }?.send(developerSnapshot())
        panels["widgets"]?.takeIf { it.ready }?.send(weather.snapshot())
    }

    private fun notice(panel: PanelView, text: String) = panel.send(obj("kind" to "notice", "text" to text))

    private fun addLog(source: String, detail: String) {
        history.add(source, detail)
    }

    private fun sendHistory() {
        val panel = panels["log"]?.takeIf { it.ready } ?: return
        val state = history.snapshot
        panel.send(obj("kind" to "history", "status" to state.status,
            "items" to state.rows.map { obj("id" to it.id, "time" to WorkHistory.time(it), "pc" to it.device, "source" to it.source, "detail" to it.detail) }))
    }

    private fun ready(panel: PanelView) {
        if (panel.key == "dev" || panel.key == "widgets") panel.send(obj("kind" to "theme", "theme" to theme)) else panel.send(snapshot())
        when (panel.key) {
            "log" -> sendHistory()
            "dev" -> panel.send(developerSnapshot())
            "widgets" -> panel.send(weather.snapshot())
        }
        panel.send(obj("kind" to "visibility", "visible" to (panel.key == visible)))
        refreshSampling()
    }

    // ── 화면 → 허브 ────────────────────────────────────────────────────────
    fun receive(sender: PanelView, raw: String) {
        if (raw.length > 16384 || panels[sender.key] != sender) return
        val msg = try { JSONObject(raw) } catch (_: Exception) { return }
        val type = msg.text("type") ?: return
        var id = msg.text("id")
        if (id == "mic_mute") id = "volume_mute"
        when {
            type == "ready" -> ready(sender)
            sender.key == "dev" && (type == "developer-ai" || type == "developer-models") -> handleUtility(sender, type, id, msg.text("provider"))
            type == "developer-show" && sender.key == "macro" -> host?.showPanel("dev")
            sender.key == "dev" && type == "developer-run" -> handleRun(sender, id, msg.text("value"))
            sender.key == "dev" && type == "developer-choose" -> handleChoose(sender, id, msg.text("value"))
            sender.key == "dev" && type == "developer-result-open" -> {
                val index = msg.text("index")?.toIntOrNull()
                val url = index?.let { lastResultUrls.getOrNull(it) }
                if (url != null) notice(sender, commands.openUrl(url, "결과")) else notice(sender, "열 수 있는 링크가 없습니다.")
            }
            sender.key == "dev" && (type == "developer-auto-branches" || type == "developer-cancel") -> { }
            type == "developer-widget" && sender.key == "widgets" -> handleWidget(sender, id, msg.text("value"))
            sender.key == "macro" && (type == "skills-close" || type == "projects-open" || type == "github-device-page") -> { }
            sender.key == "macro" && type in SKILL_MESSAGES -> {
                if (type == "skill-start-apps") { msg.text("value")?.let { if (it in setOf("both", "gpt", "claude")) startApps = it } }
                else skillsStatus = PHONE_SKILLS_STATUS
                broadcast(); sender.send(obj("kind" to "skills-result", "action" to type))
            }
            type == "history-settings" && sender.key == "log" -> historyDialog()
            type == "macro-settings" && sender.key == "macro" -> macroSettingsDialog()
            type == "account-settings" && sender.key == "macro" -> accountDialog()
            type == "smart-settings" && sender.key == "home" -> host?.openSmartSettings()
            type == "smart" && sender.key == "home" -> smart.execute(msg.text("action"), msg.text("roomId"), msg.text("itemId")) { result ->
                addLog("스마트홈", result); broadcast(); notice(sender, result)
            }
            type == "macro" && sender.key == "macro" && id != null && id in MACRO_ACTIONS -> handleMacro(sender, id, msg.text("label"))
            type == "home" && sender.key == "home" && id != null -> handleStudio(sender, id, msg)
        }
    }

    private fun handleMacro(sender: PanelView, id: String, label: String?) {
        if (id in PC_ONLY_MACROS) { notice(sender, "휴대폰에서는 지원하지 않는 PC 명령입니다 · Windows 허브에서 실행하세요."); return }
        val text: String = when (id) {
            "media" -> commands.playPause()
            "volume_mute" -> { val muted = commands.toggleMute(); if (muted == null) "볼륨 음소거 · 이 기기에서 바꿀 수 없습니다(방해 금지 설정 확인)" else "볼륨 음소거 · " + (if (muted) "켜짐" else "꺼짐") }
            "youtube_upload" -> commands.openUrl(MacroCommands.YOUTUBE_UPLOAD, "유튜브 업로드") + " · 파일 선택과 게시는 YouTube 에서 진행합니다."
            "naver_blog_upload" -> commands.openUrl(MacroCommands.NAVER_WRITE, "네이버 블로그 글쓰기")
            "work_start" -> {
                mode = "업무"
                developer.webAddress("web-work-cycle")?.let { commands.openUrl(it, "Work-cycle") }
                val apps = ArrayList<String>()
                if (startApps != "claude") apps.add(commands.openApp(MacroCommands.CHATGPT, DeveloperPreferences.BUILT_IN_LINKS["web-chatgpt"]!!, "ChatGPT"))
                if (startApps != "gpt") apps.add(commands.openApp(MacroCommands.CLAUDE, DeveloperPreferences.BUILT_IN_LINKS["web-claude"]!!, "Claude"))
                "업무 시작 · " + apps.joinToString(" · ")
            }
            "work_end" -> { mode = "퇴근"; "퇴근 · 휴대폰에서는 PC 잠금을 실행하지 않습니다. 잠금은 Windows 허브에서 진행합니다." }
            "focus" -> toggleFocus()
            "light_toggle" -> { val on = !((studio["lamp"] as Boolean) || (studio["bar"] as Boolean)); studio["lamp"] = on; studio["bar"] = on; if (on && studio["brightness"] == 0) studio["brightness"] = 65; (label ?: id).take(40) + " · 시연" }
            "ac_toggle" -> { studio["ac"] = !(studio["ac"] as Boolean); (label ?: id).take(40) + " · 시연" }
            else -> return
        }
        addLog(if (id == "work_start" || id == "work_end" || id == "focus") "매크로" else "휴대폰 명령", text)
        broadcast(); notice(sender, text)
    }

    private fun handleStudio(sender: PanelView, id: String, msg: JSONObject) {
        val names = mapOf("lamp" to "스탠드", "bar" to "라이트바", "ac" to "냉방", "presence" to "재실", "door" to "문 열림", "brightness" to "밝기", "kelvin" to "색온도", "target" to "목표 온도")
        if (id in setOf("lamp", "bar", "ac", "presence", "door")) {
            studio[id] = !(studio[id] as Boolean)
            if (id == "lamp" && studio[id] == true && studio["brightness"] == 0) studio["brightness"] = 65
        } else {
            val n = msg.integer("value") ?: return
            when {
                id == "brightness" && n in 0..100 -> { studio[id] = n; studio["lamp"] = n > 0 }
                id == "kelvin" && (n == 3000 || n == 4500 || n == 6500) -> { studio[id] = n; studio["bar"] = true }
                id == "target" && n in 18..28 -> { studio[id] = n; studio["ac"] = true }
                else -> return
            }
        }
        val value = studio[id]
        val text = (names[id] ?: id) + " · " + (if (value is Boolean) (if (value) "켜짐" else "꺼짐") else value.toString()) + " (시연)"
        addLog("스마트홈", text); broadcast(); notice(sender, text)
    }

    private fun toggleFocus(): String {
        if (!active.remove("focus")) active.add("focus")
        val detail = "집중 표시 " + (if (focusEnabled) "켜짐" else "꺼짐") + " · 허브 안에 표시"
        return detail
    }

    fun changeTheme(next: String) {
        if ((next != "green" && next != "neon") || theme == next) return
        theme = next
        host?.applyTheme(next)
        broadcast()
    }

    // ── 개발자 컨트롤 ──────────────────────────────────────────────────────
    private fun developerSnapshot(): JSONObject = obj(
        "kind" to "developer-state", "scope" to developerScope, "supportedCards" to PHONE_CARDS.toList(), "project" to null, "projects" to emptyList<Any>(), "account" to null,
        "projectBusy" to false, "projectStatus" to "휴대폰은 PC 의 프로젝트와 GitHub 조회를 대신하지 않습니다. 웹앱·Codex 요청 복사·테마는 사용할 수 있습니다.",
        "readOnly" to false, "shareReady" to false, "branch" to null, "defaultBranch" to null, "branches" to emptyList<Any>(), "branchBusy" to false,
        "needBranches" to false, "branchError" to null, "activePr" to null, "model" to developer.model, "effort" to developer.effort,
        "models" to DeveloperPreferences.MODELS, "efforts" to DeveloperPreferences.EFFORTS,
        "prompts" to (1..3).map { developer.prompt(it) }, "threads" to (1..2).map { developer.thread(it) }, "requests" to JSONObject(), "catalog" to emptyList<Any>(),
        "links" to DeveloperPreferences.LINK_NAMES.map { (key, label) -> obj("id" to key, "label" to label, "value" to developer.webAddress(key)) },
        "folders" to emptyList<Any>(), "layout" to "original", "theme" to theme, "enabled" to KEYS.associateWith { true }, "notice" to null,
        "running" to false, "verifying" to false, "focus" to focusEnabled
    )

    private fun result(sender: PanelView, title: String, summary: String, body: String? = null, rows: List<Pair<String, String?>> = emptyList()) {
        lastResultUrls = rows.map { it.second }
        sender.send(obj("kind" to "developer-result", "scope" to developerScope, "key" to title, "title" to title, "summary" to summary, "body" to body,
            "rows" to rows.mapIndexed { index, (label, url) -> obj("title" to label, "detail" to (url ?: ""), "pr" to null, "branch" to null, "index" to index, "open" to (url != null)) }))
    }

    private fun pcOnly(sender: PanelView, id: String?) =
        result(sender, CARD_LABELS[id] ?: "PC 전용 기능", "Windows 허브에서 실행하는 카드입니다. 휴대폰에서는 웹앱 바로가기·Codex 요청 복사·AI 앱 열기·테마 변경을 사용할 수 있습니다.")

    private fun handleRun(sender: PanelView, id: String?, value: String?) {
        if (id != null && id !in PHONE_CARDS) { pcOnly(sender, id); return }
        when {
            id == null -> return
            id == "tool-focus" -> { val text = toggleFocus(); addLog("매크로", text); broadcast(); notice(sender, text) }
            id == "tool-accounts" -> accountDialog()
            id == "web-ha" && developer.webAddress("web-ha") == null -> result(sender, "Home Assistant", "바로가기 설정에서 Home Assistant 주소를 저장하거나 스마트홈 탭의 연결 설정을 사용하세요.")
            id.startsWith("web-") -> {
                val url = developer.webAddress(id)
                if (url == null) result(sender, DeveloperPreferences.LINK_NAMES[id] ?: id, "바로가기 설정에서 주소를 저장하세요.")
                else { val text = commands.openUrl(url, DeveloperPreferences.LINK_NAMES[id] ?: id); addLog("개발자 컨트롤", text); notice(sender, text) }
            }
            id in DRAFT_CARDS -> {
                val text = MobileDraft.compose(value.orEmpty(), developer.model, developer.effort)
                if (text.isNotEmpty()) {
                    (app.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText("Codex 요청", text))
                }
                val opened = commands.openApp(MacroCommands.CHATGPT, DeveloperPreferences.BUILT_IN_LINKS["web-chatgpt"]!!, "ChatGPT")
                val summary = if (text.isEmpty()) opened else "요청문을 복사했습니다 · $opened · 앱에서 붙여넣어 보내세요. 모델·추론 강도는 요청문에 담는 선호값입니다."
                addLog("개발자 컨트롤", "Codex 요청 준비 · " + (if (text.isEmpty()) "빈 요청" else text.take(40)))
                result(sender, "Codex 요청 준비", summary, body = text)
            }
            id in HELP_CARDS -> notice(sender, commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS["codex-help"]!!, "Codex 명령 안내"))
            id == "codex-recent-1" || id == "codex-recent-2" -> {
                val link = developer.thread(id.last().digitToInt())
                if (link.startsWith("https://")) notice(sender, commands.openUrl(link, "저장한 채팅"))
                else result(sender, "저장한 채팅", if (link.isEmpty()) "채팅 링크를 먼저 저장하세요." else "codex:// 링크는 데스크톱 Codex 앱 전용입니다. https:// 주소를 저장하면 휴대폰에서 열 수 있습니다.")
            }
            id == "codex-settings" -> notice(sender, commands.openApp(MacroCommands.CHATGPT, DeveloperPreferences.BUILT_IN_LINKS["web-chatgpt"]!!, "ChatGPT"))
            id == "codex-skills" -> result(sender, "스킬 목록", "휴대폰에는 설치된 스킬 목록이 없습니다. Windows 허브에서 확인하세요.")
            else -> pcOnly(sender, id)
        }
    }

    private fun handleChoose(sender: PanelView, id: String?, value: String?) {
        when {
            id == null -> return
            id == "theme" -> value?.let { changeTheme(it) }
            id == "panel" -> value?.takeIf { it in KEYS }?.let { host?.showPanel(it) }
            id == "restore" -> notice(sender, "휴대폰에서는 모든 탭이 항상 켜져 있습니다.")
            id == "layout" -> { notice(sender, "창 배치는 Windows 허브 전용입니다."); sender.send(developerSnapshot()) }
            id == "model" -> { developer.setModel(value); broadcastDeveloper() }
            id == "effort" -> { developer.setEffort(value); broadcastDeveloper() }
            id in DeveloperPreferences.LINK_NAMES -> {
                try { developer.setWebAddress(id, value); notice(sender, (DeveloperPreferences.LINK_NAMES[id] ?: id) + " 주소를 저장했습니다.") }
                catch (e: IllegalArgumentException) { notice(sender, e.message ?: "주소를 저장하지 못했습니다.") }
                broadcastDeveloper()
            }
            id.startsWith("prompt-") -> { id.last().digitToIntOrNull()?.let { developer.setPrompt(it, value) }; notice(sender, "요청문을 이 휴대폰에 저장했습니다."); broadcastDeveloper() }
            id == "codex-recent-1" || id == "codex-recent-2" -> { developer.setThread(id.last().digitToInt(), value); notice(sender, "채팅 링크를 저장했습니다."); broadcastDeveloper() }
            else -> { notice(sender, "프로젝트·브랜치·폴더 설정은 Windows 허브 전용입니다."); sender.send(developerSnapshot()) }
        }
    }

    private fun handleUtility(sender: PanelView, type: String, action: String?, provider: String?) {
        if (type == "developer-models") {
            when (action) {
                "guide" -> notice(sender, commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS["codex-guide"]!!, "공식 모델 안내"))
                "release" -> notice(sender, commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS["codex-release"]!!, "모델 발표"))
                else -> sender.send(obj("kind" to "model-comparison", "busy" to false, "preferred" to developer.model, "checkedAt" to null, "builtInDate" to DeveloperPreferences.MODEL_DATE,
                    "error" to "휴대폰은 공식 문서를 조회하지 않고 앱의 비교 자료만 표시합니다.",
                    "models" to DeveloperPreferences.MODEL_ROWS.map { obj("id" to it.id, "title" to it.title, "work" to it.work, "tradeoff" to it.tradeoff, "context" to it.context, "listed" to true, "latest" to it.latest) }))
            }
            return
        }
        val gpt = provider == "gpt"
        when (action) {
            "link", "setup" -> notice(sender, commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS[if (gpt) "play-chatgpt" else "play-claude"]!!, if (gpt) "ChatGPT 앱 설치" else "Claude 앱 설치"))
            "desktop" -> notice(sender, if (gpt) commands.openApp(MacroCommands.CHATGPT, DeveloperPreferences.BUILT_IN_LINKS["web-chatgpt"]!!, "ChatGPT") else commands.openApp(MacroCommands.CLAUDE, DeveloperPreferences.BUILT_IN_LINKS["web-claude"]!!, "Claude"))
            "guide" -> notice(sender, commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS[if (gpt) "codex-auth" else "claude-auth"]!!, "공식 로그인 안내"))
            "unlink" -> notice(sender, "휴대폰은 허브 연동 표시를 저장하지 않습니다.")
        }
        sender.send(obj("kind" to "ai-account-state", "readOnly" to false, "providers" to listOf("gpt", "claude").map { key ->
            val installed = commands.isInstalled(if (key == "gpt") MacroCommands.CHATGPT else MacroCommands.CLAUDE)
            obj("id" to key, "title" to (if (key == "gpt") "GPT · ChatGPT 앱" else "Claude 앱"), "status" to (if (installed) "Account" else "MissingApp"),
                "statusText" to (if (installed) "앱 설치됨" else "앱 미설치"), "linked" to false, "connected" to installed, "busy" to false, "login" to false,
                "message" to "휴대폰은 앱 설치 여부만 확인합니다. 로그인은 각 앱에서 직접 진행하며 Codex·Claude Code CLI 상태는 Windows 허브에서 확인합니다.")
        }))
    }

    private fun handleWidget(sender: PanelView, id: String?, value: String?) {
        val text = when (id) {
            "weather" -> { weather.refresh(); return }
            "city" -> { weather.choose(value); return }
            "weather-source", "web-calendar", "web-github", "web-drive", "usage-codex", "usage-claude" -> commands.openUrl(DeveloperPreferences.BUILT_IN_LINKS[id]!!, id)
            "dock-gpt", "codex-root" -> commands.openApp(MacroCommands.CHATGPT, DeveloperPreferences.BUILT_IN_LINKS["web-chatgpt"]!!, "ChatGPT")
            "dock-claude" -> commands.openApp(MacroCommands.CLAUDE, DeveloperPreferences.BUILT_IN_LINKS["web-claude"]!!, "Claude")
            "tool-settings" -> commands.openSystem(Settings.ACTION_SETTINGS, "휴대폰 설정")
            "tool-manager" -> commands.openSystem(Settings.ACTION_MANAGE_APPLICATIONS_SETTINGS, "앱 관리")
            "folder-explorer" -> commands.openSystem(DownloadManager.ACTION_VIEW_DOWNLOADS, "다운로드 파일")
            "folder-work" -> "작업 폴더는 Windows 허브 전용입니다."
            else -> return
        }
        notice(sender, text)
    }

    // ── 네이티브 대화상자 ─────────────────────────────────────────────────
    private fun dialog(title: String, message: String, builder: (AlertDialog.Builder) -> Unit = {}) {
        val activity = host?.activity ?: return
        val box = AlertDialog.Builder(activity).setTitle(title).setMessage(message).setNegativeButton("닫기", null)
        builder(box)
        box.show()
    }

    private fun historyDialog() = dialog("작업 기록",
        "이 휴대폰의 기록은 기기 안에만 저장합니다(최근 ${WorkHistory.RETENTION}개). 두 PC 사이의 공유 폴더 동기화는 Windows 허브에서만 지원합니다.")

    private fun macroSettingsDialog() = dialog("매크로 설정",
        "재생/정지와 볼륨 음소거는 이 휴대폰의 미디어 스트림에 적용됩니다. 유튜브·네이버 버튼은 휴대폰의 앱 또는 브라우저로 화면을 열며 로그인은 각 서비스에서 진행합니다.\n\n재생 서비스 표시: " + (if (mediaProvider == "music") "YouTube Music" else "YouTube") + "\n테마: " + (if (theme == "neon") "네온" else "기존 녹색")) {
        it.setPositiveButton("재생 서비스 전환") { _, _ -> mediaProvider = if (mediaProvider == "music") "youtube" else "music"; broadcast() }
        it.setNeutralButton("테마 전환") { _, _ -> changeTheme(if (theme == "neon") "green" else "neon") }
    }

    private fun accountDialog() = dialog("계정 연결",
        "휴대폰 버전은 전용 브라우저 프로필을 만들지 않습니다. 재생/정지는 지금 재생 중인 앱에 미디어 키를 보내고, 유튜브 업로드·네이버 글쓰기는 휴대폰에 설치된 앱 또는 기본 브라우저의 로그인 상태를 그대로 사용합니다. Hub 는 비밀번호·인증번호를 수집하지 않습니다.")
}
