@file:OptIn(androidx.compose.foundation.ExperimentalFoundationApi::class)

package com.markleaf.app.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.markleaf.app.documents.DocumentEntry

/** 侧栏数据：工作区树（SAF 列表）+ 大纲条目（outlineChanged 消息缓存）。 */
data class OutlineItem(val position: Int, val level: Int, val text: String)

/**
 * 工作区侧栏（spec §6）：树/列表展示 + 文档打开回调。
 * 大纲跳转发 scrollToPosition 命令（对应桌面 outlineTab 行为）。
 */
@Composable
fun WorkspaceSidebar(
    entries: List<DocumentEntry>,
    outline: List<OutlineItem>,
    onOpenDocument: (DocumentEntry) -> Unit,
    onOutlineClick: (OutlineItem) -> Unit,
    onCreateDocument: () -> Unit,
    onRefresh: () -> Unit,
    onEntryLongPress: (DocumentEntry) -> Unit,
    modifier: Modifier = Modifier,
) {
    Column(
        modifier = modifier
            .fillMaxSize()
            .background(MaterialTheme.colorScheme.surfaceContainer)
            .verticalScroll(rememberScrollState())
    ) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 4.dp),
            verticalAlignment = androidx.compose.ui.Alignment.CenterVertically,
        ) {
            Text(
                text = "工作区",
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier.weight(1f),
            )
            Text(
                text = "＋",
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier
                    .clickable(onClick = onCreateDocument)
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
            Text(
                text = "↻",
                style = MaterialTheme.typography.titleMedium,
                modifier = Modifier
                    .clickable(onClick = onRefresh)
                    .padding(horizontal = 8.dp, vertical = 4.dp),
            )
        }
        if (entries.isEmpty()) {
            Text(
                text = "暂未打开文件夹",
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(horizontal = 16.dp),
            )
        }
        for (entry in entries) {
            Row(
                modifier = Modifier
                    .fillMaxWidth()
                    .combinedClickable(
                        onClick = {
                            if (!entry.isFolder) {
                                onOpenDocument(entry)
                            }
                        },
                        onLongClick = { onEntryLongPress(entry) },
                    )
                    .padding(horizontal = 16.dp, vertical = 10.dp),
            ) {
                Text(
                    text = if (entry.isFolder) "📁 ${entry.name}" else "📄 ${entry.name}",
                    style = MaterialTheme.typography.bodyLarge,
                )
            }
        }

        Text(
            text = "大纲",
            style = MaterialTheme.typography.titleMedium,
            fontWeight = FontWeight.Bold,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 10.dp),
        )
        for (item in outline) {
            Text(
                text = "  ".repeat((item.level - 1).coerceAtLeast(0)) + item.text,
                style = if (item.level <= 2) MaterialTheme.typography.bodyLarge else MaterialTheme.typography.bodyMedium,
                modifier = Modifier
                    .fillMaxWidth()
                    .clickable { onOutlineClick(item) }
                    .padding(horizontal = 16.dp, vertical = 6.dp),
            )
        }
    }
}
