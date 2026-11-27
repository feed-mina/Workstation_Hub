package com.minadesk.workstationhub

import java.util.concurrent.ExecutorService
import java.util.concurrent.Executors
import java.util.concurrent.Future

/** Activity-owned, single-flight connection test. Never publishes into SmartHome's operating client. */
internal class ConnectionProbe(
    private val ui: UiScheduler = AndroidUiScheduler(),
    private val worker: ExecutorService = Executors.newSingleThreadExecutor(),
    private val clientFactory: () -> HaClient = { HaClient() }
) : AutoCloseable {
    var state: HaSnapshot? = null
        private set
    var busy = false
        private set
    private var generation = 0
    private var closed = false
    private var client: HaClient? = null
    private var task: Future<*>? = null

    fun start(draft: SmartSettings, done: (HaSnapshot) -> Unit): Boolean {
        if (closed || busy) return false
        val settings = SmartSettings.from(draft.toJson())
        val probe = clientFactory()
        client = probe
        busy = true
        val request = ++generation
        task = worker.submit {
            probe.refresh(settings)
            val result = probe.state
            ui.post {
                if (!closed && request == generation) {
                    state = result
                    busy = false
                    done(result)
                }
            }
        }
        return true
    }

    override fun close() {
        if (closed) return
        closed = true
        generation++
        try { client?.cancel() } finally {
            task?.cancel(true)
            worker.shutdownNow()
            busy = false
        }
    }
}
