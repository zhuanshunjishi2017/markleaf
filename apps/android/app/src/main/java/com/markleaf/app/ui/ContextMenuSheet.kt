package com.markleaf.app.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp

/**
 * 编辑器右键/长按菜单（spec §6）：contextMenuRequested → Compose 菜单。
 * 布局与桌面端 0c41999 对齐后的规范一致：按上下文分组（普通/表格/代码块/图片/公式/脚注/只读）。
 * enabled 由 commandStateChanged.actions 驱动。
 */
data class ContextMenuEntry(val title: String, val command: String, val enabled: Boolean = true, val separatorBefore: Boolean = false)

/**
 * 通用右键菜单面板。M3 简化为 DropdownMenu 内容列（完整锚定坐标换算
 * 与 BottomSheet 形态由 MainActivity 场景决定）。
 */
@Composable
fun ContextMenuContent(
    entries: List<ContextMenuEntry>,
    onCommand: (String) -> Unit,
) {
    Column(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp)) {
        for (entry in entries) {
            if (entry.separatorBefore) {
                HorizontalDivider()
            }
            DropdownMenuItem(
                text = {
                    Text(
                        entry.title,
                        color = if (entry.enabled) MaterialTheme.colorScheme.onSurface
                        else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                    )
                },
                onClick = { if (entry.enabled) onCommand(entry.command) },
            )
        }
    }
}

/** 普通上下文（对齐桌面 showEditorContextMenu 普通分支的顶层命令集）。 */
fun ordinaryContextMenu(actions: Map<String, Pair<Boolean, Boolean>>): List<ContextMenuEntry> = listOf(
    ContextMenuEntry("撤销", "undo", actions["undo"]?.first == true),
    ContextMenuEntry("重做", "redo", actions["redo"]?.first == true),
    ContextMenuEntry("剪切", "cut", actions["cut"]?.first == true, separatorBefore = true),
    ContextMenuEntry("拷贝", "copy", actions["copy"]?.first == true),
    ContextMenuEntry("粘贴", "paste", actions["paste"]?.first == true),
    ContextMenuEntry("全选", "selectAll", true, separatorBefore = true),
    ContextMenuEntry("行内代码", "toggleCode", actions["toggleCode"]?.first == true, separatorBefore = true),
    ContextMenuEntry("插入超链接…", "insertLink", actions["insertLink"]?.first == true),
    ContextMenuEntry("格式刷", "formatPainter", actions["formatPainter"]?.first == true, separatorBefore = true),
    ContextMenuEntry("清除格式", "clearFormat", actions["clearFormat"]?.first == true),
)
