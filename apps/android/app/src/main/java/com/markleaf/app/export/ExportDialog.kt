package com.markleaf.app.export

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
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
 * 导出对话框（spec §5.3）：格式切换 + 文件名 + 打印友好开关 + PDF 纸张方向。
 * 保存目标由宿主 CreateDocument launcher 呈现（SAF），本对话框只产出选项。
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

    Column(modifier = Modifier.padding(16.dp)) {
        Text(text = "导出文档", style = androidx.compose.material3.MaterialTheme.typography.titleMedium)

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

        if (format == ExportFormat.PDF) {
            Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
                Switch(checked = landscape, onCheckedChange = { landscape = it })
                Text("横向", modifier = Modifier.padding(start = 8.dp))
            }
        }
        Row(modifier = Modifier.padding(vertical = 4.dp), verticalAlignment = Alignment.CenterVertically) {
            Switch(checked = printFriendly, onCheckedChange = { printFriendly = it })
            Text("打印友好（浅色）", modifier = Modifier.padding(start = 8.dp))
        }

        Row(modifier = Modifier.padding(top = 8.dp)) {
            Button(
                onClick = {
                    onExport(
                        ExportController.sanitizeFileName(name),
                        ExportOptions(format = format, landscape = landscape, printFriendly = printFriendly),
                    )
                },
            ) {
                Text("导出")
            }
            androidx.compose.material3.TextButton(onClick = onDismiss) { Text("取消") }
        }
    }
}
