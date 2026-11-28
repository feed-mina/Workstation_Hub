package com.minadesk.workstationhub

import android.content.SharedPreferences
import java.net.URI

/** 개발자 컨트롤·보조 위젯의 이 기기 설정(모델·추론 강도·웹 바로가기·요청문). 데스크톱 DeveloperPreferenceStore 의 휴대폰판입니다. */
internal class DeveloperPreferences(private val prefs: SharedPreferences) {
    val model: String get() = prefs.getString("model", MODELS[0])?.takeIf { it in MODELS } ?: MODELS[0]
    val effort: String get() = prefs.getString("effort", EFFORTS[0])?.takeIf { it in EFFORTS } ?: EFFORTS[0]

    fun setModel(value: String?) { if (value in MODELS) prefs.edit().putString("model", value).apply() }
    fun setEffort(value: String?) { if (value in EFFORTS) prefs.edit().putString("effort", value).apply() }
    fun setExecution(model: String?, effort: String?) {
        if (model in MODELS && effort in EFFORTS) prefs.edit().putString("model", model).putString("effort", effort).apply()
    }

    fun prompt(index: Int): String = prefs.getString("prompt-$index", "") ?: ""
    fun setPrompt(index: Int, value: String?) { prefs.edit().putString("prompt-$index", (value ?: "").take(4000)).apply() }
    fun thread(index: Int): String = prefs.getString("thread-$index", "") ?: ""
    fun setThread(index: Int, value: String?) { prefs.edit().putString("thread-$index", (value ?: "").take(160)).apply() }

    /** 저장된 주소가 있으면 그것을, 없으면 기본 주소를 돌려줍니다. 등록되지 않은 키는 null. */
    fun webAddress(key: String): String? {
        val stored = if (LINK_NAMES.containsKey(key)) prefs.getString("link-$key", null) else null
        return stored?.takeIf { it.isNotBlank() } ?: BUILT_IN_LINKS[key]
    }

    @Throws(IllegalArgumentException::class)
    fun setWebAddress(key: String, value: String?) {
        if (!LINK_NAMES.containsKey(key)) throw IllegalArgumentException("저장할 수 없는 바로가기입니다.")
        val text = value?.trim() ?: ""
        if (text.isEmpty()) { prefs.edit().remove("link-$key").apply(); return }
        val uri = try { URI(text) } catch (_: Exception) { null }
        val schemes = if (key == "web-ha") setOf("http", "https") else setOf("https")
        if (uri == null || !uri.isAbsolute || uri.scheme !in schemes || uri.host.isNullOrEmpty() || uri.userInfo != null || text.length > 1500)
            throw IllegalArgumentException("사용할 웹 주소를 https:// 형식으로 입력하세요. Home Assistant 는 http:// 도 사용할 수 있습니다.")
        prefs.edit().putString("link-$key", text).apply()
    }

    companion object {
        val MODELS = listOf("앱 기본 모델", "GPT-6.2 Sol", "GPT-6.2 Astra")
        val EFFORTS = listOf("앱 기본값", "Low", "Medium", "High", "Extra High")
        val LINK_NAMES = linkedMapOf(
            "web-work-cycle" to "Work-cycle", "web-nblog" to "NBlog", "web-kmovement" to "KMovement",
            "web-portfolio" to "포트폴리오", "web-ha" to "Home Assistant", "web-preview" to "배포 프리뷰"
        )
        val BUILT_IN_LINKS = mapOf(
            "web-work-cycle" to "https://work-cycle.evolvix.workers.dev/",
            "web-github" to "https://github.com/", "web-drive" to "https://drive.google.com/",
            "web-calendar" to "https://calendar.google.com/", "web-chatgpt" to "https://chatgpt.com/",
            "web-claude" to "https://claude.ai/", "web-youtube" to "https://www.youtube.com/", "web-naver" to "https://blog.naver.com/",
            "usage-codex" to "https://chatgpt.com/codex/settings/usage", "usage-claude" to "https://claude.ai/settings/usage",
            "codex-help" to "https://learn.chatgpt.com/docs/reference/commands", "weather-source" to "https://open-meteo.com/",
            "codex-guide" to "https://learn.chatgpt.com/docs/models", "codex-release" to "https://openai.com/index/introducing-gpt-6-2-astra-sol/",
            "codex-auth" to "https://learn.chatgpt.com/docs/auth", "claude-auth" to "https://code.claude.com/docs/en/authentication",
            "play-chatgpt" to "https://play.google.com/store/apps/details?id=com.openai.chatgpt",
            "play-claude" to "https://play.google.com/store/apps/details?id=com.anthropic.claude"
        )
        /** 데스크톱 CodexModelComparison 의 기본 비교 자료(2026-11-27). 휴대폰은 공식 문서 조회 없이 이 자료만 표시합니다. */
        const val MODEL_DATE = "2026-11-27"
        val MODEL_ROWS = listOf(
            Row("gpt-6.2-astra", "GPT-6.2 Astra", "여러 서비스에 걸친 장애 추적, 복잡한 코드 수정과 검증", "깊은 추론에 적합합니다. 표준 속도에서 6.2 Sol보다 토큰당 크레딧을 더 사용합니다.", 272000, true),
            Row("gpt-6.2-sol", "GPT-6.2 Sol", "일상 코딩, 코드 편집, 문서 작성, 여러 자료 요약", "일상 작업에 적합합니다. 표준 속도에서 6.2 Astra보다 토큰당 크레딧을 적게 사용합니다.", 500000, true),
            Row("gpt-6.1-sol", "GPT-6.1 Sol", "코드와 문서를 오가는 반복 작업과 장시간 작업", "이전 Sol 계열로, 긴 문맥을 유지하는 작업의 비교 대상입니다.", 500000, false),
            Row("gpt-6.1-terra", "GPT-6.1 Terra", "문서 작성, 화면·차트 해석, 앱에서 자료 확인", "빠른 응답과 시각 자료 이해를 중심으로 설계됐습니다.", null, false),
            Row("gpt-6-astra", "GPT-6 Astra", "코드·앱·리서치를 아우르는 복잡한 작업", "이전 Astra 계열의 심층 작업용 모델입니다.", null, false),
            Row("gpt-6-luna", "GPT-6 Luna", "범위가 명확한 반복 작업, 요약, 데이터 추출", "간결하고 반복되는 작업에 효율적인 선택입니다.", null, false)
        )
    }

    class Row(val id: String, val title: String, val work: String, val tradeoff: String, val context: Int?, val latest: Boolean)
}
