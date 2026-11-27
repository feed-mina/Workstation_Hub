package com.minadesk.workstationhub

import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.IOException
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/**
 * 데스크톱 LocalData 와 같은 규칙: 임시 파일에 쓴 뒤 원자적으로 교체하고 `.bak` 백업을 남깁니다.
 * 손상된 파일은 백업을 읽고, 백업도 없으면 기본값으로 시작합니다.
 */
internal object LocalData {
    fun readObject(file: File, fallback: () -> JSONObject): JSONObject {
        for (candidate in listOf(file, File(file.path + ".bak"))) {
            if (!candidate.isFile) continue
            try {
                return JSONObject(candidate.readText(Charsets.UTF_8))
            } catch (_: Exception) { /* 다음 후보 */ }
        }
        return fallback()
    }

    fun readArray(file: File): JSONArray {
        for (candidate in listOf(file, File(file.path + ".bak"))) {
            if (!candidate.isFile) continue
            try {
                return JSONArray(candidate.readText(Charsets.UTF_8))
            } catch (_: Exception) { /* 다음 후보 */ }
        }
        return JSONArray()
    }

    @Throws(IOException::class)
    fun write(file: File, value: Any) {
        file.parentFile?.mkdirs()
        val temporary = File(file.path + ".tmp")
        temporary.writeText(value.toString(), Charsets.UTF_8)
        if (file.exists()) {
            val backup = File(file.path + ".bak")
            if (backup.exists() && !backup.delete()) throw IOException("백업 파일을 교체할 수 없습니다.")
            if (!file.renameTo(backup)) throw IOException("기존 설정을 백업하지 못했습니다.")
        }
        if (!temporary.renameTo(file)) throw IOException("설정 파일을 저장하지 못했습니다.")
    }
}

/** Windows DPAPI 대신 Android Keystore 의 AES-GCM 키로 Home Assistant 토큰을 이 기기 안에서만 보호합니다. */
internal object Secrets {
    private const val ALIAS = "hub-local-secrets"
    private const val STORE = "AndroidKeyStore"

    private fun key(): SecretKey {
        val store = KeyStore.getInstance(STORE).apply { load(null) }
        (store.getEntry(ALIAS, null) as? KeyStore.SecretKeyEntry)?.let { return it.secretKey }
        val generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE)
        generator.init(
            KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                .setKeySize(256)
                .build()
        )
        return generator.generateKey()
    }

    fun protect(text: String?): String {
        if (text.isNullOrEmpty()) return ""
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, key())
        val body = cipher.doFinal(text.toByteArray(Charsets.UTF_8))
        return Base64.encodeToString(cipher.iv, Base64.NO_WRAP) + ":" + Base64.encodeToString(body, Base64.NO_WRAP)
    }

    fun unprotect(stored: String?): String {
        if (stored.isNullOrEmpty()) return ""
        val parts = stored.split(':')
        if (parts.size != 2) return ""
        return try {
            val cipher = Cipher.getInstance("AES/GCM/NoPadding")
            cipher.init(Cipher.DECRYPT_MODE, key(), GCMParameterSpec(128, Base64.decode(parts[0], Base64.NO_WRAP)))
            String(cipher.doFinal(Base64.decode(parts[1], Base64.NO_WRAP)), Charsets.UTF_8)
        } catch (_: Exception) {
            "" // 키가 바뀐 경우(기기 초기화·백업 복원) 토큰을 다시 입력하도록 비웁니다.
        }
    }
}
