package com.markleaf.app.export

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * 导出对话框（对齐桌面 ExportWindowController 的选项集，移动端单列布局）：
 * 格式 + 文件名 + 纸张方向/页边距预设（PDF）/ 打印友好（PDF）/
 * 分页组：表格不跨页、标题不落页底（PDF）/ 页眉页脚预设（HTML）。
 * PDF 实际边距由 PrintManager MinMargins 应用；HTML 页眉/页脚由
 * generateExportHtml 渲染（{page}/{pages} 占位支持）。
 */
@Composable
fun ExportDialog(
    defaultName: String,
    onDismiss: () -> Unit,
    onExport: (name: String, options: ExportOptions) -> Unit,
) {
    var name by remember { mutableStateOf(ExportController.sanitizeFileName(defaultName)) }
    var format by remember { mutableStateOf(ExportFormat.PDF) }
    var printFriendly by remember { mutableStateOf(false) }
    var landscape by remember { mutableStateOf(false) }
    var marginPreset by remember { mutableStateOf(MARGIN_PRESETS.first()) }
    var keepTablesTogether by remember { mutableStateOf(true) }
    var keepHeadingsWithNextBlock by remember { mutableStateOf(true) }
    var headerPreset by remember { mutableStateOf("none") }
    var footerPreset by remember { mutableStateOf("none") }

    Column(modifier = Modifier.padding(16.dp)) {
        Text(text = "导出文档", style = MaterialTheme.typography.titleMedium)

        // 格式
        Row(modifier = Modifier.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            FilterChip(
                selected = format == ExportFormat.PDF,
                onClick = { format = ExportFormat.PDF },
                label = { Text("PDF") },
            )
            androidx.compose.foundation.layout.Spacer(modifier = Modifier.padding(4.dp))
            FilterChip(
                selected = format == ExportFormat.HTML,
                onClick = { format = ExportFormat.HTML },
                label = { Text("HTML") },
            )
        }

        OutlinedTextField(
            value = name,
            onValueChange = { name = it },
            label = { Text("文件名") },
            modifier = Modifier.fillMaxWidth(),
        )

        // PDF 专属：方向 / 页边距 / 打印友好 / 分页
        if (format == ExportFormat.PDF) {
            Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Switch(checked = landscape, onCheckedChange = { landscape = it })
                Text("横向", modifier = Modifier.padding(start = 8.dp))
            }
            OptionRow(label = "页边距") {
                MARGIN_PRESETS.forEach { preset ->
                    FilterChip(
                        selected = marginPreset.id == preset.id,
                        onClick = { marginPreset = preset },
                        label = { Text(preset.display, style = MaterialTheme.typography.labelMedium) },
                    )
                }
            }
            SwitchRow("表格不跨页", keepTablesTogether) { keepTablesTogether = it }
            SwitchRow("标题不落在页面最底部", keepHeadingsWithNextBlock) { keepHeadingsWithNextBlock = it }
            SwitchRow("打印友好（浅色）", printFriendly) { printFriendly = it }
        }

        // HTML 专属：页眉 / 页脚预设
        if (format == ExportFormat.HTML) {
            PresetRow("页眉", HEADER_PRESETS, headerPreset) { headerPreset = it }
            PresetRow("页脚", HEADER_PRESETS, footerPreset) { footerPreset = it }
        }

        HorizontalDivider(modifier = Modifier.padding(vertical = 8.dp))

        Row(modifier = Modifier.padding(vertical = 8.dp)) {
            Button(
                onClick = {
                    onExport(
                        ExportController.sanitizeFileName(name),
                        ExportOptions(
                            format = format,
                            landscape = landscape,
                            printFriendly = printFriendly,
                            marginPresetId = marginPreset.id,
                            keepTablesTogether = keepTablesTogether,
                            keepHeadingsWithNextBlock = keepHeadingsWithNextBlock,
                            headerPresetId = headerPreset,
                            footerPresetId = footerPreset,
                        ),
                    )
                },
            ) {
                Text("导出")
            }
            androidx.compose.material3.TextButton(onClick = onDismiss) { Text("取消") }
        }
    }
}

/** 页边距预设（对齐桌面 ExportWindowController.marginPresets）。 */
data class MarginPreset(val id: String, val display: String, val top: Int, val bottom: Int, val left: Int, val right: Int)

val MARGIN_PRESETS = listOf(
    MarginPreset("standard", "标准", 18, 18, 15, 15),
    MarginPreset("narrow", "窄", 10, 10, 10, 10),
    MarginPreset("wide", "宽", 25, 25, 20, 20),
    MarginPreset("none", "无", 0, 0, 0, 0),
)

/** 页眉/页脚预设（对齐桌面 headerFooterPresets；custom 依赖自定义文本框，移动端暂缓）。 */
val HEADER_PRESETS = listOf(
    "none" to "无",
    "title-left" to "标题（左对齐）",
    "page-center" to "页码（居中）",
    "page-right" to "页码（右对齐）",
    "page-total-center" to "页码/总页数（居中）",
)

@Composable
private fun OptionRow(label: String, content: @Composable () -> Unit) {
    Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(vertical = 2.dp))
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
    ) { content() }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Switch(checked = checked, onCheckedChange = { onChange(it) })
        Text(label, modifier = Modifier.padding(start = 8.dp))
    }
}

@Composable
private fun PresetRow(label: String, presets: List<Pair<String, String>>, selected: String, onSelect: (String) -> Unit) {
    Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(vertical = 2.dp))
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
    ) {
        presets.forEach { (id, display) ->
            FilterChip(
                selected = selected == id,
                onClick = { onSelect(id) },
                label = { Text(display, style = MaterialTheme.typography.labelMedium) },
            )
        }
    }
}
