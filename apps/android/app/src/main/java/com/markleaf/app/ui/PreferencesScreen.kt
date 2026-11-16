@file:OptIn(androidx.compose.foundation.layout.ExperimentalLayoutApi::class)

package com.markleaf.app.ui

import com.markleaf.app.L10n
import com.markleaf.app.editor.StyleAssets

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.ui.window.DialogProperties
import androidx.compose.material3.FilterChip
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.rememberTextMeasurer
import androidx.compose.ui.unit.dp

/** 偏好设置层（单页分组，字段对齐桌面 AppSettings 默认值）。分组与桌面偏好设置
 *  页签同构：文件 / 编辑器 / 外观 / 通用 / 图片，组内小节标题也沿用桌面页内
 *  header（保存选项、文本格式、Markdown 行为…）。改值即回调 onChange（宿主保存并应用）。 */
@Composable
fun PreferencesScreen(
    prefs: EditorPrefs,
    styleIds: List<Pair<String, String>>,
    colorThemes: List<StyleAssets.ColorTheme>,
    activeColorThemeId: String,
    onChange: (EditorPrefs) -> Unit,
    onClearHistory: () -> Unit,
    onOpenRecoveryList: () -> Unit,
    onDismiss: () -> Unit,
) {
    var showVisualSettings by remember { mutableStateOf(false) }
    var showSourceSettings by remember { mutableStateOf(false) }
    var showMarkdownSettings by remember { mutableStateOf(false) }
    var showStatusBarSettings by remember { mutableStateOf(false) }
    var showClearHistoryConfirm by remember { mutableStateOf(false) }
    var showResetConfirm by remember { mutableStateOf(false) }
    val lightThemes = colorThemes.filter { !it.isDark }
    val darkThemes = colorThemes.filter { it.isDark }
    Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
        Column(modifier = Modifier.fillMaxSize()) {
            // 退出走系统返回手势（BackHandler 已接管），不再放页内返回按钮
            Text(
                L10n.t("首选项"),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.Bold,
                modifier = Modifier
                    .statusBarsPadding()
                    .padding(horizontal = 16.dp, vertical = 12.dp),
            )
            Column(modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp)) {
                // ===== 文件（对齐桌面「文件」页签）=====
                SectionTitle(L10n.t("文件"))
                GroupHeader(L10n.t("启动"))
                ChipRow(
                    label = L10n.t("启动操作"),
                    options = EditorPrefs.STARTUP_ACTIONS.map { L10n.t(it.second) },
                    selected = L10n.t(EditorPrefs.STARTUP_ACTIONS.firstOrNull { it.first == prefs.startupAction }?.second ?: ""),
                    onSelect = { display ->
                        EditorPrefs.STARTUP_ACTIONS
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(startupAction = it.first)) }
                    },
                )
                GroupHeader(L10n.t("保存选项"))
                SwitchRow(
                    label = L10n.t("自动保存"),
                    checked = prefs.autoSave,
                    onChange = { onChange(prefs.copy(autoSave = it)) },
                )
                SwitchRow(
                    label = L10n.t("切换文档时自动保存"),
                    checked = prefs.saveOnSwitch,
                    onChange = { onChange(prefs.copy(saveOnSwitch = it)) },
                )
                StepperRow(
                    label = L10n.t("快照保存间隔"),
                    value = "${prefs.snapshotInterval} ${L10n.t("秒")}",
                    step = 10,
                    onMinus = { onChange(prefs.copy(snapshotInterval = (prefs.snapshotInterval - 10).coerceAtLeast(10))) },
                    onPlus = { onChange(prefs.copy(snapshotInterval = (prefs.snapshotInterval + 10).coerceAtMost(300))) },
                )
                GroupHeader(L10n.t("文本格式"))
                ChipRow(
                    label = L10n.t("默认编码"),
                    options = EditorPrefs.ENCODING_CHOICES,
                    selected = prefs.defaultEncoding,
                    onSelect = { onChange(prefs.copy(defaultEncoding = it)) },
                )
                ChipRow(
                    label = L10n.t("默认换行符"),
                    options = EditorPrefs.NEWLINE_CHOICES,
                    selected = prefs.defaultNewline,
                    onSelect = { onChange(prefs.copy(defaultNewline = it)) },
                )
                GroupHeader(L10n.t("历史记录"))
                SwitchRow(
                    label = L10n.t("记录最近文件"),
                    checked = prefs.recordRecent,
                    onChange = { onChange(prefs.copy(recordRecent = it)) },
                )
                SwitchRow(
                    label = L10n.t("记录最近文件夹"),
                    checked = prefs.recordRecentFolders,
                    onChange = { onChange(prefs.copy(recordRecentFolders = it)) },
                )
                Row(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    // 对齐桌面「文件」页签「历史记录」组：恢复入口 + 清除入口并列
                    TextButton(onClick = onOpenRecoveryList) { Text(L10n.t("恢复未保存的文档…")) }
                    TextButton(onClick = { showClearHistoryConfirm = true }) { Text(L10n.t("清除历史记录…")) }
                }

                // ===== 编辑器（对齐桌面「编辑器」页签：可视化 / 源码模式 / Markdown 行为 / 缩放视图）=====
                SectionTitle(L10n.t("编辑器"))
                GroupHeader(L10n.t("可视化"))
                LinkRow(L10n.t("可视化设置…")) { showVisualSettings = true }
                GroupHeader(L10n.t("源码模式"))
                LinkRow(L10n.t("字体设置…")) { showSourceSettings = true }
                GroupHeader(L10n.t("Markdown 行为"))
                LinkRow(L10n.t("Markdown 行为…")) { showMarkdownSettings = true }
                GroupHeader(L10n.t("缩放视图"))
                SwitchRow(
                    label = L10n.t("打开时还原上次的缩放比例"),
                    checked = prefs.restoreZoomOnOpen,
                    onChange = { onChange(prefs.copy(restoreZoomOnOpen = it)) },
                )
                SwitchRow(
                    label = L10n.t("使用双指捏合进行缩放"),
                    checked = prefs.pinchZoom,
                    onChange = { onChange(prefs.copy(pinchZoom = it)) },
                )
                // 会话内缩放（「缩放」±）不进首选项：它是当前文档的即时视图
                // 状态，属于视图菜单/手势（桌面同款约定：首选项只有「打开时
                // 还原」与缩放方式开关）。

                // ===== 外观（对齐桌面「外观」页签「文档外观」组）=====
                SectionTitle(L10n.t("外观"))
                GroupHeader(L10n.t("文档外观"))
                ChipRow(
                    label = L10n.t("排版样式"),
                    options = styleIds.map { L10n.t(it.second) },
                    selected = L10n.t(styleIds.firstOrNull { it.first == prefs.styleId }?.second ?: ""),
                    onSelect = { display ->
                        styleIds.firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(styleId = it.first)) }
                    },
                )
                // 与桌面一致：这里显示当前实际渲染的主题；开启系统同步时
                // 该项只读，默认浅/深色主题分别由下面两行控制。
                ThemeChipRow(
                    label = L10n.t("颜色主题"),
                    options = colorThemes,
                    selectedId = activeColorThemeId,
                    enabled = !prefs.followSystemTheme,
                    onSelect = { id -> onChange(prefs.copy(colorTheme = id)) },
                )
                SwitchRow(
                    label = L10n.t("与操作系统同步"),
                    checked = prefs.followSystemTheme,
                    onChange = { onChange(prefs.copy(followSystemTheme = it)) },
                )
                ThemeChipRow(
                    label = L10n.t("默认浅色主题"),
                    options = lightThemes,
                    selectedId = prefs.defaultLightTheme,
                    enabled = prefs.followSystemTheme,
                    onSelect = { id -> onChange(prefs.copy(defaultLightTheme = id)) },
                )
                ThemeChipRow(
                    label = L10n.t("默认深色主题"),
                    options = darkThemes,
                    selectedId = prefs.defaultDarkTheme,
                    enabled = prefs.followSystemTheme,
                    onSelect = { id -> onChange(prefs.copy(defaultDarkTheme = id)) },
                )
                SwitchRow(
                    label = L10n.t("显示代码高亮"),
                    checked = prefs.codeHighlight,
                    onChange = { onChange(prefs.copy(codeHighlight = it)) },
                )
                // 桌面外观页「状态栏」：自定义各字段可见性（侧栏切换/命令状态
                // 为桌面专属）。字段较多，收进独立弹窗（同可视化设置形态）。
                GroupHeader(L10n.t("状态栏"))
                LinkRow(L10n.t("状态栏设置…")) { showStatusBarSettings = true }

                // ===== 通用（对齐桌面「通用」页签「显示」组）=====
                SectionTitle(L10n.t("通用"))
                GroupHeader(L10n.t("显示"))
                ChipRow(
                    label = L10n.t("显示语言"),
                    options = EditorPrefs.DISPLAY_LANGUAGES.map { it.second },
                    selected = EditorPrefs.DISPLAY_LANGUAGES
                        .firstOrNull { it.first == prefs.displayLanguage }?.second ?: "",
                    onSelect = { display ->
                        EditorPrefs.DISPLAY_LANGUAGES
                            .firstOrNull { it.second == display }
                            ?.let { onChange(prefs.copy(displayLanguage = it.first)) }
                    },
                )

                // ===== 通用（顶栏快捷操作自定义）=====
                GroupHeader(L10n.t("顶栏快捷操作"))
                val selectedSet = EditorPrefs.parseQuickActions(prefs.quickActions).toSet()
                // 勾选上限自适应设备宽度：按顶栏可用宽度（屏宽扣除汉堡按
                // 钮、标题、行边距等固定开销）与池内各标签实测文本宽贪心
                // 累加估算，与顶栏 SubcomposeLayout 实测结果一致（手机约
                // 6 项，平板/大屏可容纳全部候选）。
                val quickActionCapacity = rememberQuickActionCapacity()
                // 展示顺序=池顺序；选中集合驱动的显示顺序与顶栏一致
                val ordered = EditorPrefs.QUICK_ACTION_POOL.filter { it.first in selectedSet }
                    .map { it.first } + EditorPrefs.parseQuickActions(prefs.quickActions)
                    .filter { id -> EditorPrefs.QUICK_ACTION_POOL.none { it.first == id } }
                androidx.compose.foundation.layout.FlowRow(
                    modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
                    horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
                    verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
                ) {
                    EditorPrefs.QUICK_ACTION_POOL.forEach { (id, display) ->
                        androidx.compose.material3.FilterChip(
                            selected = id in selectedSet,
                            // 选满容量后，未选中项自动置灰（M3 禁用态），表意
                            // 「不可用」而非点了没反应；取消一项即恢复可选。
                            enabled = id in selectedSet || selectedSet.size < quickActionCapacity,
                            onClick = {
                                val current = EditorPrefs.parseQuickActions(prefs.quickActions).toMutableList()
                                if (id in current) {
                                    current.remove(id)
                                } else if (current.size < quickActionCapacity) {
                                    current.add(id)
                                }
                                onChange(prefs.copy(quickActions = current.joinToString(",")))
                            },
                            label = { Text(L10n.t(display), style = MaterialTheme.typography.labelMedium) },
                        )
                    }
                }

                // ===== 图片（对齐桌面「图片」页签；Android 通过 SAF 插入，
                //      clipboardImageHandling / imageDefaultDirectory 依赖任意
                //      目录路径，scoped storage 下无可靠映射，不提供）=====
                SectionTitle(L10n.t("图片"))
                ChipRow(
                    label = L10n.t("插入图片方式"),
                    options = EditorPrefs.FILE_IMAGE_HANDLINGS.map { L10n.t(it.second) },
                    selected = L10n.t(
                        EditorPrefs.FILE_IMAGE_HANDLINGS
                            .firstOrNull { it.first == prefs.fileImageHandling }?.second ?: "",
                    ),
                    onSelect = { display ->
                        EditorPrefs.FILE_IMAGE_HANDLINGS
                            .firstOrNull { L10n.t(it.second) == display }
                            ?.let { onChange(prefs.copy(fileImageHandling = it.first)) }
                    },
                )
                SwitchRow(
                    label = L10n.t("相对路径前加 \"./\""),
                    checked = prefs.prefixRelativeWithDotSlash,
                    onChange = { onChange(prefs.copy(prefixRelativeWithDotSlash = it)) },
                )

                // 底部操作（对齐桌面 PreferencesWindowController 底部「恢复
                // 默认设置」）：确认后整体重置为默认值。
                HorizontalDivider(modifier = Modifier.padding(vertical = 8.dp))
                TextButton(onClick = { showResetConfirm = true }) {
                    Text(L10n.t("恢复默认设置…"), color = MaterialTheme.colorScheme.error)
                }

            }
            if (showVisualSettings) {
                AlertDialog(
                    onDismissRequest = { showVisualSettings = false },
                    properties = DialogProperties(usePlatformDefaultWidth = false),
                    title = { Text(L10n.t("可视化设置")) },
                    text = {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth(0.92f)
                                .heightIn(max = 520.dp)
                                .verticalScroll(rememberScrollState()),
                        ) {
                            StepperRow(
                                label = L10n.t("字号"),
                                value = "${prefs.fontSize}",
                                onMinus = { onChange(prefs.copy(fontSize = (prefs.fontSize - 1).coerceAtLeast(12))) },
                                onPlus = { onChange(prefs.copy(fontSize = (prefs.fontSize + 1).coerceAtMost(24))) },
                            )
                            ChipRow(L10n.t("行高"), listOf("1.5", "1.75", "2.0"), prefs.lineHeight.toString()) {
                                onChange(prefs.copy(lineHeight = it.toDouble()))
                            }
                            ChipRow(
                                L10n.t("内容宽度"),
                                listOf("740", "820", "980", L10n.t("不限")),
                                if (prefs.ignoreMaxWidth) L10n.t("不限") else prefs.maxWidth.toString(),
                            ) {
                                if (it == L10n.t("不限")) onChange(prefs.copy(ignoreMaxWidth = true))
                                else onChange(prefs.copy(ignoreMaxWidth = false, maxWidth = it.toInt()))
                            }
                            ChipRow(
                                L10n.t("中文排版语言"),
                                EditorPrefs.CJK_LANGUAGES.map { it.second },
                                EditorPrefs.CJK_LANGUAGES.firstOrNull { it.first == prefs.cjkLanguage }?.second ?: "",
                            ) { display ->
                                EditorPrefs.CJK_LANGUAGES.firstOrNull { it.second == display }
                                    ?.let { onChange(prefs.copy(cjkLanguage = it.first)) }
                            }
                            SwitchRow(L10n.t("中西文之间自动加空格"), prefs.cjkSpacing) { onChange(prefs.copy(cjkSpacing = it)) }
                            SwitchRow(L10n.t("西文两端对齐"), prefs.adaptiveJustification) { onChange(prefs.copy(adaptiveJustification = it)) }
                            SwitchRow(L10n.t("允许调整表格列宽"), prefs.tableColumnResize) { onChange(prefs.copy(tableColumnResize = it)) }
                            SwitchRow(L10n.t("显示段落块手柄"), prefs.blockHandle) { onChange(prefs.copy(blockHandle = it)) }
                        }
                    },
                    confirmButton = { TextButton(onClick = { showVisualSettings = false }) { Text(L10n.t("关闭")) } },
                )
            }
            if (showSourceSettings) {
                AlertDialog(
                    onDismissRequest = { showSourceSettings = false },
                    properties = DialogProperties(usePlatformDefaultWidth = false),
                    title = { Text(L10n.t("字体设置")) },
                    text = {
                        Column(
                            modifier = Modifier.fillMaxWidth(0.92f).heightIn(max = 520.dp).verticalScroll(rememberScrollState()),
                        ) {
                            StepperRow(L10n.t("源码字号"), "${prefs.sourceFontSize}", onMinus = {
                                onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize - 1).coerceAtLeast(12)))
                            }, onPlus = {
                                onChange(prefs.copy(sourceFontSize = (prefs.sourceFontSize + 1).coerceAtMost(20)))
                            })
                            ChipRow(
                                L10n.t("源码字体"),
                                EditorPrefs.SOURCE_FONT_FAMILIES.map { L10n.t(it.second) },
                                L10n.t(EditorPrefs.SOURCE_FONT_FAMILIES.firstOrNull { it.first == prefs.sourceFontFamily }?.second ?: ""),
                            ) { display ->
                                EditorPrefs.SOURCE_FONT_FAMILIES.firstOrNull { L10n.t(it.second) == display }
                                    ?.let { onChange(prefs.copy(sourceFontFamily = it.first)) }
                            }
                            ChipRow(
                                L10n.t("源码中文字体"),
                                EditorPrefs.SOURCE_CJK_FONT_FAMILIES.map { L10n.t(it.second) },
                                L10n.t(EditorPrefs.SOURCE_CJK_FONT_FAMILIES.firstOrNull { it.first == prefs.sourceCjkFontFamily }?.second ?: ""),
                            ) { display ->
                                EditorPrefs.SOURCE_CJK_FONT_FAMILIES.firstOrNull { L10n.t(it.second) == display }
                                    ?.let { onChange(prefs.copy(sourceCjkFontFamily = it.first)) }
                            }
                            StepperRow(L10n.t("源码缩进宽度"), "${prefs.sourceIndent}", onMinus = {
                                onChange(prefs.copy(sourceIndent = (prefs.sourceIndent - 1).coerceAtLeast(1)))
                            }, onPlus = {
                                onChange(prefs.copy(sourceIndent = (prefs.sourceIndent + 1).coerceAtMost(8)))
                            })
                        }
                    },
                    confirmButton = { TextButton(onClick = { showSourceSettings = false }) { Text(L10n.t("关闭")) } },
                )
            }
            if (showMarkdownSettings) {
                AlertDialog(
                    onDismissRequest = { showMarkdownSettings = false },
                    properties = DialogProperties(usePlatformDefaultWidth = false),
                    title = { Text(L10n.t("Markdown 行为")) },
                    text = {
                        Column(
                            modifier = Modifier.fillMaxWidth(0.92f).heightIn(max = 520.dp).verticalScroll(rememberScrollState()),
                        ) {
                            SwitchRow(L10n.t("转义文本中的 Markdown 符号"), prefs.escapeLiteralSymbols) { onChange(prefs.copy(escapeLiteralSymbols = it)) }
                            SwitchRow(L10n.t("转义 Markdown 字面量符号"), prefs.escapeMarkdownLiteralSymbols) { onChange(prefs.copy(escapeMarkdownLiteralSymbols = it)) }
                            SwitchRow(L10n.t("代码块拼写检查"), prefs.codeBlockSpellcheck) { onChange(prefs.copy(codeBlockSpellcheck = it)) }
                            SwitchRow(L10n.t("空行回车退出块"), prefs.exitBlockOnEmptyEnter) { onChange(prefs.copy(exitBlockOnEmptyEnter = it)) }
                            SwitchRow(L10n.t("Shift+Enter 插入硬换行"), prefs.useShiftEnterHardBreak) { onChange(prefs.copy(useShiftEnterHardBreak = it)) }
                            ChipRow(L10n.t("代码围栏"), EditorPrefs.CODE_FENCES.map { L10n.t(it.second) }, L10n.t(EditorPrefs.CODE_FENCES.firstOrNull { it.first == prefs.codeFence }?.second ?: "")) { display ->
                                EditorPrefs.CODE_FENCES.firstOrNull { L10n.t(it.second) == display }?.let { onChange(prefs.copy(codeFence = it.first)) }
                            }
                            ChipRow(L10n.t("强调标记"), EditorPrefs.EMPHASIS_MARKERS.map { L10n.t(it.second) }, L10n.t(EditorPrefs.EMPHASIS_MARKERS.firstOrNull { it.first == prefs.emphasisMarker }?.second ?: "")) { display ->
                                EditorPrefs.EMPHASIS_MARKERS.firstOrNull { L10n.t(it.second) == display }?.let { onChange(prefs.copy(emphasisMarker = it.first)) }
                            }
                            ChipRow(L10n.t("不安全强调标记"), EditorPrefs.UNSAFE_EMPHASIS_CHOICES.map { L10n.t(it.second) }, L10n.t(EditorPrefs.UNSAFE_EMPHASIS_CHOICES.firstOrNull { it.first == prefs.unsafeEmphasis }?.second ?: "")) { display ->
                                EditorPrefs.UNSAFE_EMPHASIS_CHOICES.firstOrNull { L10n.t(it.second) == display }?.let { onChange(prefs.copy(unsafeEmphasis = it.first)) }
                            }
                            ChipRow(L10n.t("列表标记"), EditorPrefs.BULLET_MARKERS.map { L10n.t(it.second) }, L10n.t(EditorPrefs.BULLET_MARKERS.firstOrNull { it.first == prefs.bulletMarker }?.second ?: "")) { display ->
                                EditorPrefs.BULLET_MARKERS.firstOrNull { L10n.t(it.second) == display }?.let { onChange(prefs.copy(bulletMarker = it.first)) }
                            }
                        }
                    },
                    confirmButton = { TextButton(onClick = { showMarkdownSettings = false }) { Text(L10n.t("关闭")) } },
                )
            }
            if (showStatusBarSettings) {
                AlertDialog(
                    onDismissRequest = { showStatusBarSettings = false },
                    properties = DialogProperties(usePlatformDefaultWidth = false),
                    title = { Text(L10n.t("状态栏设置")) },
                    text = {
                        Column(
                            modifier = Modifier
                                .fillMaxWidth(0.92f)
                                .verticalScroll(rememberScrollState()),
                        ) {
                            // 对齐桌面外观页「状态栏」：侧栏切换/命令状态为桌面专属，不提供
                            SwitchRow(L10n.t("显示光标位置"), prefs.statusBarPosition) { onChange(prefs.copy(statusBarPosition = it)) }
                            SwitchRow(L10n.t("显示块类型"), prefs.statusBarBlockType) { onChange(prefs.copy(statusBarBlockType = it)) }
                            SwitchRow(L10n.t("显示编码"), prefs.statusBarEncoding) { onChange(prefs.copy(statusBarEncoding = it)) }
                            SwitchRow(L10n.t("显示换行符"), prefs.statusBarNewline) { onChange(prefs.copy(statusBarNewline = it)) }
                            SwitchRow(L10n.t("显示源码/可视化切换"), prefs.statusBarModeToggle) { onChange(prefs.copy(statusBarModeToggle = it)) }
                            SwitchRow(L10n.t("显示缩放比例"), prefs.statusBarZoom) { onChange(prefs.copy(statusBarZoom = it)) }
                            SwitchRow(L10n.t("显示字符数"), prefs.statusBarWordCount) { onChange(prefs.copy(statusBarWordCount = it)) }
                        }
                    },
                    confirmButton = { TextButton(onClick = { showStatusBarSettings = false }) { Text(L10n.t("关闭")) } },
                )
            }
            // 恢复默认设置确认（对齐桌面 resetAll：整体重置为 AppSettings() 默认值）
            if (showResetConfirm) {
                AlertDialog(
                    onDismissRequest = { showResetConfirm = false },
                    title = { Text(L10n.t("重置所有设置为默认值？")) },
                    text = { Text(L10n.t("此操作会恢复默认设置，包括外观、缩放与图片设置。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            showResetConfirm = false
                            onChange(EditorPrefs())
                        }) {
                            Text(L10n.t("重置"), color = MaterialTheme.colorScheme.error)
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { showResetConfirm = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
            // 清除历史记录确认（对齐桌面：不可恢复删除前确认，destructive 红）
            if (showClearHistoryConfirm) {
                AlertDialog(
                    onDismissRequest = { showClearHistoryConfirm = false },
                    title = { Text(L10n.t("确定要清除历史记录吗？")) },
                    text = { Text(L10n.t("此操作会删除最近的文件和文件夹记录。")) },
                    confirmButton = {
                        TextButton(onClick = {
                            showClearHistoryConfirm = false
                            onClearHistory()
                        }) {
                            Text(
                                L10n.t("清除"),
                                color = MaterialTheme.colorScheme.error,
                            )
                        }
                    },
                    dismissButton = {
                        TextButton(onClick = { showClearHistoryConfirm = false }) { Text(L10n.t("取消")) }
                    },
                )
            }
        }
    }
}

/** 估算顶栏快捷操作可容纳数（即勾选上限）：屏幕宽扣除顶栏固定开销
 *  （汉堡按钮 48dp、标题预留 64dp、行水平边距 16dp）得可用宽，再按
 *  池序以 labelLarge 实测各标签文本宽 + 16dp 按钮内边距贪心累加。
 *  与 TopMenuBar 的 SubcomposeLayout 实测口径一致，随设备宽度自适应；
 *  下限 2、上限=候选池大小。标题预留按常见短名取值，文档标题过长时
 *  顶栏实际显示数会少于该上限（超出的项不渲染、不丢失）。 */
@Composable
private fun rememberQuickActionCapacity(): Int {
    val density = LocalDensity.current
    val screenWidth = LocalConfiguration.current.screenWidthDp.dp
    val measurer = rememberTextMeasurer()
    val labelStyle = MaterialTheme.typography.labelLarge
    val language = L10n.language
    return remember(screenWidth, language) {
        with(density) {
            var available = (screenWidth - 48.dp - 64.dp - 16.dp).toPx()
            var count = 0
            for ((_, display) in EditorPrefs.QUICK_ACTION_POOL) {
                val w = measurer.measure(
                    text = androidx.compose.ui.text.AnnotatedString(L10n.translate(display, language)),
                    style = labelStyle,
                ).size.width + 16.dp.toPx()
                if (available < w) break
                available -= w
                count++
            }
            count.coerceIn(2, EditorPrefs.QUICK_ACTION_POOL.size)
        }
    }
}

@Composable
private fun SectionTitle(title: String) {
    Text(
        title,
        style = MaterialTheme.typography.titleSmall,
        fontWeight = FontWeight.Bold,
        color = MaterialTheme.colorScheme.primary,
        modifier = Modifier.padding(top = 18.dp, bottom = 6.dp),
    )
    HorizontalDivider()
}

/** 组内小节标题：沿用桌面偏好设置页内 header（字号更小、次级色）。 */
@Composable
private fun GroupHeader(title: String) {
    Text(
        title,
        style = MaterialTheme.typography.labelLarge,
        fontWeight = FontWeight.Medium,
        color = MaterialTheme.colorScheme.onSurfaceVariant,
        modifier = Modifier.padding(top = 12.dp, bottom = 2.dp),
    )
}

@Composable
private fun LinkRow(label: String, onClick: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        TextButton(onClick = onClick) { Text(label) }
    }
}

@Composable
private fun StepperRow(label: String, value: String, step: Int = 1, onMinus: () -> Unit, onPlus: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        TextButton(onClick = onMinus) { Text(if (step >= 10) "−" else "−") }
        Text(value, style = MaterialTheme.typography.bodyLarge, modifier = Modifier.padding(horizontal = 8.dp))
        TextButton(onClick = onPlus) { Text("+") }
    }
}

@Composable
private fun ChipRow(
    label: String,
    options: List<String>,
    selected: String,
    enabled: Boolean = true,
    onSelect: (String) -> Unit,
) {
    val compact = LocalConfiguration.current.screenWidthDp < 600
    @Composable
    fun Options() {
        androidx.compose.foundation.layout.FlowRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
            verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
        ) {
            options.forEach { option ->
                FilterChip(
                    selected = option == selected,
                    onClick = { onSelect(option) },
                    enabled = enabled,
                    label = { Text(option, style = MaterialTheme.typography.labelMedium) },
                )
            }
        }
    }
    if (compact) {
        Column(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
            Text(
                label,
                style = MaterialTheme.typography.bodyLarge,
                color = if (enabled) MaterialTheme.colorScheme.onSurface
                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
            )
            Options()
        }
    } else {
        Row(
            modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                label,
                modifier = Modifier.weight(1f),
                style = MaterialTheme.typography.bodyLarge,
                color = if (enabled) MaterialTheme.colorScheme.onSurface
                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f),
            )
            androidx.compose.foundation.layout.Box(modifier = Modifier.weight(2.2f)) { Options() }
        }
    }
}

@Composable
private fun ThemeChipRow(
    label: String,
    options: List<StyleAssets.ColorTheme>,
    selectedId: String,
    onSelect: (String) -> Unit,
    enabled: Boolean = true,
) {
    val compact = LocalConfiguration.current.screenWidthDp < 600
    @Composable
    fun Options() {
        androidx.compose.foundation.layout.FlowRow(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(6.dp),
            verticalArrangement = androidx.compose.foundation.layout.Arrangement.spacedBy(2.dp),
        ) {
            options.forEach { option ->
                FilterChip(
                    selected = option.id == selectedId,
                    onClick = { onSelect(option.id) },
                    enabled = enabled,
                    label = { Text(L10n.t(option.name), style = MaterialTheme.typography.labelMedium) },
                )
            }
        }
    }
    if (compact) {
        Column(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp)) {
            Text(label, style = MaterialTheme.typography.bodyLarge,
                color = if (enabled) MaterialTheme.colorScheme.onSurface
                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f))
            Options()
        }
    } else {
        Row(modifier = Modifier.fillMaxWidth().padding(vertical = 6.dp), verticalAlignment = Alignment.CenterVertically) {
            Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge,
                color = if (enabled) MaterialTheme.colorScheme.onSurface
                else MaterialTheme.colorScheme.onSurface.copy(alpha = 0.38f))
            androidx.compose.foundation.layout.Box(modifier = Modifier.weight(2.2f)) { Options() }
        }
    }
}

@Composable
private fun SwitchRow(label: String, checked: Boolean, onChange: (Boolean) -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(vertical = 2.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(label, modifier = Modifier.weight(1f), style = MaterialTheme.typography.bodyLarge)
        Switch(checked = checked, onCheckedChange = onChange)
    }
}

/** 关于对话框（对齐桌面 App 菜单「关于 MarkLeaf」）。 */
@Composable
fun AboutDialog(version: String, onDismiss: () -> Unit) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(L10n.t("关于 MarkLeaf")) },
        text = {
            Column {
                Text("MarkLeaf Android", style = MaterialTheme.typography.bodyLarge)
                Text(
                    "版本 $version",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(L10n.t("关闭")) } },
    )
}
