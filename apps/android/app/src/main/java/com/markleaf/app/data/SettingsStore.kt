package com.markleaf.app.data

import android.content.Context
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.runBlocking

/** 应用设置（spec §6）：主题/编码默认值/列表标记/状态栏/最近文件。 */
data class AppSettings(
    val themeMode: String = "system",     // system | light | dark
    val defaultEncoding: String = "UTF-8",
    val bulletMarker: String = "dash",    // dash | asterisk | plus
    val showStatusBar: Boolean = true,
    val recentFiles: List<String> = emptyList(),
)

private val Context.dataStore by preferencesDataStore(name = "markleaf-settings")

/** 最近文件列表序列化：JSON 数组保序。 */
private fun encodeRecentFiles(files: List<String>): String =
    if (files.isEmpty()) "[]" else files.joinToString(",", "[", "]") { file ->
            kotlinx.serialization.json.JsonPrimitive(file).toString()
        }

private fun decodeRecentFiles(value: String?): List<String> =
    if (value.isNullOrEmpty()) {
        emptyList()
    } else {
        runCatching {
            kotlinx.serialization.json.Json.decodeFromString<List<String>>(value)
        }.getOrElse { emptyList() }
    }

/** DataStore Preferences 存储。所有 IO 在 DataStore 自己的 IO 调度器。 */
class SettingsStore(private val context: Context) {

    private object Keys {
        val themeMode = stringPreferencesKey("themeMode")
        val defaultEncoding = stringPreferencesKey("defaultEncoding")
        val bulletMarker = stringPreferencesKey("bulletMarker")
        val showStatusBar = androidx.datastore.preferences.core.booleanPreferencesKey("showStatusBar")
        val recentFiles = stringPreferencesKey("recentFiles")
    }

    /** 非响应式读取一次（启动/设置页用）。 */
    fun readOnce(): AppSettings = runBlocking { read() }

    suspend fun read(): AppSettings {
        val prefs = context.dataStore.data.first()
        return AppSettings(
            themeMode = prefs[Keys.themeMode] ?: "system",
            defaultEncoding = prefs[Keys.defaultEncoding] ?: "UTF-8",
            bulletMarker = prefs[Keys.bulletMarker] ?: "dash",
            showStatusBar = (prefs[Keys.showStatusBar] ?: true),
            recentFiles = decodeRecentFiles(prefs[Keys.recentFiles]),
        )
    }

    suspend fun update(transform: (AppSettings) -> AppSettings) {
        context.dataStore.edit { prefs ->
            val current = AppSettings(
                themeMode = prefs[Keys.themeMode] ?: "system",
                defaultEncoding = prefs[Keys.defaultEncoding] ?: "UTF-8",
                bulletMarker = prefs[Keys.bulletMarker] ?: "dash",
                showStatusBar = (prefs[Keys.showStatusBar] ?: true),
                recentFiles = decodeRecentFiles(prefs[Keys.recentFiles]),
            )
            val next = transform(current)
            prefs[Keys.themeMode] = next.themeMode
            prefs[Keys.defaultEncoding] = next.defaultEncoding
            prefs[Keys.bulletMarker] = next.bulletMarker
            prefs[Keys.showStatusBar] = next.showStatusBar
            prefs[Keys.recentFiles] = encodeRecentFiles(next.recentFiles)
        }
    }

    /** 最近文件：置顶、去重、上限 10。 */
    suspend fun addRecentFile(documentId: String) {
        update { it.copy(recentFiles = (listOf(documentId) + it.recentFiles).distinct().take(10)) }
    }

    /** 测试隔离：清空全部设置。 */
    suspend fun clearAllForTest() {
        context.dataStore.edit { it.clear() }
    }
}
