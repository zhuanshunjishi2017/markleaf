@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app.ui

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
    onDismiss: () -> Unit,
) {
    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
        Column(modifier = Modifier.fillMaxSize()) {
            // 退出走系统返回手势（BackHandler 已接管），不再放页内返回按钮
            Text(
                "首选项",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier
                    .statusBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
            Column(modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp)) {
                SectionTitle("排版")
                StepperRow(
                    label = "字号",
                    value = "${prefs.fontSize}",
                    onMinus = { onChange(prefs.copy(fontSize = (prefs.fontSize - 1).coerceAtLeast(12))) },
                    onPlus = { onChange(prefs.copy(fontSize = (prefs.fontSize + 1).coerceAtMost(24))) },
                )
                ChipRow(
                    label = "行高",
                    options = listOf("1.5", "1.75", "2.0"),
                    selected = prefs.lineHeight.toString(),
                    onSelect = { onChange(prefs.copy(lineHeight = it.toDouble())) },
                )
                ChipRow(
                    label = "内容宽度",
                    options = listOf("740", "820", "980", "不限"),
                    selected = if (prefs.ignoreMaxWidth) "不限" else prefs.maxWidth.toString(),
                    onSelect = {
                        if (it == "不限") onChange(prefs.copy(ignoreMaxWidth = true))
                        else onChange(prefs.copy(ignoreMaxWidth = false, maxWidth = it.toInt()))
                    },
                )
                StepperRow(
                    label = "源码字号",
                    value = "${prefs.sourceFontSize}",
                    onMinus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize - 1).coerceAtLeast(12))) },
                    onPlus = { onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize + 1).coerceAtMost(20))) },
                )
                ChipRow(
                    label = "源码字体",
                    options = EditorPrefs.SOURCE_FONT_FAMILIES.map { it.second },
                    selected = EditorPrefs.SOURCE_FONT_FAMILIES
                        .firstOrNull { it.first == prefs.sourceFontFamily }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.SOURCE_FONT_FAMILIES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(sourceFontFamily = it.first)) }
                    },
                )
                ChipRow(
                    label = "中文排版语言",
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
                    label = "中西文之间自动加空格",
                    checked = prefs.cjkSpacing,
                    onChange = { onChange(prefs.copy(cjkSpacing = it)) },
                )
                SwitchRow(
                    label = "西文两端对齐",
                    checked = prefs.adaptiveJustification,
                    onChange = { onChange(prefs.copy(adaptiveJustification = it)) },
                )
                SwitchRow(
                    label = "允许拖拽调整表格列宽",
                    checked = prefs.tableColumnResize,
                    onChange = { onChange(prefs.copy(tableColumnResize = it)) },
                )

                SectionTitle("外观")
                ChipRow(
                    label = "主题色",
                    options = themeIds.map { it.second },
                    selected = themeIds.firstOrNull { it.first == prefs.colorTheme }?.second ?: "",
                    onSelect = { display ->
                        themeIds.firstOrNull { it.second == display }?.let { onChange(prefs.copy(colorTheme = it.first)) }
                    },
                )

                SectionTitle("视图")
                StepperRow(
                    label = "缩放",
                    value = "${prefs.zoom}%",
                    step = 10,
                    onMinus = { onChange(prefs.copy(zoom = (prefs.zoom - 10).coerceAtLeast(50))) },
                    onPlus = { onChange(prefs.copy(zoom = (prefs.zoom + 10).coerceAtMost(200))) },
                )

                SectionTitle("主题")
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

                SectionTitle("通用")
                SwitchRow(
                    label = "显示代码高亮",
                    checked = prefs.codeHighlight,
                    onChange = { onChange(prefs.copy(codeHighlight = it)) },
                )
                SwitchRow(
                    label = "自动保存",
                    checked = prefs.autoSave,
                    onChange = { onChange(prefs.copy(autoSave = it)) },
                )
                SwitchRow(
                    label = "切换文档时自动保存",
                    checked = prefs.saveOnSwitch,
                    onChange = { onChange(prefs.copy(saveOnSwitch = it)) },
                )

                SectionTitle("文件")
                ChipRow(
                    label = "默认编码",
                    options = EditorPrefs.ENCODING_CHOICES,
                    selected = prefs.defaultEncoding,
                    onSelect = { onChange(prefs.copy(defaultEncoding = it)) },
                )
                ChipRow(
                    label = "默认换行符",
                    options = EditorPrefs.NEWLINE_CHOICES,
                    selected = prefs.defaultNewline,
                    onSelect = { onChange(prefs.copy(defaultNewline = it)) },
                )
                Text(
                    "更多设置（颜色主题自定义、显示语言等）将在后续版本提供",
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
        title = { Text("关于 MarkLeaf") },
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
        confirmButton = { TextButton(onClick = onDismiss) { Text("关闭") } },
    )
}
