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
 * 导出页（对齐桌面 ExportWindowController 选项集，移动端单列布局）：
 * 格式（PDF/HTML/图片）+ 文件名 + 格式专属选项：
 * - PDF：纸张方向/页边距预设/分页组（表格不跨页、标题不落页底）/打印友好浅色
 * - HTML：页眉/页脚预设
 * - 图片：格式 PNG/JPEG、JPEG 质量、缩放比例
 * 实际边距由 PrintManager MinMargins 应用；页眉/页脚 {page}/{pages} 占位
 * 由前端 generateExportHtml 展开。
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
    var imageFormat by remember { mutableStateOf("png") }
    var imageJpegQuality by remember { mutableStateOf(90) }
    var imageScale by remember { mutableStateOf(2) }

    Column(modifier = Modifier.padding(16.dp)) {
        Text(text = "导出文档", style = MaterialTheme.typography.titleMedium)

        // 格式
        Row(modifier = Modifier.padding(vertical = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            listOf(ExportFormat.PDF to "PDF", ExportFormat.HTML to "HTML", ExportFormat.IMAGE to "图片").forEach { (f, label) ->
                FilterChip(
                    selected = format == f,
                    onClick = { format = f },
                    label = { Text(label) },
                )
                androidx.compose.foundation.layout.Spacer(modifier = Modifier.padding(3.dp))
            }
        }

        OutlinedTextField(
            value = name,
            onValueChange = { name = it },
            label = { Text("文件名") },
            modifier = Modifier.fillMaxWidth(),
        )

        // 页面行为（对齐桌面「页面行为」组；PDF/图片共用分页组，HTML 无分页概念）
        if (format != ExportFormat.HTML) {
            Text(
                "页面行为",
                style = MaterialTheme.typography.labelLarge,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Medium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
            )
            androidx.compose.material3.ListItem(
                headlineContent = { Text("尽量保持表格不跨页") },
                trailingContent = {
                    Switch(checked = keepTablesTogether, onCheckedChange = { keepTablesTogether = it })
                },
            )
            androidx.compose.material3.ListItem(
                headlineContent = { Text("尽量避免标题孤悬页尾") },
                trailingContent = {
                    Switch(checked = keepHeadingsWithNextBlock, onCheckedChange = { keepHeadingsWithNextBlock = it })
                },
            )
        }

        // PDF 专属：方向 / 页边距 / 打印友好
        if (format == ExportFormat.PDF) {
            Text(
                "页面设置",
                style = MaterialTheme.typography.labelLarge,
                fontWeight = androidx.compose.ui.text.font.FontWeight.Medium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
            )
            Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Switch(checked = landscape, onCheckedChange = { landscape = it })
                Text("横向", modifier = Modifier.padding(start = 8.dp))
            }
            ChipRowGroup(label = "页边距", presets = MARGIN_PRESETS_MAP, selectedId = marginPreset.id) { selectedId ->
                marginPreset = MARGIN_PRESETS.firstOrNull { it.id == selectedId } ?: marginPreset
            }
            SwitchRow("打印友好（浅色）", printFriendly) { printFriendly = it }
        }

        // 图片专属：格式 / 质量 / 缩放
        if (format == ExportFormat.IMAGE) {
            ChipRowGroup(label = "图片格式", presets = IMAGE_FORMATS, selectedId = imageFormat) { imageFormat = it }
            if (imageFormat == "jpeg") {
                Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("JPEG 质量", modifier = Modifier.weight(1f))
                    Text("$imageJpegQuality", modifier = Modifier.padding(horizontal = 8.dp))
                }
                androidx.compose.material3.Slider(
                    value = imageJpegQuality.toFloat(),
                    onValueChange = { imageJpegQuality = it.toInt() },
                    valueRange = 50f..100f,
                )
            }
            Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("缩放倍数", modifier = Modifier.weight(1f))
                Text("${imageScale}×", modifier = Modifier.padding(horizontal = 8.dp))
            }
            androidx.compose.material3.Slider(
                value = imageScale.toFloat(),
                onValueChange = { imageScale = it.toInt() },
                valueRange = 1f..4f,
                steps = 5,
            )
        }

        // HTML 专属：页眉 / 页脚预设
        if (format == ExportFormat.HTML) {
            ChipRowGroup(label = "页眉", presets = HEADER_PRESET_MAP, selectedId = headerPreset) { headerPreset = it }
            ChipRowGroup(label = "页脚", presets = HEADER_PRESET_MAP, selectedId = footerPreset) { footerPreset = it }
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
                            imageFormat = imageFormat,
                            imageJpegQuality = imageJpegQuality,
                            imageScale = imageScale,
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

internal val MARGIN_PRESETS: List<MarginPreset> = listOf(
    MarginPreset("standard", "标准", 18, 18, 15, 15),
    MarginPreset("narrow", "窄", 10, 10, 10, 10),
    MarginPreset("wide", "宽", 25, 25, 20, 20),
    MarginPreset("none", "无", 0, 0, 0, 0),
)

internal val MARGIN_PRESETS_MAP: Map<String, String> = mapOf(
    "standard" to "标准", "narrow" to "窄", "wide" to "宽", "none" to "无",
)
internal val IMAGE_FORMATS: Map<String, String> = mapOf("png" to "PNG", "jpeg" to "JPEG")
internal val HEADER_PRESET_MAP: Map<String, String> = mapOf(
    "none" to "无", "title-left" to "标题（左对齐）", "page-center" to "页码（居中）",
    "page-right" to "页码（右对齐）", "page-total-center" to "页码/总页数（居中）",
)

/** 通用「标签 + FilterChip 组」行。 */
@Composable
internal fun ChipRowGroup(label: String, presets: Map<String, String>, selectedId: String, onSelect: (String) -> Unit) {
    Text(label, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(vertical = 2.dp))
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
    ) {
        presets.forEach { (id, display) ->
            FilterChip(
                selected = selectedId == id,
                onClick = { onSelect(id) },
                label = { Text(display, style = MaterialTheme.typography.labelMedium) },
            )
        }
    }
}

@Composable
internal fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
        Switch(checked = checked, onCheckedChange = { onChange(it) })
        Text(label, modifier = Modifier.padding(start = 8.dp))
    }
}
