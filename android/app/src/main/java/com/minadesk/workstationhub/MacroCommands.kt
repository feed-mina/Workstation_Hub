package com.minadesk.workstationhub

import android.content.ActivityNotFoundException
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioManager
import android.net.Uri
import android.view.KeyEvent

/**
 * 매크로 패드 버튼 중 휴대폰에서 실제로 실행할 수 있는 명령.
 * PC 잠금·스크린샷·터미널·VS Code·OBS 녹화는 Windows 허브 전용이며 mobile.css 가 해당 버튼을 숨깁니다.
 */
internal class MacroCommands(private val context: Context) {
    private val audio = context.getSystemService(Context.AUDIO_SERVICE) as AudioManager

    /** 미디어 스트림의 음소거 상태. 읽을 수 없으면 null(화면에는 '상태 확인 대기'). */
    fun muted(): Boolean? = try { audio.isStreamMute(AudioManager.STREAM_MUSIC) } catch (_: Exception) { null }

    fun toggleMute(): Boolean? = try {
        audio.adjustStreamVolume(AudioManager.STREAM_MUSIC, AudioManager.ADJUST_TOGGLE_MUTE, 0)
        audio.isStreamMute(AudioManager.STREAM_MUSIC)
    } catch (_: SecurityException) { null }

    /** 현재 재생 중인 앱에 재생/일시정지 미디어 키를 보냅니다. 재생 상태 확인은 하지 않습니다. */
    fun playPause(): String {
        val down = KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_MEDIA_PLAY_PAUSE)
        audio.dispatchMediaKeyEvent(down)
        audio.dispatchMediaKeyEvent(KeyEvent.changeAction(down, KeyEvent.ACTION_UP))
        return if (audio.isMusicActive) "재생 / 정지 · 재생 중인 앱에 미디어 키 전송" else "재생 / 정지 · 미디어 키 전송 · 재생 중인 앱이 없으면 반응이 없습니다."
    }

    fun isInstalled(packageName: String): Boolean = try {
        context.packageManager.getPackageInfo(packageName, 0); true
    } catch (_: PackageManager.NameNotFoundException) { false }

    /** 설치된 앱을 열고, 없으면 웹 주소를 기본 브라우저로 엽니다. 결과 문구를 돌려줍니다. */
    fun openApp(packageName: String, fallback: String, label: String): String {
        val launch = context.packageManager.getLaunchIntentForPackage(packageName)
        if (launch != null) {
            launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
            try { context.startActivity(launch); return "$label 앱 열기 요청됨" } catch (_: ActivityNotFoundException) { }
        }
        return openUrl(fallback, label)
    }

    fun openUrl(address: String, label: String): String {
        val uri = Uri.parse(address)
        if (uri.scheme != "https" && uri.scheme != "http") return "$label · 열 수 없는 주소입니다."
        val intent = Intent(Intent.ACTION_VIEW, uri).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        return try { context.startActivity(intent); "$label · 웹 화면 열기 요청됨" } catch (_: ActivityNotFoundException) { "$label · 열 수 있는 브라우저가 없습니다." }
    }

    fun openSystem(action: String, label: String): String = try {
        context.startActivity(Intent(action).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); "$label 열기 요청됨"
    } catch (_: ActivityNotFoundException) { "$label · 이 기기에서 열 수 없습니다." }

    companion object {
        const val CHATGPT = "com.openai.chatgpt"
        const val CLAUDE = "com.anthropic.claude"
        const val YOUTUBE_UPLOAD = "https://www.youtube.com/upload"
        const val YOUTUBE = "https://www.youtube.com/"
        const val YOUTUBE_MUSIC = "https://music.youtube.com/"
        const val NAVER_WRITE = "https://blog.naver.com/GoBlogWrite.naver"
    }
}
