package com.minadesk.workstationhub

internal object MobileDraft {
    fun compose(input: String, model: String, effort: String): String {
        val lines = ArrayList<String>()
        if (model != "앱 기본 모델") lines.add("선호 모델 · $model (앱에서 확인)")
        if (effort != "앱 기본값") lines.add("선호 추론 강도 · $effort (앱에서 확인)")
        val request = input.trim().take(4000)
        if (request.isNotEmpty()) lines.add(request)
        return lines.joinToString("\n\n")
    }
}
