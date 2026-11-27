package com.minadesk.workstationhub

import org.json.JSONArray
import org.json.JSONObject

/** 데스크톱의 익명 객체 `new {kind="state",...}` 에 해당하는 짧은 JSON 작성 도우미. null 은 JSON null 로 보냅니다. */
internal fun obj(vararg pairs: Pair<String, Any?>): JSONObject {
    val json = JSONObject()
    for ((key, value) in pairs) json.put(key, wrap(value))
    return json
}

internal fun arr(items: Iterable<Any?>): JSONArray {
    val json = JSONArray()
    for (item in items) json.put(wrap(item))
    return json
}

private fun wrap(value: Any?): Any = when (value) {
    null -> JSONObject.NULL
    is JSONObject, is JSONArray, is String, is Boolean, is Int, is Long -> value
    is Double -> if (value.isNaN() || value.isInfinite()) JSONObject.NULL else value
    is Float -> if (value.isNaN() || value.isInfinite()) JSONObject.NULL else value.toDouble()
    is Number -> value
    is Map<*, *> -> JSONObject().also { for ((k, v) in value) it.put(k.toString(), wrap(v)) }
    is Iterable<*> -> arr(value)
    is Array<*> -> arr(value.asIterable())
    else -> value.toString()
}

/** 문자열 값이 없거나 null 이면 null. `JSONObject.optString` 은 "null" 문자열을 돌려주므로 쓰지 않습니다. */
internal fun JSONObject.text(key: String): String? {
    if (!has(key) || isNull(key)) return null
    return get(key) as? String
}

internal fun JSONObject.integer(key: String): Int? {
    if (!has(key) || isNull(key)) return null
    return when (val value = get(key)) {
        is Int -> value
        is Long -> if (value in Int.MIN_VALUE..Int.MAX_VALUE) value.toInt() else null
        is Double -> if (value == Math.floor(value) && !value.isInfinite()) value.toInt() else null
        else -> null
    }
}

internal fun JSONArray.strings(): List<String> = (0 until length()).mapNotNull { optString(it, null) }
