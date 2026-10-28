// 菜单模型:主菜单(HeaderBar MenuButton)与上下文菜单。
// 菜单项全部指向 win. 动作(见 window.rs install_actions / ctx-command 参数)。
use gtk::gio;

/// 主菜单:文档/视图/导出/帮助分组(对齐桌面菜单结构,精简到 Linux 首发集合)。
pub fn main_menu_model() -> gio::Menu {
    let menu = gio::Menu::new();

    let document = gio::Menu::new();
    document.append(Some("打开"), Some("win.open"));
    document.append(Some("保存"), Some("win.save"));
    document.append(Some("另存为"), Some("win.saveAs"));
    document.append(Some("关闭窗口"), Some("win.close-window"));
    menu.append_submenu(Some("文件"), &document);

    let view = gio::Menu::new();
    view.append(Some("源码模式"), Some("win.sourceMode"));
    menu.append_submenu(Some("视图"), &view);

    let export = gio::Menu::new();
    export.append(Some("导出 HTML"), Some("win.exportHtml"));
    export.append(Some("导出长图"), Some("win.exportImage"));
    export.append(Some("打印"), Some("win.print"));
    menu.append_submenu(Some("导出"), &export);

    menu
}

/// 上下文菜单:按桌面规范精简的命令集(参数动作 win.ctx-command)。
pub fn context_menu_model() -> gio::Menu {
    let menu = gio::Menu::new();
    for (label, command) in [
        ("剪切", "cut"),
        ("复制", "copy"),
        ("粘贴", "paste"),
        ("撤销", "undo"),
        ("重做", "redo"),
        ("加粗", "toggleBold"),
        ("斜体", "toggleItalic"),
        ("插入表格", "insertTable"),
        ("清除格式", "clearFormat"),
    ] {
        menu.append(Some(label), Some(&format!("win.ctx-command::{command}")));
    }
    menu
}
