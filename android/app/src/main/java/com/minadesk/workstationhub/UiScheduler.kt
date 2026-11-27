package com.minadesk.workstationhub

import android.os.Handler
import android.os.Looper

/** Worker results return through this boundary; tests control delivery without an Android Looper. */
internal interface UiScheduler {
    fun post(action: () -> Unit)
    fun later(action: Runnable, delayMillis: Long)
    fun remove(action: Runnable)
}

internal class AndroidUiScheduler : UiScheduler {
    private val handler = Handler(Looper.getMainLooper())
    override fun post(action: () -> Unit) { handler.post { action() } }
    override fun later(action: Runnable, delayMillis: Long) { handler.postDelayed(action, delayMillis) }
    override fun remove(action: Runnable) { handler.removeCallbacks(action) }
}
