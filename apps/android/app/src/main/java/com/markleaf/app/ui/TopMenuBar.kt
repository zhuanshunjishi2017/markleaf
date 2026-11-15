package com.markleaf.app.ui

import com.markleaf.app.L10n

import androidx.compose.animation.AnimatedContent
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInHorizontally
import androidx.compose.animation.slideOutHorizontally
import androidx.compose.animation.core.tween
import androidx.compose.animation.togetherWith
import androidx.compose.foundation.clickable
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Checkbox
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp

/** 顶栏菜单条目：children 非空时渲染为子菜单父项（换页式二级面板）；
 *  checked 非空且为 true 时在统一勾选列显示 ✓。 */
data class TopMenuEntry(
    val title: String,
    val enabled: Boolean = true,
    val separatorBefore: Boolean = false,
    val checked: Boolean? = null,
    val children: List<TopMenuEntry>? = null,
    val action: () -> Unit = {},
)

data class TopMenuSection(val title: String, val entries: List<TopMenuEntry>)

/** 条目文字：可选内嵌 18dp 勾选列 + 标题。✓ 在列内右对齐（贴住文字，
 *  实际间距 ≈2dp），列宽保持 18dp 让有无勾选的条目文字仍对齐。 */
@Composable
private fun MenuEntryText(entry: TopMenuEntry) {
    Row {
        Box(
            modifier = Modifier.width(18.dp),
            contentAlignment = Alignment.CenterEnd,
        ) {
            if (entry.checked == true) {
                Text(
                    "✓",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.primary,
                )
            }
        }
        Text(
            entry.title,
            color = if (entry.enabled) MaterialTheme.colorScheme.onSurface
            else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
        )
    }
}

/** 单个快捷操作：id、展示标签、启用态、动作。标签随状态变化（如源码/可视化）。 */
data class QuickAction(
    val id: String,
    val label: String,
    val enabled: Boolean = true,
    val action: () -> Unit,
)

/** 顶栏 v2：行1 快捷操作（自定义候选池），行2 菜单栏（桌面 NativeMenuBuilder
 *  同构——文件/编辑/插入/格式/视图/更多），条目对齐桌面菜单同名命令。 */
@OptIn(androidx.compose.material3.ExperimentalMaterial3Api::class)
@Composable
fun TopMenuBar(
    active: String,
    dirty: Boolean,
    undoEnabled: Boolean,
    redoEnabled: Boolean,
    sections: List<TopMenuSection>,
    quickActions: List<QuickAction>,
    onOpenDrawer: () -> Unit,
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
                modifier = Modifier.padding(vertical = 12.dp),
                style = MaterialTheme.typography.titleMedium,
                maxLines = 1,
                overflow = androidx.compose.ui.text.style.TextOverflow.Ellipsis,
            )
            // 快捷操作自定义（候选池见 EditorPrefs.QUICK_ACTION_POOL）：
            // 用 SubcomposeLayout 逐项实测按钮宽度后从左截取放得下的最
            // 大前缀并右贴容器边（容器=标题右侧至屏幕边的全部剩余宽度）
            // ——不依赖估算（M3 触达最小尺寸等隐藏开销会让估宽失真），
            // 数量随设备宽度自适应（平板/大屏可容纳全部候选）。下限 2；
            // 超出容量的已选项不渲染（不丢用户选择，如文档标题过长挤占
            // 空间时）。顺序=池顺序、勾选子集按原相对顺序编排（如 撤销/
            // 保存）。局部关闭 M3 最小触达尺寸强制：否则每个按钮布局宽
            // 被顶到 48dp，六项在常规手机屏放不下（关闭后 2 字按钮约
            // 44dp）。
            androidx.compose.runtime.CompositionLocalProvider(
                androidx.compose.material3.LocalMinimumInteractiveComponentEnforcement provides false,
            ) {
                androidx.compose.ui.layout.SubcomposeLayout(
                    modifier = Modifier.weight(1f),
                ) { constraints ->
                    // 不用 TextButton：其内部 defaultMinSize(minWidth=58dp)
                    // 会把两字按钮钳宽，六项放不下。轻量文本按钮实测宽
                    // = 文本 + 8dp 内边距；条目间另加 10dp 间距（触达区域
                    // 分离、视觉更宽松），容量计算把间距一并计入。
                    val itemGap = 10.dp.roundToPx()
                    val content: @Composable androidx.compose.foundation.layout.RowScope.(com.markleaf.app.ui.QuickAction) -> Unit = { qa ->
                        Text(
                            text = qa.label,
                            style = MaterialTheme.typography.labelLarge,
                            color = if (qa.enabled) MaterialTheme.colorScheme.primary
                                    else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                            modifier = Modifier
                                .clip(RoundedCornerShape(20.dp))
                                .clickable(enabled = qa.enabled, onClick = qa.action)
                                .padding(horizontal = 4.dp, vertical = 10.dp),
                        )
                    }
                    val rows = quickActions.map { qa ->
                        subcompose(qa.id) { androidx.compose.foundation.layout.Row { content(qa) } }
                            .map { it.measure(constraints.copy(minWidth = 0, minHeight = 0)) }
                    }
                    var used = 0
                    var count = 0
                    for (row in rows) {
                        val w = row.maxOf { it.width } + if (count > 0) itemGap else 0
                        if (count >= quickActions.size || used + w > constraints.maxWidth) break
                        used += w
                        count++
                    }
                    // 下限 2 = 宽度再窄也至少显示两项（首选项容量估算同理）；
                    // 下限须钳到列表实际大小——勾选被取消到剩 1 项时
                    // coerceIn(2, 1) 是空区间，会抛 IllegalArgumentException
                    // （此前「取消勾选全部」崩溃的根源）。
                    count = count.coerceIn(minOf(2, quickActions.size), quickActions.size)
                    val chosen = rows.take(count)
                    val height = chosen.maxOfOrNull { it.maxOf { p -> p.height } } ?: 0
                    layout(constraints.maxWidth, height) {
                        // 右贴边：从右往左逆序排布，条目间留 itemGap
                        var x = constraints.maxWidth
                        chosen.reversed().forEachIndexed { index, row ->
                            val p = row.first()
                            x -= p.width
                            p.placeRelative(x = x, y = 0)
                            if (index < chosen.size - 1) x -= itemGap
                        }
                    }
                }
            }
        }
        // 菜单行不加 horizontalScroll：DropdownMenu 在可滚动容器内锚点
        // 会错位到行首；六项在常规屏宽内放得下，窄屏由文本自动收缩承载
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 4.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            sections.forEach { section ->
                Box {
                    Text(
                        text = section.title,
                        modifier = Modifier
                            .clip(RoundedCornerShape(10.dp))
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
                        shape = RoundedCornerShape(14.dp),
                    ) {
                        // 换页式二级面板统一动画：进二级从右滑入，返回从左滑入
                        // （此前为无过渡硬切，仅菜单高度骤变产生不一致的观感）
                        AnimatedContent(
                            targetState = submenuTitle,
                            transitionSpec = {
                                if (targetState != null) {
                                    (slideInHorizontally(tween(110)) { it / 3 } + fadeIn(tween(110))) togetherWith
                                        (slideOutHorizontally(tween(110)) { -it / 3 } + fadeOut(tween(110)))
                                } else {
                                    (slideInHorizontally(tween(110)) { -it / 3 } + fadeIn(tween(110))) togetherWith
                                        (slideOutHorizontally(tween(110)) { it / 3 } + fadeOut(tween(110)))
                                }
                            },
                            label = "submenuPanel",
                        ) { panelTitle ->
                            if (panelTitle != null) {
                                Column {
                                    DropdownMenuItem(
                                        text = {
                                            Row(verticalAlignment = Alignment.CenterVertically) {
                                                Text(
                                                    "‹",
                                                    style = MaterialTheme.typography.titleLarge,
                                                    fontWeight = FontWeight.Medium,
                                                )
                                                Text(
                                                    panelTitle,
                                                    modifier = Modifier.padding(start = 6.dp),
                                                    style = MaterialTheme.typography.titleSmall,
                                                    fontWeight = FontWeight.Bold,
                                                )
                                            }
                                        },
                                        onClick = { submenuTitle = null },
                                    )
                                    HorizontalDivider()
                                    submenuEntries.forEach { entry ->
                                        if (entry.separatorBefore) {
                                            HorizontalDivider()
                                        }
                                        // 空标题 + 分隔标记 = 纯分隔行（最近项目
                                        // 组间距），不渲染空菜单行
                                        if (entry.title.isNotEmpty()) {
                                            DropdownMenuItem(
                                                text = { MenuEntryText(entry) },
                                                enabled = entry.enabled,
                                                onClick = {
                                                    expandedSection = null
                                                    submenuTitle = null
                                                    entry.action()
                                                },
                                            )
                                        }
                                    }
                                }
                            } else {
                                Column {
                                    section.entries.forEach { entry ->
                                        if (entry.separatorBefore) {
                                            HorizontalDivider()
                                        }
                                        if (entry.children != null) {
                                            DropdownMenuItem(
                                                text = {
                                                    Row {
                                                        Box(modifier = Modifier.width(18.dp))
                                                        Text(
                                                            entry.title,
                                                            color = if (entry.enabled) MaterialTheme.colorScheme.onSurface
                                                            else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
                                                        )
                                                    }
                                                },
                                                trailingIcon = {
                                                    Text(
                                                        "›",
                                                        style = MaterialTheme.typography.titleLarge,
                                                        fontWeight = FontWeight.Medium,
                                                        modifier = Modifier.padding(end = 4.dp),
                                                    )
                                                },
                                                enabled = entry.enabled,
                                                onClick = {
                                                    submenuTitle = entry.title
                                                    submenuEntries = entry.children
                                                },
                                            )
                                        } else {
                                            DropdownMenuItem(
                                                text = { MenuEntryText(entry) },
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
    }
}
