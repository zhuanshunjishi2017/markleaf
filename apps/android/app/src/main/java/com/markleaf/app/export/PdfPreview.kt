package com.markleaf.app.export

import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.pdf.PdfRenderer
import android.os.ParcelFileDescriptor
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.withContext
import java.io.File

/** Render the actual PDF pages lazily; no screen-media HTML approximation. */
@Composable
internal fun PdfPreview(file: File, modifier: Modifier = Modifier) {
    var error by remember(file) { mutableStateOf<String?>(null) }
    val pageCount by produceState(0, file) {
        try {
            value = withContext(Dispatchers.IO) { withPdf(file) { it.pageCount } }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            error = "无法显示 PDF：${e.message}"
        }
    }
    BoxWithConstraints(modifier.background(MaterialTheme.colorScheme.surfaceVariant)) {
        val width = with(LocalDensity.current) { (maxWidth - 24.dp).roundToPx() }.coerceIn(1, 1600)
        error?.let { Text(it, color = MaterialTheme.colorScheme.error) }
        LazyColumn(
            modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(12.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            items((0 until pageCount).toList(), key = { "${file.name}:$it" }) { index ->
                val bitmap by produceState<Bitmap?>(null, file, index, width) {
                    try {
                        value = withContext(Dispatchers.IO) {
                            withPdf(file) { renderer ->
                                renderer.openPage(index).use { page ->
                                    val height = (width.toLong() * page.height / page.width).toInt().coerceAtLeast(1)
                                    Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888).also { image ->
                                        image.eraseColor(Color.WHITE)
                                        page.render(image, null, null, PdfRenderer.Page.RENDER_MODE_FOR_DISPLAY)
                                    }
                                }
                            }
                        }
                    } catch (e: CancellationException) {
                        throw e
                    } catch (e: Exception) {
                        error = "无法显示 PDF：${e.message}"
                    }
                }
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("第 ${index + 1} / $pageCount 页", style = MaterialTheme.typography.labelSmall)
                    bitmap?.let { image ->
                        Image(image.asImageBitmap(), "PDF 第 ${index + 1} 页",
                            Modifier.fillMaxWidth().aspectRatio(image.width.toFloat() / image.height))
                    }
                }
            }
        }
    }
}

private inline fun <T> withPdf(file: File, action: (PdfRenderer) -> T): T =
    ParcelFileDescriptor.open(file, ParcelFileDescriptor.MODE_READ_ONLY).use { descriptor ->
        PdfRenderer(descriptor).use(action)
    }
