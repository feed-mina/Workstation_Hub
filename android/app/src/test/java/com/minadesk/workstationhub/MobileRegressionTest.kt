package com.minadesk.workstationhub

import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File
import java.io.IOException
import java.util.concurrent.AbstractExecutorService
import java.util.concurrent.TimeUnit

class MobileRegressionTest {
    @get:Rule val temporary = TemporaryFolder()

    private class QueueWorker : AbstractExecutorService() {
        val tasks = ArrayDeque<Runnable>()
        private var stopped = false
        override fun execute(command: Runnable) { check(!stopped); tasks.addLast(command) }
        fun runNext() { tasks.removeFirst().run() }
        fun drain() { while (tasks.isNotEmpty()) runNext() }
        override fun shutdown() { stopped = true }
        override fun shutdownNow(): MutableList<Runnable> { stopped = true; val queued = tasks.toMutableList(); tasks.clear(); return queued }
        override fun isShutdown() = stopped
        override fun isTerminated() = stopped && tasks.isEmpty()
        override fun awaitTermination(timeout: Long, unit: TimeUnit) = isTerminated()
    }

    private class QueueUi : UiScheduler {
        val tasks = ArrayDeque<() -> Unit>()
        override fun post(action: () -> Unit) { tasks.addLast(action) }
        override fun later(action: Runnable, delayMillis: Long) {}
        override fun remove(action: Runnable) {}
        fun drain() { while (tasks.isNotEmpty()) tasks.removeFirst().invoke() }
    }

    private fun settings(name: String) = SmartSettings("https://$name.invalid", "fixture-token", mutableListOf(RoomBinding(id = "room", vacuum = "vacuum.$name")))
    private fun response(config: SmartSettings, route: String): JSONArray {
        val name = if (config.haAddress.contains("a.invalid")) "a" else "b"
        return if (route == "states") JSONArray().put(obj("entity_id" to "vacuum.$name", "state" to "docked", "attributes" to obj("supported_features" to 8212)))
        else JSONArray().put(obj("domain" to "vacuum", "services" to obj("start" to obj(), "pause" to obj(), "return_to_base" to obj())))
    }

    @Test fun lateOldConnectionCannotPublishIntoNewSettings() {
        val ui = QueueUi(); val work = QueueWorker()
        lateinit var smart: SmartHome
        var replaceDuringRequest = true
        val calls = ArrayList<String>()
        smart = SmartHome(temporary.newFolder(), ui, work, {
            HaClient { config, route, _ ->
                calls.add(config.haAddress + "/" + route)
                if (route == "states" && replaceDuringRequest) {
                    replaceDuringRequest = false
                    smart.save(settings("b"))
                }
                response(config, route)
            }
        }) {}
        smart.save(settings("a"))
        val old = smart.ha
        work.runNext() // B is saved while A's response is in flight.
        assertNotSame(old, smart.ha)
        assertTrue(old.state.entities.containsKey("vacuum.a"))
        assertFalse(smart.snapshot().getBoolean("connected"))
        assertFalse(smart.ha.state.can("vacuum.a", 8192))
        ui.drain() // A's late completion cannot clear B's pending refresh.
        smart.refresh()
        assertEquals(1, work.tasks.size)
        work.drain(); ui.drain()
        assertEquals("https://b.invalid", smart.settings.haAddress)
        assertTrue(smart.ha.state.can("vacuum.b", 8192))
        assertFalse(smart.ha.state.entities.containsKey("vacuum.a"))
        assertEquals(4, calls.size)
    }

    @Test fun supersededQueuedRefreshIsSkippedAndColdCommandsFail() {
        val ui = QueueUi(); val work = QueueWorker(); val calls = ArrayList<String>()
        val smart = SmartHome(temporary.newFolder(), ui, work, {
            HaClient { config, route, _ -> calls.add(config.haAddress); response(config, route) }
        }) {}
        smart.save(settings("a")); smart.save(settings("b"))
        try { smart.ha.execute(settings("b"), settings("b").rooms[0], "start"); fail("cold state must reject commands") }
        catch (_: IllegalStateException) {}
        assertTrue(calls.isEmpty())
        work.drain(); ui.drain()
        assertEquals(listOf("https://b.invalid", "https://b.invalid"), calls)
    }

    @Test fun failedRefreshClearsCapabilitiesAndDoesNotSendCommand() {
        var offline = false; var posts = 0
        val client = HaClient { config, route, body ->
            if (body != null) posts++
            if (offline) throw IOException("offline")
            response(config, route)
        }
        client.refresh(settings("a")); assertTrue(client.state.can("vacuum.a", 8192))
        offline = true; client.refresh(settings("a"))
        assertNull(client.state.updatedUtc); assertTrue(client.state.entities.isEmpty())
        try { client.execute(settings("a"), settings("a").rooms[0], "start"); fail("offline command") }
        catch (_: IllegalStateException) {}
        assertEquals(0, posts)
    }

    @Test fun staleAndFutureStateCannotEnableDevices() {
        val entities = mapOf("vacuum.a" to HaEntity("vacuum.a", "A", "docked", 8192))
        for (at in listOf(System.currentTimeMillis() - 21_000, System.currentTimeMillis() + 60_000)) {
            val state = HaSnapshot(entities, setOf("vacuum.start"), "connected", at)
            assertFalse(state.can("vacuum.a", 8192))
            assertFalse(state.view("vacuum.a")!!.getBoolean("available"))
        }
    }

    @Test fun settingsCannotChangeWhileACommandIsPending() {
        val ui = QueueUi(); val work = QueueWorker()
        val smart = SmartHome(temporary.newFolder(), ui, work, { HaClient { config, route, _ -> response(config, route) } }) {}
        smart.save(settings("a")); work.drain(); ui.drain()
        var result = ""
        smart.execute("start", "room", null) { result = it }
        try { smart.save(settings("b")); fail("settings changed during command") }
        catch (_: IllegalStateException) {}
        assertEquals("https://a.invalid", smart.settings.haAddress)
        work.drain(); ui.drain()
        assertFalse(smart.executing); assertTrue(result.contains("요청 완료"))
    }

    @Test fun probeIsIsolatedAndRepeatedClicksCoalesce() {
        val ui = QueueUi(); val work = QueueWorker(); var requests = 0; var completed = 0
        val operating = HaClient { config, route, _ -> response(config, route) }
        operating.refresh(settings("a"))
        val original = operating.state
        val probe = ConnectionProbe(ui, work) { HaClient { config, route, _ -> requests++; response(config, route) } }
        assertTrue(probe.start(settings("b")) { completed++ })
        repeat(10) { assertFalse(probe.start(settings("b")) { completed++ }) }
        assertEquals(1, work.tasks.size)
        work.drain(); ui.drain()
        assertSame(original, operating.state)
        assertTrue(probe.state!!.entities.containsKey("vacuum.b"))
        assertEquals(2, requests); assertEquals(1, completed); assertFalse(probe.busy)
        assertTrue(probe.start(settings("a")) { completed++ })
        work.drain(); ui.drain(); assertEquals(2, completed)
        probe.close()
    }

    @Test fun closingProbeCancelsQueuedWorkAndSuppressesLateUiResult() {
        val ui = QueueUi(); val work = QueueWorker(); var callbacks = 0; var requests = 0
        val probe = ConnectionProbe(ui, work) { HaClient { config, route, _ -> requests++; response(config, route) } }
        probe.start(settings("a")) { callbacks++ }
        work.drain() // Result is queued on the UI thread, but the Activity closes first.
        probe.close(); probe.close(); ui.drain()
        assertEquals(0, callbacks); assertNull(probe.state)
        assertFalse(probe.start(settings("a")) { callbacks++ })
        val queued = QueueWorker()
        val other = ConnectionProbe(ui, queued) { HaClient { config, route, _ -> requests++; response(config, route) } }
        other.start(settings("b")) { callbacks++ }; other.close(); queued.drain(); ui.drain()
        assertEquals(2, requests); assertEquals(0, callbacks)
    }

    @Test fun historyReadsAndWritesAreDeferredAndRemainOrdered() {
        val ui = QueueUi(); val work = QueueWorker(); val folder = temporary.newFolder()
        var opened = false; var writes = 0
        val history = WorkHistoryStore(folder, "test phone", ui, work, { directory ->
            opened = true
            WorkHistory(directory) { file, value -> writes++; LocalData.write(file, value) }
        }) {}
        history.add("macro", "first"); history.add("macro", "second")
        assertFalse(opened); assertEquals(0, writes); assertTrue(history.snapshot.rows.isEmpty())
        work.drain(); assertEquals(2, writes); assertTrue(history.snapshot.rows.isEmpty())
        ui.drain()
        val saved = WorkHistory(folder).snapshot()
        assertEquals(listOf("first", "second"), saved.map { it.detail })
        assertEquals(saved.map { it.id }.toSet(), history.snapshot.rows.map { it.id }.toSet())
        assertTrue(saved.all { it.device == "test phone" })
    }

    @Test fun historyFailureIsReportedAndNextWriteRecovers() {
        val ui = QueueUi(); val work = QueueWorker(); val folder = temporary.newFolder(); var fail = true
        val history = WorkHistoryStore(folder, "phone", ui, work, { directory ->
            WorkHistory(directory) { file, value -> if (fail) throw IOException("full"); LocalData.write(file, value) }
        }) {}
        history.add("macro", "offline"); work.drain(); ui.drain()
        assertTrue(history.snapshot.status.contains("저장하지 못했습니다"))
        fail = false
        history.add("macro", "recovered"); work.drain(); ui.drain()
        assertEquals("이 휴대폰", history.snapshot.status)
        assertEquals(listOf("offline", "recovered"), WorkHistory(folder).snapshot().map { it.detail })
    }

    @Test fun historyKeepsFiveThousandAndPublishesOnlyRecentSixty() {
        val folder = temporary.newFolder()
        val rows = JSONArray()
        repeat(WorkHistory.RETENTION) { rows.put(WorkEntry("id-$it", it.toLong(), "phone", "macro", "$it").toJson()) }
        LocalData.write(File(folder, "work-history.json"), rows)
        val history = WorkHistory(folder)
        history.add(WorkEntry("new", 99999, "phone", "macro", "latest"))
        val stored = WorkHistory(folder).snapshot()
        assertEquals(5000, stored.size); assertFalse(stored.any { it.id == "id-0" })
        val recent = WorkHistory.recent(stored)
        assertEquals(60, recent.size); assertEquals("new", recent.last().id)
    }

    @Test fun finalDraftIncludesPreferencesAndKeepsTheEntireInputLimit() {
        val input = "한".repeat(4000)
        val draft = MobileDraft.compose(input, "GPT-6.2 Astra", "High")
        assertTrue(draft.startsWith("선호 모델 · GPT-6.2 Astra"))
        assertTrue(draft.contains("선호 추론 강도 · High"))
        assertTrue(draft.endsWith(input))
        assertEquals("request", MobileDraft.compose(" request ", "앱 기본 모델", "앱 기본값"))
        assertEquals("", MobileDraft.compose("", "앱 기본 모델", "앱 기본값"))
        assertEquals(4000, MobileDraft.compose(input + "extra", "앱 기본 모델", "앱 기본값").length)
    }
}
