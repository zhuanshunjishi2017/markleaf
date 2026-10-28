package com.markleaf.app.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** 顶栏菜单条目：children 非空时渲染为子菜单父项（二级嵌套，对齐桌面
 *  段落样式/列表/提示框/表格/图片等子菜单结构）；action 为空且无 children
 *  仅作展示分隔用。checked 非空时渲染勾选框。 */
data class TopMenuEntry(
    val title: String,
    val enabled: Boolean = true,
    val separatorBefore: Boolean = false,
    val checked: Boolean? = null,
    val children: List<TopMenuEntry>? = null,
    val action: () -> Unit = {},
)

data class TopMenuSection(val title: String, val entries: List<TopMenuEntry>)

/** 顶栏 v2：行1 操作行（撤销/重做/保存），行2 菜单栏（桌面 NativeMenuBuilder
 *  同构——文件/编辑/插入/格式/视图/更多），条目对齐桌面菜单同名命令。 */
@Composable
fun TopMenuBar(
    active: String,
    dirty: Boolean,
    undoEnabled: Boolean,
    redoEnabled: Boolean,
    sections: List<TopMenuSection>,
    onOpenDrawer: () -> Unit,
    onUndo: () -> Unit,
    onRedo: () -> Unit,
    onSave: () -> Unit,
) {
    // 菜单单开互斥：同一时刻至多一个下拉展开
    var expandedSection by remember { mutableStateOf<String?>(null) }
    // 二级面板（换页式）：嵌套 Popup 在手机上锚点会错位，改为同一面板
    // 内切换「‹ 返回 + 子条目列表」
    var submenuTitle by remember { mutableStateOf<String?>(null) }
    var submenuEntries by remember { mutableStateOf<List<TopMenuEntry>>(emptyList()) }
    Column(modifier = Modifier.fillMaxWidth().statusBarsPadding()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = 8.dp, vertical = 2.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            TextButton(onClick = onOpenDrawer) { Text("☰") }
            Text(
                text = (if (dirty) "● " else "") + active,
                modifier = Modifier.weight(1f).padding(vertical = 12.dp),
                style = MaterialTheme.typography.titleMedium,
                maxLines = 1,
            )
            TextButton(onClick = onUndo, enabled = undoEnabled) { Text("撤销") }
            TextButton(onClick = onRedo, enabled = redoEnabled) { Text("重做") }
            TextButton(onClick = onSave) { Text("保存") }
        }
        // 菜单行不加 horizontalScroll：DropdownMenu 在可滚动容器内锚点
        // 会错位到行首；六项在常规屏宽内放得下，窄屏由文本自动收缩承载
        // DropdownMenu 锚定其父布局节点：每组用 Box 包裹，菜单才能锚在
        // 对应菜单名下方（直接放 Row 里会锚到整行左上角）
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            sections.forEach { section ->
                androidx.compose.foundation.layout.Box {
                    Text(
                        text = section.title,
                        modifier = Modifier
                            .clickable { expandedSection = section.title }
                            .padding(horizontal = 12.dp, vertical = 6.dp),
                        style = MaterialTheme.typography.bodyMedium,
                        color = if (expandedSection == section.title) MaterialTheme.colorScheme.primary
                        else MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    DropdownMenu(
                        expanded = expandedSection == section.title,
                        onDismissRequest = {
                            expandedSection = null
                            submenuTitle = null
                        },
                    ) {
                        if (submenuTitle != null) {
                            // 二级面板：返回头 + 子条目（同窗口内换页，无锚点问题）
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        "‹ $submenuTitle",
                                        style = MaterialTheme.typography.titleSmall,
                                        fontWeight = FontWeight.Bold,
                                    )
                                },
                                onClick = { submenuTitle = null },
                            )
                            HorizontalDivider()
                            submenuEntries.forEach { entry ->
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
                                    leadingIcon = {
                                        androidx.compose.foundation.layout.Box(
                                            modifier = Modifier.width(4.dp),
                                            contentAlignment = androidx.compose.ui.Alignment.CenterStart,
                                        ) {
                                            if (entry.checked == true) {
                                                Text(
                                                    "✓",
                                                    style = MaterialTheme.typography.labelSmall,
                                                    color = MaterialTheme.colorScheme.primary,
                                                )
                                            }
                                        }
                                    },
                                    enabled = entry.enabled,
                                    onClick = {
                                        expandedSection = null
                                        submenuTitle = null
                                        entry.action()
                                    },
                                )
                            }
                        } else {
                            section.entries.forEach { entry ->
                                if (entry.separatorBefore) {
                                    HorizontalDivider()
                                }
                                if (entry.children != null) {
                                    DropdownMenuItem(
                                        text = {
                                            Text(
                                                entry.title,
                                                color = if (entry.enabled) MaterialTheme.colorScheme.onSurface
                                                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                                            )
                                        },
                                        leadingIcon = {
                                            androidx.compose.foundation.layout.Box(
                                                modifier = Modifier.width(4.dp),
                                                contentAlignment = Alignment.CenterStart,
                                            ) {}
                                        },
                                        trailingIcon = {
                                            Text("▸", style = MaterialTheme.typography.labelSmall)
                                        },
                                        enabled = entry.enabled,
                                        onClick = {
                                            submenuTitle = entry.title
                                            submenuEntries = entry.children
                                        },
                                    )
                                } else {
                                    DropdownMenuItem(
                                        text = {
                                            Text(
                                                entry.title,
                                                color = if (entry.enabled) MaterialTheme.colorScheme.onSurface
                                                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                                            )
                                        },
                                        leadingIcon = {
                                            Box(
                                                modifier = Modifier.width(4.dp),
                                                contentAlignment = Alignment.CenterStart,
                                            ) {
                                                if (entry.checked == true) {
                                                    Text(
                                                        "✓",
                                                        style = MaterialTheme.typography.labelSmall,
                                                        color = MaterialTheme.colorScheme.primary,
                                                    )
                                                }
                                            }
                                        },
                                        enabled = entry.enabled,
                                        onClick = {
                                            expandedSection = null
                                            entry.action()
                                        },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
