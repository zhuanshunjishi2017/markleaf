// 菜单模型:主菜单(HeaderBar MenuButton)与上下文菜单。
// 菜单项全部指向 win. 动作(见 window.rs install_actions / ctx-command 参数)。
use gtk::gio;

use crate::l10n::tr;

/// 菜单文案查表(l10n 接线);zh-Hans 直返原文。
fn label(lang: &str, key: &str) -> String {
    tr(lang, key).unwrap_or_else(|| key.to_string())
}

/// 主菜单:文档/视图/导出分组(对齐桌面菜单结构,精简到 Linux 首发集合)。
pub fn main_menu_model(lang: &str) -> gio::Menu {
    let menu = gio::Menu::new();

    let document = gio::Menu::new();
    document.append(Some(&label(lang, "打开")), Some("win.open"));
    document.append(Some(&label(lang, "保存")), Some("win.save"));
    document.append(Some(&label(lang, "另存为")), Some("win.saveAs"));
    document.append(Some(&label(lang, "关闭")), Some("win.close-window"));
    menu.append_submenu(Some(&label(lang, "文件")), &document);

    let view = gio::Menu::new();
    view.append(Some(&label(lang, "源码模式")), Some("win.sourceMode"));
    view.append(Some(&label(lang, "查找")), Some("win.find"));
    view.append(Some(&label(lang, "设置")), Some("win.showPreferences"));
    menu.append_submenu(Some(&label(lang, "视图")), &view);

    let export = gio::Menu::new();
    export.append(Some(&label(lang, "导出 HTML")), Some("win.exportHtml"));
    export.append(Some(&label(lang, "导出长图")), Some("win.exportImage"));
    export.append(Some(&label(lang, "打印")), Some("win.print"));
    menu.append_submenu(Some(&label(lang, "导出")), &export);

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
    ] {
        menu.append(Some(&label(lang, key)), Some(&format!("win.ctx-command::{command}")));
    }
    menu
}
