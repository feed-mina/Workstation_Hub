package com.minadesk.workstationhub

import android.app.Application

/** 프로세스당 하나의 Hub 를 들고 있어 탭 전환·화면 회전 뒤에도 상태(모드·기록·HA 폴링)가 이어집니다. */
class HubApplication : Application() {
    internal lateinit var hub: Hub
        private set

    override fun onCreate() {
        super.onCreate()
        hub = Hub(this)
    }
}
