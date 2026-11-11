package com.markleaf.app.export

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import android.content.Context
import android.webkit.WebView
import androidx.compose.ui.platform.LocalContext
import java.io.File
import kotlinx.coroutines.TimeoutCancellationException
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.delay
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeout
import com.markleaf.app.L10n

/** Export options above a live preview. PDF preview renders the same paginated file that is saved. */
@Composable
fun ExportScreen(
    defaultName: String,
    initialStyleId: String,
    initialColorThemeId: String,
    styleIds: List<Pair<String, String>>,
    colorThemes: List<com.markleaf.app.editor.StyleAssets.ColorTheme>,
    previewHtmlProvider: suspend (name: String, options: ExportOptions) -> String,
    createExportWebView: (Context) -> WebView,
    onSystemPdfPreview: (name: String, options: ExportOptions) -> Unit,
    onDismiss: () -> Unit,
    onExport: (name: String, options: ExportOptions, html: String, pdf: File?) -> Unit,
) {
    androidx.activity.compose.BackHandler(onBack = onDismiss)
    var name by remember { mutableStateOf(ExportController.sanitizeFileName(defaultName)) }
    var format by remember { mutableStateOf(ExportFormat.PDF) }
    var printFriendly by remember { mutableStateOf(false) }
    var landscape by remember { mutableStateOf(false) }
    var marginPresetId by remember { mutableStateOf("standard") }
    var customMargins by remember { mutableStateOf(listOf(18.0, 18.0, 15.0, 15.0)) } // top,bottom,left,right
    var showCustomMarginDialog by remember { mutableStateOf(false) }
    // 兼容旧引用：本页直接以 id 驱动（MarginPreset 对象仅用于预设常量）
    var keepTablesTogether by remember { mutableStateOf(true) }
    var keepHeadingsWithNextBlock by remember { mutableStateOf(true) }
    var headerPreset by remember { mutableStateOf("none") }
    var footerPreset by remember { mutableStateOf("none") }
    var styleId by remember { mutableStateOf(initialStyleId) }
    var colorThemeId by remember { mutableStateOf(initialColorThemeId) }
    var imageFormat by remember { mutableStateOf("png") }
    var imageJpegQuality by remember { mutableStateOf(90) }
    var imageScale by remember { mutableStateOf(2) }
    val options = ExportOptions(
        format = format, landscape = landscape, printFriendly = printFriendly,
        marginPresetId = marginPresetId,
        marginTop = customMargins[0], marginBottom = customMargins[1],
        marginLeft = customMargins[2], marginRight = customMargins[3],
        keepTablesTogether = keepTablesTogether,
        keepHeadingsWithNextBlock = keepHeadingsWithNextBlock,
        headerPresetId = headerPreset, footerPresetId = footerPreset,
        imageFormat = imageFormat, imageJpegQuality = imageJpegQuality, imageScale = imageScale,
        styleId = styleId, colorThemeId = colorThemeId,
    )
    val context = LocalContext.current
    val pdfMutex = remember { Mutex() }
    val previewGeneration = remember { java.util.concurrent.atomic.AtomicLong() }
    val ownedFiles = remember { mutableSetOf<File>() }
    var transferredFile by remember { mutableStateOf<File?>(null) }
    var previewHtml by remember { mutableStateOf<String?>(null) }
    var previewPdf by remember { mutableStateOf<File?>(null) }
    var preparedKey by remember { mutableStateOf<Pair<String, ExportOptions>?>(null) }
    var generating by remember { mutableStateOf(true) }
    var previewError by remember { mutableStateOf<String?>(null) }
    var retryKey by remember { mutableStateOf(0) }
    DisposableEffect(Unit) {
        onDispose { ownedFiles.filterNot { it == transferredFile }.forEach { it.delete() } }
    }
    LaunchedEffect(name, options, retryKey) {
        val generation = previewGeneration.incrementAndGet()
        generating = true
        previewError = null
        try {
            delay(300)
            val html = withTimeout(30_000) { previewHtmlProvider(name, options) }
            val pdf = if (format == ExportFormat.PDF) pdfMutex.withLock {
                PdfExporter.render(context, html, options, createExportWebView)
            } else null
            pdf?.let { ownedFiles.add(it) }
            val old = previewPdf
            previewHtml = html
            previewPdf = pdf
            preparedKey = name to options
            if (old != null && old != pdf) { old.delete(); ownedFiles.remove(old) }
        } catch (e: TimeoutCancellationException) {
            previewError = "预览生成超时，请重试或使用系统 PDF 预览"
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            previewError = "预览生成失败：${e.message ?: "未知错误"}"
        } finally {
            if (previewGeneration.get() == generation) generating = false
        }
    }

    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
        Column(modifier = Modifier.fillMaxSize().safeDrawingPadding()) {
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
                Button(
                    enabled = preparedKey == (name to options) && previewError == null && !generating,
                    onClick = {
                        transferredFile = previewPdf
                        onExport(ExportController.sanitizeFileName(name), options, previewHtml!!, previewPdf)
                    },
                ) { Text("导出") }
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
                            onClick = { format = f },
                            label = { Text(label) },
                        )
                        androidx.compose.foundation.layout.Spacer(modifier = Modifier.padding(3.dp))
                    }
                }

                OutlinedTextField(
                    value = name,
                    singleLine = true,
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
                        trailingContent = { Switch(checked = keepTablesTogether, onCheckedChange = { keepTablesTogether = it }) },
                    )
                    androidx.compose.material3.ListItem(
                        headlineContent = { Text("尽量避免标题孤悬页尾") },
                        trailingContent = { Switch(checked = keepHeadingsWithNextBlock, onCheckedChange = { keepHeadingsWithNextBlock = it }) },
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
                        Switch(checked = landscape, onCheckedChange = { landscape = it })
                        Text("横向", modifier = Modifier.padding(start = 8.dp))
                    }
                    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp), verticalAlignment = Alignment.CenterVertically) {
                        ChipRowGroup("", MARGIN_PRESETS_MAP, marginPresetId) { marginPresetId = it }
                        TextButton(onClick = { showCustomMarginDialog = true }) {
                            Text(L10n.t("自定义边距…"))
                        }
                    }
                    // 当前边距摘要（对齐桌面小字：上/下/左/右 mm）
                    val summary = if (marginPresetId == "custom")
                        "上: ${customMargins[0]}mm, 下: ${customMargins[1]}mm, 左: ${customMargins[2]}mm, 右: ${customMargins[3]}mm"
                    else
                        MARGIN_PRESETS.firstOrNull { it.id == marginPresetId }?.let {
                            "上: ${it.top}mm, 下: ${it.bottom}mm, 左: ${it.left}mm, 右: ${it.right}mm"
                        } ?: ""
                    Text(
                        summary,
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                        modifier = Modifier.padding(start = 4.dp),
                    )
                    SwitchRow("打印友好（浅色）", printFriendly) { printFriendly = it }
                }

                // 排版样式 + 配色方案（对齐桌面导出窗口两行；两种格式均提供，
                // 图片格式无分页概念但样式同样影响渲染）
                ChipRowGroup(
                    "排版样式",
                    styleIds.associate { it.first to L10n.t(it.second) },
                    styleId,
                ) { styleId = it }
                val themeNames = colorThemes.associate { it.id to L10n.t(it.name) }
                ChipRowGroup(
                    "配色方案",
                    themeNames,
                    colorThemeId,
                ) { picked ->
                    colorThemeId = picked
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
                    ChipRowGroup("图片格式", IMAGE_FORMATS, imageFormat) { imageFormat = it }
                    Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                        Text("JPEG 质量", modifier = Modifier.weight(1f))
                        Text("$imageJpegQuality", modifier = Modifier.padding(horizontal = 8.dp))
                    }
                    androidx.compose.material3.Slider(
                        value = imageJpegQuality.toFloat(),
                        onValueChange = { imageJpegQuality = it.toInt() },
                        valueRange = 50f..100f,
                    )
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

                // HTML 专属：页眉 / 页脚
                if (format == ExportFormat.HTML) {
                    ChipRowGroup("页眉", HEADER_PRESET_MAP, headerPreset) { headerPreset = it }
                    ChipRowGroup("页脚", HEADER_PRESET_MAP, footerPreset) { footerPreset = it }
                }
            }

            HorizontalDivider()

            Row(
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    if (generating) "正在生成预览…" else if (format == ExportFormat.PDF) "PDF 预览" else "预览",
                    style = MaterialTheme.typography.labelMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            val previewModifier = Modifier.fillMaxWidth().weight(1f)
            if (previewError != null) {
                Column(previewModifier.padding(16.dp)) {
                    Text(previewError!!, color = MaterialTheme.colorScheme.error)
                    TextButton(onClick = { retryKey++ }) { Text("重试") }
                    if (format == ExportFormat.PDF) {
                        TextButton(onClick = { onSystemPdfPreview(name, options) }) { Text("使用系统 PDF 预览并导出") }
                    }
                }
            } else if (format == ExportFormat.PDF) {
                previewPdf?.let { PdfPreview(it, previewModifier) }
                    ?: androidx.compose.foundation.layout.Box(previewModifier)
            } else {
                AndroidView(
                    factory = { ctx -> createExportWebView(ctx) },
                    onRelease = { web -> web.destroy() },
                    update = { web ->
                        previewHtml?.let { html ->
                            if (web.tag != html) {
                                web.tag = html
                                web.loadDataWithBaseURL(com.markleaf.app.editor.EditorWebViewFactory.EDITOR_URL,
                                    html, "text/html", "utf-8", null)
                            }
                        }
                    },
                    modifier = previewModifier,
                )
            }
        }
    }
}


@Composable
private fun CustomMarginsDialog(
    initial: List<Double>,
    onConfirm: (List<Double>) -> Unit,
    onDismiss: () -> Unit,
) {
    val labels = listOf("上边距", "下边距", "左边距", "右边距")
    val texts = remember {
        androidx.compose.runtime.mutableStateListOf(*initial.map { compactMargin(it) }.toTypedArray())
    }
    val valid = texts.all { it.toDoubleOrNull()?.let { v -> v in 0.0..100.0 } != null }
    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(L10n.t("自定义边距")) },
        text = {
            Column {
                labels.forEachIndexed { index, label ->
                    OutlinedTextField(
                        value = texts[index],
                        onValueChange = { input ->
                            // 对齐桌面：过滤非法字符、允许清空
                            texts[index] = input.filter { it.isDigit() || it == '.' }.take(6)
                        },
                        label = { Text(L10n.t(label)) },
                        singleLine = true,
                        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    )
                }
            }
        },
        confirmButton = {
            androidx.compose.material3.TextButton(
                enabled = valid,
                onClick = {
                    onConfirm(texts.map { it.toDoubleOrNull() ?: 0.0 })
                    onDismiss()
                },
            ) { Text(L10n.t("确定")) }
        },
        dismissButton = {
            androidx.compose.material3.TextButton(onClick = onDismiss) { Text(L10n.t("取消")) }
        },
    )
}

private fun compactMargin(value: Double): String =
    if (value == value.toLong().toDouble()) value.toLong().toString() else value.toString()
