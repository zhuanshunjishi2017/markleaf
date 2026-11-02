@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app.ui

import com.markleaf.app.L10n

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

/** 偏好设置层（单页分组，字段对齐桌面 AppSettings 默认值）：
 *  排版 / 外观 / 视图 / 主题 / 文件。改值即回调 onChange（宿主保存并应用）。 */
@Composable
fun PreferencesScreen(
    prefs: EditorPrefs,
    styleIds: List<Pair<String, String>>,
    themeIds: List<Pair<String, String>>,
    onChange: (EditorPrefs) -> Unit,
    onClearHistory: () -> Unit,
    onDismiss: () -> Unit,
) {
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
                // 分组顺序对齐桌面偏好设置页签：文件 → 编辑器 → 外观
                // （通用/图片页 Android 暂无可接项）

                SectionTitle(L10n.t("文件"))
                // 桌面文件页「保存选项」
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
                // 桌面文件页「文本格式」
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
                // 桌面文件页「历史记录」组
                SwitchRow(
                    label = L10n.t("记录最近文件"),
                    checked = prefs.recordRecent,
                    onChange = { onChange(prefs.copy(recordRecent = it)) },
                )
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(L10n.t("历史记录"), modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
                    TextButton(onClick = onClearHistory) { Text(L10n.t("清除…")) }
                }

                SectionTitle(L10n.t("编辑器"))
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
                StepperRow(
                    label = L10n.t("源码字号"),
                    value = "${prefs.sourceFontSize}",
                    onMinus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize - 1).coerceAtLeast(12))) },
                    onPlus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize + 1).coerceAtMost(20))) },
                )
                ChipRow(
                    label = L10n.t("源码字体"),
                    options = EditorPrefs.SOURCE_FONT_FAMILIES.map { it.second },
                    selected = EditorPrefs.SOURCE_FONT_FAMILIES
                        .firstOrNull { it.first == prefs.sourceFontFamily }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.SOURCE_FONT_FAMILIES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(sourceFontFamily = it.first)) }
                    },
                )
                StepperRow(
                    label = L10n.t("源码缩进宽度"),
                    value = "${prefs.sourceIndent}",
                    onMinus = { onChange(prefs.copy(sourceIndent = (prefs.sourceIndent - 1).coerceAtLeast(1))) },
                    onPlus = { onChange(prefs.copy(sourceIndent = (prefs.sourceIndent + 1).coerceAtMost(8))) },
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

                // 桌面编辑器页「Markdown 行为」子窗口
                ChipRow(
                    label = L10n.t("代码块围栏"),
                    options = EditorPrefs.CODE_FENCES.map { it.second },
                    selected = EditorPrefs.CODE_FENCES
                        .firstOrNull { it.first == prefs.codeFence }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.CODE_FENCES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(codeFence = it.first)) }
                    },
                )
                ChipRow(
                    label = L10n.t("强调标记"),
                    options = EditorPrefs.EMPHASIS_MARKERS.map { it.second },
                    selected = EditorPrefs.EMPHASIS_MARKERS
                        .firstOrNull { it.first == prefs.emphasisMarker }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.EMPHASIS_MARKERS
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(emphasisMarker = it.first)) }
                    },
                )
                ChipRow(
                    label = L10n.t("列表符号"),
                    options = EditorPrefs.BULLET_MARKERS.map { it.second },
                    selected = EditorPrefs.BULLET_MARKERS
                        .firstOrNull { it.first == prefs.bulletMarker }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.BULLET_MARKERS
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(bulletMarker = it.first)) }
                    },
                )
                SwitchRow(
                    label = L10n.t("空回车退出块"),
                    checked = prefs.exitBlockOnEmptyEnter,
                    onChange = { onChange(prefs.copy(exitBlockOnEmptyEnter = it)) },
                )
                SwitchRow(
                    label = L10n.t("Shift+Enter 硬换行"),
                    checked = prefs.useShiftEnterHardBreak,
                    onChange = { onChange(prefs.copy(useShiftEnterHardBreak = it)) },
                )
                // 桌面编辑器页「缩放视图」组
                StepperRow(
                    label = L10n.t("缩放"),
                    value = "${prefs.zoom}%",
                    step = 10,
                    onMinus = { onChange(prefs.copy(zoom = (prefs.zoom - 10).coerceAtLeast(50))) },
                    onPlus = { onChange(prefs.copy(zoom = (prefs.zoom + 10).coerceAtMost(200))) },
                )

                SectionTitle(L10n.t("外观"))
                // 桌面外观页「文档外观」：排版样式 / 颜色主题 / 代码高亮
                styleIds.forEach { (id, display) ->
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .clickable { onChange(prefs.copy(styleId = id)) }
                            .padding(vertical = 4.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        RadioButton(selected = prefs.styleId == id, onClick = { onChange(prefs.copy(styleId = id)) })
                        Text(display, style = MaterialTheme.typography.bodyLarge)
                    }
                }
                ChipRow(
                    label = L10n.t("主题色"),
                    options = themeIds.map { it.second },
                    selected = themeIds.firstOrNull { it.first == prefs.colorTheme }?.second ?: "",
                    onSelect = { display ->
                        themeIds.firstOrNull { it.second == display }?.let { onChange(prefs.copy(colorTheme = it.first)) }
                    },
                )
                SwitchRow(
                    label = L10n.t("显示代码高亮"),
                    checked = prefs.codeHighlight,
                    onChange = { onChange(prefs.copy(codeHighlight = it)) },
                )

                // 桌面通用页「显示语言」：驱动宿主 L10n 与编辑器内核字符串
                SectionTitle(L10n.t("通用"))
                ChipRow(
                    label = L10n.t("编辑器语言"),
                    options = EditorPrefs.DISPLAY_LANGUAGES.map { it.second },
                    selected = EditorPrefs.DISPLAY_LANGUAGES
                        .firstOrNull { it.first == prefs.displayLanguage }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.DISPLAY_LANGUAGES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(displayLanguage = it.first)) }
                    },
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
) {
    // 所有 chip 组统一走加权 FlowRow 列：各行的 chip 起始 x 对齐一致
    // （此前非 wrap 行的 chip 右贴边，与相邻 wrap 行左对齐混排错位）
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        androidx.compose.foundation.layout.FlowRow(
            modifier = Modifier.weight(2.2f),
            horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
            verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
        ) {
            options.forEach { option ->
                FilterChip(
                    selected = option == selected,
                    onClick = { onSelect(option) },
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
