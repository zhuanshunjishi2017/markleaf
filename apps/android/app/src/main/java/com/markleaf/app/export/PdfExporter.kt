package com.markleaf.app.export

import android.content.Context
import android.os.Bundle
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.MarkLeafLayoutResultCallback
import android.print.MarkLeafWriteResultCallback
import android.print.PrintManager
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.webkit.WebView
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.ensureActive
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import java.io.File
import kotlin.coroutines.coroutineContext
import kotlin.coroutines.resume
import kotlin.coroutines.resumeWithException
import kotlin.math.roundToInt

/** Real PDF pagination. The returned file is both the preview source and the saved PDF. */
object PdfExporter {
    internal fun attributes(landscape: Boolean, margins: MarginPreset): PrintAttributes {
        fun mils(mm: Int): Int = (mm * 1000.0 / 25.4).roundToInt()
        return PrintAttributes.Builder()
            .setMediaSize(if (landscape) PrintAttributes.MediaSize.ISO_A4.asLandscape()
                else PrintAttributes.MediaSize.ISO_A4.asPortrait())
            // Chromium's Android PDF exporter converts margins via 72-point page units.
            // Use vector-PDF resolution: 300 DPI incorrectly multiplies the physical margins.
            .setResolution(PrintAttributes.Resolution("markleaf", "MarkLeaf", 72, 72))
            .setColorMode(PrintAttributes.COLOR_MODE_COLOR)
            .setMinMargins(PrintAttributes.Margins(
                mils(margins.left), mils(margins.top), mils(margins.right), mils(margins.bottom),
            ))
            .build()
    }

    suspend fun render(
        context: Context,
        html: String,
        options: ExportOptions,
        createWebView: (Context) -> WebView,
    ): File = withContext(Dispatchers.Main.immediate) {
        val file = File.createTempFile("markleaf-preview-", ".pdf", context.cacheDir)
        val web = createWebView(context)
        var completed = false
        try {
            val margins = if (options.marginPresetId == "custom")
                MarginPreset("custom", "custom",
                    options.marginTop.toInt(), options.marginBottom.toInt(),
                    options.marginLeft.toInt(), options.marginRight.toInt())
            else
                MARGIN_PRESETS.firstOrNull { it.id == options.marginPresetId } ?: MARGIN_PRESETS.first()
            val attrs = attributes(options.landscape, margins)
            // Give fonts/formulas the printable width before the print adapter performs pagination.
            val cssWidth = attrs.mediaSize!!.widthMils * 96 / 1000
            val pixelWidth = (cssWidth * context.resources.displayMetrics.density).roundToInt()
            web.measure(
                android.view.View.MeasureSpec.makeMeasureSpec(pixelWidth, android.view.View.MeasureSpec.EXACTLY),
                android.view.View.MeasureSpec.makeMeasureSpec(1, android.view.View.MeasureSpec.EXACTLY),
            )
            web.layout(0, 0, pixelWidth, 1)
            web.loadExportDocument(html)
            val adapter = web.createPrintDocumentAdapter("MarkLeaf")
            // Let an in-flight native write settle before releasing its WebView or descriptor.
            // New option changes cancel the result; they cannot destroy resources under Chromium.
            withContext(NonCancellable) {
                adapter.onStart()
                try {
                    withTimeout(30_000) {
                        layout(adapter, attrs)
                        ParcelFileDescriptor.open(file,
                            ParcelFileDescriptor.MODE_CREATE or ParcelFileDescriptor.MODE_TRUNCATE or
                                ParcelFileDescriptor.MODE_READ_WRITE).use { output ->
                            write(adapter, output)
                        }
                    }
                } finally {
                    adapter.onFinish()
                }
            }
            coroutineContext.ensureActive()
            completed = true
            file
        } catch (e: LinkageError) {
            throw IllegalStateException("此设备需使用系统 PDF 预览", e)
        } finally {
            web.destroy()
            if (!completed) file.delete()
        }
    }

    /** Public-framework fallback: only the system creates its layout/write callbacks. */
    fun openSystemPreview(context: Context, web: WebView, name: String, options: ExportOptions) {
        val adapter = web.createPrintDocumentAdapter(ExportController.jobName(name))
        val delegate = object : PrintDocumentAdapter() {
            override fun onStart() = adapter.onStart()
            override fun onLayout(old: PrintAttributes?, new: PrintAttributes, signal: CancellationSignal,
                callback: LayoutResultCallback, extras: Bundle?) {
                val vectorAttrs = PrintAttributes.Builder()
                    .setMediaSize(requireNotNull(new.mediaSize))
                    .setResolution(PrintAttributes.Resolution("markleaf", "MarkLeaf", 72, 72))
                    .setColorMode(new.colorMode)
                    .setMinMargins(requireNotNull(new.minMargins))
                    .build()
                adapter.onLayout(old, vectorAttrs, signal, callback, extras)
            }
            override fun onWrite(pages: Array<out PageRange>, destination: ParcelFileDescriptor,
                signal: CancellationSignal, callback: WriteResultCallback) = adapter.onWrite(pages, destination, signal, callback)
            override fun onFinish() { adapter.onFinish(); web.destroy() }
        }
        val margins = MARGIN_PRESETS.firstOrNull { it.id == options.marginPresetId } ?: MARGIN_PRESETS.first()
        (context.getSystemService(Context.PRINT_SERVICE) as PrintManager)
            .print(ExportController.jobName(name), delegate, attributes(options.landscape, margins))
    }

    private suspend fun layout(adapter: PrintDocumentAdapter, attrs: PrintAttributes) =
        suspendCancellableCoroutine<Unit> { continuation ->
            val cancellation = CancellationSignal()
            continuation.invokeOnCancellation { cancellation.cancel() }
            adapter.onLayout(null, attrs, cancellation, object : MarkLeafLayoutResultCallback() {
                override fun onLayoutFinished(info: PrintDocumentInfo, changed: Boolean) {
                    if (continuation.isActive) continuation.resume(Unit)
                }
                override fun onLayoutFailed(error: CharSequence?) {
                    if (continuation.isActive) continuation.resumeWithException(IllegalStateException(error?.toString() ?: "PDF 排版失败"))
                }
                override fun onLayoutCancelled() { continuation.cancel() }
            }, Bundle().apply { putBoolean(PrintDocumentAdapter.EXTRA_PRINT_PREVIEW, true) })
        }

    private suspend fun write(adapter: PrintDocumentAdapter, output: ParcelFileDescriptor) =
        suspendCancellableCoroutine<Unit> { continuation ->
            val cancellation = CancellationSignal()
            continuation.invokeOnCancellation { cancellation.cancel() }
            adapter.onWrite(arrayOf(PageRange.ALL_PAGES), output, cancellation,
                object : MarkLeafWriteResultCallback() {
                    override fun onWriteFinished(pages: Array<out PageRange>) {
                        if (continuation.isActive) continuation.resume(Unit)
                    }
                    override fun onWriteFailed(error: CharSequence?) {
                        if (continuation.isActive) continuation.resumeWithException(IllegalStateException(error?.toString() ?: "PDF 生成失败"))
                    }
                    override fun onWriteCancelled() { continuation.cancel() }
                })
        }
}
