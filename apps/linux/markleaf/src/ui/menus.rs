// 菜单模型:主菜单(HeaderBar MenuButton)与上下文菜单。
// 菜单项全部指向 win. 动作(见 window.rs install_actions);纯内核命令走
// 统一的 win.command::<id>(command_router 按桌面路由表分派)。
use gtk::gio;

use crate::l10n::tr;

/// 菜单文案查表(l10n 接线);zh-Hans 直返原文。
fn label(lang: &str, key: &str) -> String {
    tr(lang, key).unwrap_or_else(|| key.to_string())
}

/// 内核命令条目(对齐 macOS NativeMenuBuilder 的内核透传集)。
fn command_item(menu: &gio::Menu, lang: &str, title: &str, command: &str) {
    menu.append(Some(&label(lang, title)), Some(&format!("win.command::{command}")));
}

fn section(parent: &gio::Menu) -> gio::Menu {
    let section = gio::Menu::new();
    parent.append_section(None, &section);
    section
}

/// 主菜单:文件/编辑/插入/格式/视图/帮助(对齐桌面七组结构的可实现子集)。
/// recent:最近文件条目((name, "win.openRecent::<index>");按 settings.recent_files 顺序)。
pub fn main_menu_model(lang: &str, recent: &[(String, String)]) -> gio::Menu {
    let menu = gio::Menu::new();

    // ---- 文件 ----
    let file = gio::Menu::new();
    let new_doc = gio::Menu::new();
    new_doc.append(Some(&label(lang, "Markdown 文档")), Some("win.newMarkdown"));
    new_doc.append(Some(&label(lang, "纯文本")), Some("win.newPlainText"));
    file.append_submenu(Some(&label(lang, "新建")), &new_doc);
    file.append(Some(&label(lang, "新建窗口")), Some("win.command::newWindow"));
    file.append(Some(&label(lang, "打开")), Some("win.open"));
    if !recent.is_empty() {
        let recent_menu = gio::Menu::new();
        for (index, (name, _)) in recent.iter().enumerate() {
            recent_menu.append(
                Some(&format!("{}  {}", index + 1, name)),
                Some(&format!("win.openRecent::{index}")),
            );
        }
        file.append_submenu(Some(&label(lang, "最近项目")), &recent_menu);
        file.append(
            Some(&label(lang, "恢复未保存的文件…")),
            Some("win.command::recoverUnsavedFiles"),
        );
    }
    let file_save = section(&file);
    file_save.append(Some(&label(lang, "保存")), Some("win.save"));
    file_save.append(Some(&label(lang, "保存全部")), Some("win.command::saveAll"));
    file_save.append(Some(&label(lang, "另存为")), Some("win.saveAs"));
    let file_export = section(&file);
    let export = gio::Menu::new();
    export.append(Some(&label(lang, "导出 HTML")), Some("win.exportHtml"));
    export.append(Some(&label(lang, "导出长图")), Some("win.exportImage"));
    export.append(Some(&label(lang, "打印")), Some("win.print"));
    file_export.append_submenu(Some(&label(lang, "导出")), &export);
    let file_close = section(&file);
    file_close.append(Some(&label(lang, "分享")), Some("win.share"));
    file_close.append(Some(&label(lang, "关闭")), Some("win.close-window"));
    menu.append_submenu(Some(&label(lang, "文件")), &file);

    // ---- 编辑 ----
    let edit = gio::Menu::new();
    command_item(&edit, lang, "撤销", "undo");
    command_item(&edit, lang, "重做", "redo");
    let edit_clip = section(&edit);
    command_item(&edit_clip, lang, "剪切", "cut");
    command_item(&edit_clip, lang, "拷贝", "copy");
    command_item(&edit_clip, lang, "粘贴", "paste");
    command_item(&edit_clip, lang, "粘贴为纯文本", "pastePlainText");
    let edit_find = section(&edit);
    edit_find.append(Some(&label(lang, "查找与替换")), Some("win.find"));
    command_item(&edit_find, lang, "全选", "selectAll");
    menu.append_submenu(Some(&label(lang, "编辑")), &edit);

    // ---- 插入 ----
    let insert = gio::Menu::new();
    command_item(&insert, lang, "插入表格", "insertTable");
    command_item(&insert, lang, "水平线", "insertHorizontalRule");
    command_item(&insert, lang, "插入 Mermaid 图表", "insertMermaid");
    command_item(&insert, lang, "插入脚注…", "insertFootnote");
    let insert_math = section(&insert);
    command_item(&insert_math, lang, "行内公式", "insertMathInline");
    command_item(&insert_math, lang, "块级公式", "insertMathBlock");
    let insert_meta = section(&insert);
    command_item(&insert_meta, lang, "YAML 前置元数据", "showFrontMatter");
    menu.append_submenu(Some(&label(lang, "插入")), &insert);

    // ---- 格式 ----
    let format = gio::Menu::new();
    command_item(&format, lang, "加粗", "toggleBold");
    command_item(&format, lang, "斜体", "toggleItalic");
    command_item(&format, lang, "下划线", "toggleUnderline");
    command_item(&format, lang, "删除线", "toggleStrike");
    command_item(&format, lang, "高亮", "toggleHighlight");
    command_item(&format, lang, "行内代码", "toggleCode");
    let format_paragraph = section(&format);
    let paragraph_style = gio::Menu::new();
    command_item(&paragraph_style, lang, "正文", "setParagraph");
    command_item(&paragraph_style, lang, "一级标题", "setHeading1");
    command_item(&paragraph_style, lang, "二级标题", "setHeading2");
    command_item(&paragraph_style, lang, "三级标题", "setHeading3");
    command_item(&paragraph_style, lang, "四级标题", "setHeading4");
    command_item(&paragraph_style, lang, "五级标题", "setHeading5");
    command_item(&paragraph_style, lang, "六级标题", "setHeading6");
    format_paragraph.append_submenu(Some(&label(lang, "段落样式")), &paragraph_style);
    let lists = gio::Menu::new();
    command_item(&lists, lang, "子弹列表", "toggleBulletList");
    command_item(&lists, lang, "有序列表", "toggleOrderedList");
    command_item(&lists, lang, "任务列表", "toggleTaskList");
    format_paragraph.append_submenu(Some(&label(lang, "列表")), &lists);
    command_item(&format_paragraph, lang, "引用", "toggleBlockquote");
    command_item(&format_paragraph, lang, "代码块", "toggleCodeBlock");
    let alerts = gio::Menu::new();
    command_item(&alerts, lang, "备注", "insertAlertNote");
    command_item(&alerts, lang, "提示", "insertAlertTip");
    command_item(&alerts, lang, "重要", "insertAlertImportant");
    command_item(&alerts, lang, "注意", "insertAlertCaution");
    command_item(&alerts, lang, "警告", "insertAlertWarning");
    format_paragraph.append_submenu(Some(&label(lang, "提示框")), &alerts);
    let format_ops = section(&format);
    command_item(&format_ops, lang, "段前插入行", "insertLineBefore");
    command_item(&format_ops, lang, "段后插入行", "insertLineAfter");
    command_item(&format_ops, lang, "清除格式", "clearFormat");
    menu.append_submenu(Some(&label(lang, "格式")), &format);

    // ---- 视图 ----
    let view = gio::Menu::new();
    view.append(Some(&label(lang, "源码模式")), Some("win.sourceMode"));
    command_item(&view, lang, "显示侧栏", "toggleSidebar");
    command_item(&view, lang, "只读模式", "toggleReadOnlyMode");
    let view_zoom = section(&view);
    command_item(&view_zoom, lang, "放大", "zoomIn");
    command_item(&view_zoom, lang, "缩小", "zoomOut");
    command_item(&view_zoom, lang, "重置缩放", "resetZoom");
    let view_prefs = section(&view);
    view_prefs.append(Some(&label(lang, "设置")), Some("win.showPreferences"));
    menu.append_submenu(Some(&label(lang, "视图")), &view);

    // ---- 帮助 ----
    let help = gio::Menu::new();
    help.append(Some(&label(lang, "关于 MarkLeaf")), Some("win.showAbout"));
    menu.append_submenu(Some(&label(lang, "帮助")), &help);

    menu
}

/// 上下文菜单:按桌面规范精简的命令集(参数动作 win.ctx-command)。
pub fn context_menu_model(lang: &str) -> gio::Menu {
    let menu = gio::Menu::new();
    for (key, command) in [
        ("编辑", "cut"),
        ("复制", "copy"),
        ("粘贴", "paste"),
        ("撤销", "undo"),
        ("重做", "redo"),
        ("加粗", "toggleBold"),
        ("斜体", "toggleItalic"),
        ("插入表格", "insertTable"),
        ("清除格式", "clearFormat"),
        ("声明代码语言", "setCodeBlockLanguage"),
    ] {
        menu.append(Some(&label(lang, key)), Some(&format!("win.ctx-command::{command}")));
    }
    menu
}
