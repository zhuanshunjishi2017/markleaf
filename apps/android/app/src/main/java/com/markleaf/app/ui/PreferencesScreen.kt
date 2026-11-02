@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app.ui

import com.markleaf.app.L10n
import com.markleaf.app.editor.StyleAssets

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.RadioButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** 偏好设置层（单页分组，字段对齐桌面 AppSettings 默认值）。分组与桌面偏好设置
 *  页签同构：文件 / 编辑器 / 外观 / 通用 / 图片，组内小节标题也沿用桌面页内
 *  header（保存选项、文本格式、Markdown 行为…）。改值即回调 onChange（宿主保存并应用）。 */
@Composable
fun PreferencesScreen(
    prefs: EditorPrefs,
    styleIds: List<Pair<String, String>>,
    colorThemes: List<StyleAssets.ColorTheme>,
    onChange: (EditorPrefs) -> Unit,
    onClearHistory: () -> Unit,
    onDismiss: () -> Unit,
) {
    val lightThemes = colorThemes.filter { !it.isDark }
    val darkThemes = colorThemes.filter { it.isDark }
    fun themeDisplay(id: String): String =
        L10n.t(colorThemes.firstOrNull { it.id == id }?.name ?: id)

    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
        Column(modifier = Modifier.fillMaxSize()) {
            // 退出走系统返回手势（BackHandler 已接管），不再放页内返回按钮
            Text(
                L10n.t("首选项"),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier
                    .statusBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
            Column(modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp)) {
                // ===== 文件（对齐桌面「文件」页签）=====
                SectionTitle(L10n.t("文件"))
                GroupHeader(L10n.t("保存选项"))
                SwitchRow(
                    label = L10n.t("自动保存"),
                    checked = prefs.autoSave,
                    onChange = { onChange(prefs.copy(autoSave = it)) },
                )
                SwitchRow(
                    label = L10n.t("切换文档时自动保存"),
                    checked = prefs.saveOnSwitch,
                    onChange = { onChange(prefs.copy(saveOnSwitch = it)) },
                )
                StepperRow(
                    label = L10n.t("快照保存间隔"),
                    value = "${prefs.snapshotInterval} ${L10n.t("秒")}",
                    step = 10,
                    onMinus = { onChange(prefs.copy(snapshotInterval = (prefs.snapshotInterval - 10).coerceAtLeast(10))) },
                    onPlus = { onChange(prefs.copy(snapshotInterval = (prefs.snapshotInterval + 10).coerceAtMost(300))) },
                )
                GroupHeader(L10n.t("文本格式"))
                ChipRow(
                    label = L10n.t("默认编码"),
                    options = EditorPrefs.ENCODING_CHOICES,
                    selected = prefs.defaultEncoding,
                    onSelect = { onChange(prefs.copy(defaultEncoding = it)) },
                )
                ChipRow(
                    label = L10n.t("默认换行符"),
                    options = EditorPrefs.NEWLINE_CHOICES,
                    selected = prefs.defaultNewline,
                    onSelect = { onChange(prefs.copy(defaultNewline = it)) },
                )
                GroupHeader(L10n.t("历史记录"))
                SwitchRow(
                    label = L10n.t("记录最近文件"),
                    checked = prefs.recordRecent,
                    onChange = { onChange(prefs.copy(recordRecent = it)) },
                )
                SwitchRow(
                    label = L10n.t("记录最近文件夹"),
                    checked = prefs.recordRecentFolders,
                    onChange = { onChange(prefs.copy(recordRecentFolders = it)) },
                )
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    TextButton(onClick = onClearHistory) { Text(L10n.t("清除历史记录…")) }
                }

                // ===== 编辑器（对齐桌面「编辑器」页签：可视化 / 源码模式 / Markdown 行为 / 缩放视图）=====
                SectionTitle(L10n.t("编辑器"))
                GroupHeader(L10n.t("可视化"))
                StepperRow(
                    label = L10n.t("字号"),
                    value = "${prefs.fontSize}",
                    onMinus = { onChange(prefs.copy(fontSize = (prefs.fontSize - 1).coerceAtLeast(12))) },
                    onPlus = { onChange(prefs.copy(fontSize = (prefs.fontSize + 1).coerceAtMost(24))) },
                )
                ChipRow(
                    label = L10n.t("行高"),
                    options = listOf("1.5", "1.75", "2.0"),
                    selected = prefs.lineHeight.toString(),
                    onSelect = { onChange(prefs.copy(lineHeight = it.toDouble())) },
                )
                ChipRow(
                    label = L10n.t("内容宽度"),
                    options = listOf("740", "820", "980", L10n.t("不限")),
                    selected = if (prefs.ignoreMaxWidth) L10n.t("不限") else prefs.maxWidth.toString(),
                    onSelect = {
                        if (it == L10n.t("不限")) onChange(prefs.copy(ignoreMaxWidth = true))
                        else onChange(prefs.copy(ignoreMaxWidth = false, maxWidth = it.toInt()))
                    },
                )
                ChipRow(
                    label = L10n.t("中文排版语言"),
                    options = EditorPrefs.CJK_LANGUAGES.map { it.second },
                    selected = EditorPrefs.CJK_LANGUAGES
                        .firstOrNull { it.first == prefs.cjkLanguage }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.CJK_LANGUAGES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(cjkLanguage = it.first)) }
                    },
                )
                SwitchRow(
                    label = L10n.t("中西文之间自动加空格"),
                    checked = prefs.cjkSpacing,
                    onChange = { onChange(prefs.copy(cjkSpacing = it)) },
                )
                SwitchRow(
                    label = L10n.t("西文两端对齐"),
                    checked = prefs.adaptiveJustification,
                    onChange = { onChange(prefs.copy(adaptiveJustification = it)) },
                )
                SwitchRow(
                    label = L10n.t("允许拖拽调整表格列宽"),
                    checked = prefs.tableColumnResize,
                    onChange = { onChange(prefs.copy(tableColumnResize = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示段落块手柄"),
                    checked = prefs.blockHandle,
                    onChange = { onChange(prefs.copy(blockHandle = it)) },
                )
                GroupHeader(L10n.t("源码模式"))
                StepperRow(
                    label = L10n.t("源码字号"),
                    value = "${prefs.sourceFontSize}",
                    onMinus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize - 1).coerceAtLeast(12))) },
                    onPlus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize + 1).coerceAtMost(20))) },
                )
                ChipRow(
                    label = L10n.t("源码字体"),
                    options = EditorPrefs.SOURCE_FONT_FAMILIES.map { L10n.t(it.second) },
                    selected = L10n.t(
                        EditorPrefs.SOURCE_FONT_FAMILIES
                            .firstOrNull { it.first == prefs.sourceFontFamily }?.second ?: "",
                    ),
                    onSelect = { display ->
                        EditorPrefs.SOURCE_FONT_FAMILIES
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(sourceFontFamily = it.first)) }
                    },
                )
                StepperRow(
                    label = L10n.t("源码缩进宽度"),
                    value = "${prefs.sourceIndent}",
                    onMinus = { onChange(prefs.copy(sourceIndent = (prefs.sourceIndent - 1).coerceAtLeast(1))) },
                    onPlus = { onChange(prefs.copy(sourceIndent = (prefs.sourceIndent + 1).coerceAtMost(8))) },
                )
                GroupHeader(L10n.t("Markdown 行为"))
                SwitchRow(
                    label = L10n.t("转义文本中的 Markdown 符号"),
                    checked = prefs.escapeLiteralSymbols,
                    onChange = { onChange(prefs.copy(escapeLiteralSymbols = it)) },
                )
                SwitchRow(
                    label = L10n.t("转义 Markdown 字面量符号"),
                    checked = prefs.escapeMarkdownLiteralSymbols,
                    onChange = { onChange(prefs.copy(escapeMarkdownLiteralSymbols = it)) },
                )
                SwitchRow(
                    label = L10n.t("代码块拼写检查"),
                    checked = prefs.codeBlockSpellcheck,
                    onChange = { onChange(prefs.copy(codeBlockSpellcheck = it)) },
                )
                SwitchRow(
                    label = L10n.t("空行回车退出块"),
                    checked = prefs.exitBlockOnEmptyEnter,
                    onChange = { onChange(prefs.copy(exitBlockOnEmptyEnter = it)) },
                )
                SwitchRow(
                    label = L10n.t("Shift+Enter 插入硬换行"),
                    checked = prefs.useShiftEnterHardBreak,
                    onChange = { onChange(prefs.copy(useShiftEnterHardBreak = it)) },
                )
                ChipRow(
                    label = L10n.t("代码围栏"),
                    options = EditorPrefs.CODE_FENCES.map { L10n.t(it.second) },
                    selected = L10n.t(
                        EditorPrefs.CODE_FENCES.firstOrNull { it.first == prefs.codeFence }?.second ?: "",
                    ),
                    onSelect = { display ->
                        EditorPrefs.CODE_FENCES
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(codeFence = it.first)) }
                    },
                )
                ChipRow(
                    label = L10n.t("强调标记"),
                    options = EditorPrefs.EMPHASIS_MARKERS.map { L10n.t(it.second) },
                    selected = L10n.t(
                        EditorPrefs.EMPHASIS_MARKERS
                            .firstOrNull { it.first == prefs.emphasisMarker }?.second ?: "",
                    ),
                    onSelect = { display ->
                        EditorPrefs.EMPHASIS_MARKERS
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(emphasisMarker = it.first)) }
                    },
                )
                ChipRow(
                    label = L10n.t("列表标记"),
                    options = EditorPrefs.BULLET_MARKERS.map { L10n.t(it.second) },
                    selected = L10n.t(
                        EditorPrefs.BULLET_MARKERS
                            .firstOrNull { it.first == prefs.bulletMarker }?.second ?: "",
                    ),
                    onSelect = { display ->
                        EditorPrefs.BULLET_MARKERS
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(bulletMarker = it.first)) }
                    },
                )
                GroupHeader(L10n.t("缩放视图"))
                SwitchRow(
                    label = L10n.t("打开时还原上次的缩放比例"),
                    checked = prefs.restoreZoomOnOpen,
                    onChange = { onChange(prefs.copy(restoreZoomOnOpen = it)) },
                )
                SwitchRow(
                    label = L10n.t("使用双指捏合进行缩放"),
                    checked = prefs.pinchZoom,
                    onChange = { onChange(prefs.copy(pinchZoom = it)) },
                )
                StepperRow(
                    label = L10n.t("缩放"),
                    value = "${prefs.zoom}%",
                    step = 10,
                    onMinus = { onChange(prefs.copy(zoom = (prefs.zoom - 10).coerceAtLeast(50))) },
                    onPlus = { onChange(prefs.copy(zoom = (prefs.zoom + 10).coerceAtMost(200))) },
                )

                // ===== 外观（对齐桌面「外观」页签「文档外观」组）=====
                SectionTitle(L10n.t("外观"))
                GroupHeader(L10n.t("文档外观"))
                Text(
                    L10n.t("排版样式"),
                    style = MaterialTheme.typography.bodyLarge,
                    modifier = Modifier.padding(top = 4.dp),
                )
                styleIds.forEach { (id, display) ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onChange(prefs.copy(styleId = id)) }
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        RadioButton(selected = prefs.styleId == id, onClick = { onChange(prefs.copy(styleId = id)) })
                        Text(L10n.t(display), style = MaterialTheme.typography.bodyLarge)
                    }
                }
                // 与桌面一致：开启「与操作系统同步」时颜色主题被默认浅/深主题接管
                ChipRow(
                    label = L10n.t("颜色主题"),
                    options = colorThemes.map { L10n.t(it.name) },
                    selected = themeDisplay(prefs.colorTheme),
                    enabled = !prefs.followSystemTheme,
                    onSelect = { display ->
                        colorThemes.firstOrNull { L10n.t(it.name) == display }?.let { onChange(prefs.copy(colorTheme = it.id)) }
                    },
                )
                ChipRow(
                    label = L10n.t("默认浅色主题"),
                    options = lightThemes.map { L10n.t(it.name) },
                    selected = themeDisplay(prefs.defaultLightTheme),
                    enabled = prefs.followSystemTheme,
                    onSelect = { display ->
                        lightThemes.firstOrNull { L10n.t(it.name) == display }?.let { onChange(prefs.copy(defaultLightTheme = it.id)) }
                    },
                )
                ChipRow(
                    label = L10n.t("默认深色主题"),
                    options = darkThemes.map { L10n.t(it.name) },
                    selected = themeDisplay(prefs.defaultDarkTheme),
                    enabled = prefs.followSystemTheme,
                    onSelect = { display ->
                        darkThemes.firstOrNull { L10n.t(it.name) == display }?.let { onChange(prefs.copy(defaultDarkTheme = it.id)) }
                    },
                )
                SwitchRow(
                    label = L10n.t("与操作系统同步"),
                    checked = prefs.followSystemTheme,
                    onChange = { onChange(prefs.copy(followSystemTheme = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示代码高亮"),
                    checked = prefs.codeHighlight,
                    onChange = { onChange(prefs.copy(codeHighlight = it)) },
                )
                // 桌面外观页「状态栏」：自定义各字段可见性（侧栏切换/命令状态为桌面专属）
                GroupHeader(L10n.t("状态栏"))
                SwitchRow(
                    label = L10n.t("显示光标位置"),
                    checked = prefs.statusBarPosition,
                    onChange = { onChange(prefs.copy(statusBarPosition = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示块类型"),
                    checked = prefs.statusBarBlockType,
                    onChange = { onChange(prefs.copy(statusBarBlockType = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示编码"),
                    checked = prefs.statusBarEncoding,
                    onChange = { onChange(prefs.copy(statusBarEncoding = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示换行符"),
                    checked = prefs.statusBarNewline,
                    onChange = { onChange(prefs.copy(statusBarNewline = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示源码/可视化切换"),
                    checked = prefs.statusBarModeToggle,
                    onChange = { onChange(prefs.copy(statusBarModeToggle = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示缩放比例"),
                    checked = prefs.statusBarZoom,
                    onChange = { onChange(prefs.copy(statusBarZoom = it)) },
                )
                SwitchRow(
                    label = L10n.t("显示字符数"),
                    checked = prefs.statusBarWordCount,
                    onChange = { onChange(prefs.copy(statusBarWordCount = it)) },
                )

                // ===== 通用（对齐桌面「通用」页签「显示」组）=====
                SectionTitle(L10n.t("通用"))
                GroupHeader(L10n.t("显示"))
                ChipRow(
                    label = L10n.t("显示语言"),
                    options = EditorPrefs.DISPLAY_LANGUAGES.map { it.second },
                    selected = EditorPrefs.DISPLAY_LANGUAGES
                        .firstOrNull { it.first == prefs.displayLanguage }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.DISPLAY_LANGUAGES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(displayLanguage = it.first)) }
                    },
                )

                // ===== 图片（对齐桌面「图片」页签；Android 暂只接「./」前缀，
                //      图片始终复制到文档 assets 目录）=====
                SectionTitle(L10n.t("图片"))
                Text(
                    L10n.t("图片始终复制到文档目录下的 assets 文件夹"),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
                SwitchRow(
                    label = L10n.t("相对路径前加 \"./\""),
                    checked = prefs.prefixRelativeWithDotSlash,
                    onChange = { onChange(prefs.copy(prefixRelativeWithDotSlash = it)) },
                )

                Text(
                    L10n.t("更多设置（文件关联、图片处理等）将在后续版本提供"),
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(vertical = 16.dp),
                )
            }
        }
    }
}

@Composable
private fun SectionTitle(title: String) {
    Text(
        title,
        style = MaterialTheme.typography.titleSmall,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 18.dp, bottom = 6.dp),
    )
    HorizontalDivider()
}

/** 组内小节标题：沿用桌面偏好设置页内 header（字号更小、次级色）。 */
@Composable
private fun GroupHeader(title: String) {
    Text(
        title,
        style = MaterialTheme.typography.labelLarge,
        fontWeight = FontWeight.Medium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
    )
}

@Composable
private fun StepperRow(label: String, value: String, step: Int = 1, onMinus: () -> Unit, onPlus: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        TextButton(onClick = onMinus) { Text(if (step >= 10) "−" else "−") }
        Text(value, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(horizontal = 8.dp))
        TextButton(onClick = onPlus) { Text("+") }
    }
}

@Composable
private fun ChipRow(
    label: String,
    options: List<String>,
    selected: String,
    onSelect: (String) -> Unit,
    enabled: Boolean = true,
) {
    // 所有 chip 组统一走加权 FlowRow 列：各行的 chip 起始 x 对齐一致
    // （此前非 wrap 行的 chip 右贴边，与相邻 wrap 行左对齐混排错位）
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            label,
            modifier = Modifier.weight(1f),
            style = MaterialTheme.typography.bodyLarge,
            color = if (enabled) MaterialTheme.colorScheme.onSurface
            else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
        )
        androidx.compose.foundation.layout.FlowRow(
            modifier = Modifier.weight(2.2f),
            horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
            verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
        ) {
            options.forEach { option ->
                FilterChip(
                    selected = option == selected,
                    onClick = { onSelect(option) },
                    enabled = enabled,
                    label = { Text(option, style = MaterialTheme.typography.labelMedium) },
                )
            }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

/** 关于对话框（对齐桌面 App 菜单「关于 MarkLeaf」）。 */
@Composable
fun AboutDialog(version: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(L10n.t("关于 MarkLeaf")) },
        text = {
            Column {
                Text("MarkLeaf Android", style = MaterialTheme.typography.bodyLarge)
                Text(
                    "版本 $version",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) } },
    )
}
