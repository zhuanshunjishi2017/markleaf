package com.markleaf.app.export

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import android.webkit.WebView

/**
 * 全屏导出页（对齐桌面 ExportWindowController：左侧选项 + 右侧实时预览）。
 * 布局：上方选项表单（可滚动），下方预览区（等比 A4 WebView 渲染，防抖
 * 300ms 生成自包含 HTML——与最终导出走同一 generateExportHtml 管线）。
 * 图片格式无分页概念，预览按长图宽度渲染。
 */
@Composable
fun ExportScreen(
    defaultName: String,
    previewHtmlProvider: suspend (optionsText: String) -> String,
    onDismiss: () -> Unit,
    onExport: (name: String, options: ExportOptions) -> Unit,
) {
    var name by remember { mutableStateOf(ExportController.sanitizeFileName(defaultName)) }
    var format by remember { mutableStateOf(ExportFormat.PDF) }
    var printFriendly by remember { mutableStateOf(false) }
    var landscape by remember { mutableStateOf(false) }
    var marginPresetId by remember { mutableStateOf("standard") }
    // 兼容旧引用：本页直接以 id 驱动（MarginPreset 对象仅用于预设常量）
    var keepTablesTogether by remember { mutableStateOf(true) }
    var keepHeadingsWithNextBlock by remember { mutableStateOf(true) }
    var headerPreset by remember { mutableStateOf("none") }
    var footerPreset by remember { mutableStateOf("none") }
    var imageFormat by remember { mutableStateOf("png") }
    var imageJpegQuality by remember { mutableStateOf(90) }
    var imageScale by remember { mutableStateOf(2) }
    var optionsSeq by remember { mutableStateOf(0) }

    // 选项变化 → 计数器推进（预览 LaunchedEffect 依此重新生成）
    fun bump() { optionsSeq++ }

    Column(modifier = Modifier.fillMaxSize()) {
        // 顶部栏
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(onClick = onDismiss) { Text("‹") }
            Text(
                "导出文档",
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.titleMedium,
            )
            Button(onClick = {
                onExport(ExportController.sanitizeFileName(name), ExportOptions(
                    format = format,
                    landscape = landscape,
                    printFriendly = printFriendly,
                    marginPresetId = marginPresetId,
                    keepTablesTogether = keepTablesTogether,
                    keepHeadingsWithNextBlock = keepHeadingsWithNextBlock,
                    headerPresetId = headerPreset,
                    footerPresetId = footerPreset,
                    imageFormat = imageFormat,
                    imageJpegQuality = imageJpegQuality,
                    imageScale = imageScale,
                ))
            }) { Text("导出") }
        }
        HorizontalDivider()

        Column(
            modifier = Modifier
                .weight(1f)
                .verticalScroll(rememberScrollState())
                .padding(horizontal = 16.dp),
        ) {
            // 格式
            Text("格式", style = MaterialTheme.typography.labelLarge, color = MaterialTheme.colorScheme.onSurfaceVariant,
                modifier = Modifier.padding(top = 10.dp, bottom = 2.dp))
            Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                listOf(ExportFormat.PDF to "PDF", ExportFormat.HTML to "HTML", ExportFormat.IMAGE to "图片").forEach { (f, label) ->
                    FilterChip(
                        selected = format == f,
                        onClick = { format = f; bump() },
                        label = { Text(label) },
                    )
                    androidx.compose.foundation.layout.Spacer(modifier = Modifier.padding(3.dp))
                }
            }

            OutlinedTextField(
                value = name,
                onValueChange = { name = it },
                label = { Text("文件名") },
                modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
            )

            // 页面行为（对齐桌面文案）
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
                    trailingContent = { Switch(checked = keepTablesTogether, onCheckedChange = { keepTablesTogether = it; bump() }) },
                )
                androidx.compose.material3.ListItem(
                    headlineContent = { Text("尽量避免标题孤悬页尾") },
                    trailingContent = { Switch(checked = keepHeadingsWithNextBlock, onCheckedChange = { keepHeadingsWithNextBlock = it; bump() }) },
                )
            }

            // PDF 专属
            if (format == ExportFormat.PDF) {
                Text(
                    "页面设置",
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
                )
                Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Switch(checked = landscape, onCheckedChange = { landscape = it; bump() })
                    Text("横向", modifier = Modifier.padding(start = 8.dp))
                }
                ChipRowGroup("页边距", MARGIN_PRESETS_MAP, marginPresetId) { marginPresetId = it; bump() }
                SwitchRow("打印友好（浅色）", printFriendly) { printFriendly = it; bump() }
            }

            // 图片专属
            if (format == ExportFormat.IMAGE) {
                Text(
                    "图片选项",
                    style = MaterialTheme.typography.labelLarge,
                    fontWeight = androidx.compose.ui.text.font.FontWeight.Medium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
                )
                ChipRowGroup("图片格式", IMAGE_FORMATS, imageFormat) { imageFormat = it; bump() }
                Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("JPEG 质量", modifier = Modifier.weight(1f))
                    Text("$imageJpegQuality", modifier = Modifier.padding(horizontal = 8.dp))
                }
                androidx.compose.material3.Slider(
                    value = imageJpegQuality.toFloat(),
                    onValueChange = { imageJpegQuality = it.toInt(); bump() },
                    valueRange = 50f..100f,
                )
                Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                    Text("缩放倍数", modifier = Modifier.weight(1f))
                    Text("${imageScale}×", modifier = Modifier.padding(horizontal = 8.dp))
                }
                androidx.compose.material3.Slider(
                    value = imageScale.toFloat(),
                    onValueChange = { imageScale = it.toInt(); bump() },
                    valueRange = 1f..4f,
                    steps = 5,
                )
            }

            // HTML 专属：页眉 / 页脚
            if (format == ExportFormat.HTML) {
                ChipRowGroup("页眉", HEADER_PRESET_MAP, headerPreset) { headerPreset = it; bump() }
                ChipRowGroup("页脚", HEADER_PRESET_MAP, footerPreset) { footerPreset = it; bump() }
            }
        }

        HorizontalDivider()

        // ===== 预览区（等比 A4 渲染）=====
        val context = LocalContext.current
        var previewHtml by remember { mutableStateOf<String?>(null) }
        var generating by remember { mutableStateOf(false) }
        LaunchedEffect(optionsSeq, format) {
            generating = true
            val opts = """
                {"format":"${if (format == ExportFormat.HTML) "html" else "pdf"}",
                 "header":"${if (format == ExportFormat.HTML) presetTitle(headerPreset) else ""}",
                 "footer":"${if (format == ExportFormat.HTML) presetTitle(footerPreset) else ""}",
                 "title":"$name",
                 "keepTablesTogether":$keepTablesTogether,
                 "keepHeadingsWithNextBlock":$keepHeadingsWithNextBlock}
            """.trimIndent()
            previewHtml = previewHtmlProvider(opts)
            generating = false
        }
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                if (generating) "正在生成预览…" else "预览",
                style = MaterialTheme.typography.labelMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        AndroidView(
            factory = { ctx ->
                WebView(ctx).apply {
                    settings.javaScriptEnabled = false
                    settings.loadWithOverviewMode = true
                    settings.useWideViewPort = true
                    setBackgroundColor(android.graphics.Color.WHITE)
                }
            },
            update = { web ->
                previewHtml?.let { web.loadDataWithBaseURL(null, it, "text/html", "utf-8", null) }
            },
            modifier = Modifier
                .fillMaxWidth()
                .weight(1f),
        )
    }
}

private fun presetTitle(id: String): String = when (id) {
    "title-left" -> "{title}"
    "page-center" -> "{page}"
    "page-right" -> "{page}"
    "page-total-center" -> "{page}/{pages}"
    else -> ""
}
