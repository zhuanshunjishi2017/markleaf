package com.markleaf.app.export

import android.content.Context
import android.os.CancellationSignal
import android.os.ParcelFileDescriptor
import android.print.PageRange
import android.print.PrintAttributes
import android.print.PrintDocumentAdapter
import android.print.PrintDocumentInfo
import android.print.PrintManager
import android.webkit.WebView
import java.io.File

/**
 * PDF 导出（spec §5.2）：WebView createPrintDocumentAdapter → PrintManager。
 * 用户在系统打印对话框里选择"另存为 PDF"目标（SAF）。
 *
 * 已知限制（spec 显式降级）：页眉/页脚由系统打印框架决定，桌面端的
 * {page}/{pages} 自定义页眉页脚不在第一版范围。
 */
object PdfExporter {

    /**
     * 发起 PDF 打印。打印框架是异步的：本方法只负责启动，结果由系统 UI 呈现。
     * @param documentName 文档名（自动清洗为合法文件名）
     * @param landscape 横向
     */
    fun printToPdf(context: Context, webView: WebView, documentName: String, landscape: Boolean) {
        val jobName = ExportController.jobName(documentName)
        val attributes = PrintAttributes.Builder()
            .setMediaSize(
                if (landscape) PrintAttributes.MediaSize.ISO_A4.asLandscape()
                else PrintAttributes.MediaSize.ISO_A4.asPortrait(),
            )
            .setColorMode(PrintAttributes.COLOR_MODE_COLOR)
            .setMinMargins(PrintAttributes.Margins(1000, 1000, 1000, 1000))
            .build()
        val adapter = webView.createPrintDocumentAdapter(jobName)
        val printManager = context.getSystemService(Context.PRINT_SERVICE) as PrintManager
        printManager.print(jobName, adapter, attributes)
    }
}
