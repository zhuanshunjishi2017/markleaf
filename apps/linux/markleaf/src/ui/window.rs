// AppWindow:GTK 壳——HeaderBar、每标签一个常驻 EditorWebView(spec D3)、
// 文档生命周期(打开/保存/恢复)、上下文菜单、状态栏、深色跟随、动作与加速键。
// 侧栏(工作区树/大纲)见 sidebar.rs;菜单构建见 menus.rs。
use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use adw::prelude::*;
use gtk::gio;
use gtk::glib;
use gtk::pango;
use gtk::PopoverMenu;

use crate::bridge::command_router::{route, CommandRoute};
use crate::bridge::messages::{EditorMessage, EditorMessageType, HostMessage};
use crate::bridge::session_state::EditorSessionState;
use crate::data::recovery::{discard_recovery, save_recovery, RecoveryEntry};
use crate::data::settings::AppSettings;
use crate::data::settings::StatusBarCommandDisplayMode;
use crate::documents::controller::EncodingDecision;
use crate::documents::encoding::{apply_newline, detect_newline, detect, encode};
use crate::documents::store::{read_document, write_document, DocumentSnapshot, WriteError};
use crate::editor::webview::{EditorWebView, WebContext};
use webkit::prelude::WebViewExt;
use webkit::{PrintOperation, SnapshotOptions, SnapshotRegion};
use crate::ui::menus;

fn install_tab_bar_actions(tab_bar: &adw::TabBar, view: &adw::TabView, lang: &str) {
    let new_tab_label = crate::l10n::tr(lang, "新建标签").unwrap_or_else(|| "新建标签".into());
    let new_tab = gtk::Button::from_icon_name("list-add-symbolic");
    new_tab.add_css_class("flat");
    new_tab.set_tooltip_text(Some(&new_tab_label));
    new_tab.update_property(&[gtk::accessible::Property::Label(&new_tab_label)]);
    new_tab.set_action_name(Some("win.newMarkdown"));

    let label = crate::l10n::tr(lang, "所有标签").unwrap_or_else(|| "所有标签".into());
    let button = gtk::MenuButton::builder()
        .direction(gtk::ArrowType::Down)
        .has_frame(false)
        .tooltip_text(&label)
        .build();
    button.update_property(&[gtk::accessible::Property::Label(&label)]);

    let popover = gtk::Popover::new();
    let list = gtk::ListBox::builder()
        .selection_mode(gtk::SelectionMode::None)
        .build();
    list.add_css_class("navigation-sidebar");
    list.bind_model(Some(&view.pages()), |item| {
        let page = item.downcast_ref::<adw::TabPage>().expect("tab view page");
        let row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let check = gtk::Image::from_icon_name("object-select-symbolic");
        page.bind_property("selected", &check, "opacity")
            .transform_to(|_, selected: bool| Some(if selected { 1.0 } else { 0.0 }))
            .sync_create()
            .build();
        let title = gtk::Label::builder()
            .xalign(0.0)
            .hexpand(true)
            .ellipsize(pango::EllipsizeMode::End)
            .max_width_chars(36)
            .build();
        page.bind_property("title", &title, "label").sync_create().build();
        row.append(&check);
        row.append(&title);
        row.upcast()
    });
    let scroll = gtk::ScrolledWindow::builder()
        .hscrollbar_policy(gtk::PolicyType::Never)
        .min_content_width(220)
        .max_content_height(360)
        .propagate_natural_width(true)
        .propagate_natural_height(true)
        .child(&list)
        .build();
    popover.set_child(Some(&scroll));
    let weak_view = view.downgrade();
    let weak_popover = popover.downgrade();
    list.connect_row_activated(move |_, row| {
        if let Some(view) = weak_view.upgrade() {
            view.set_selected_page(&view.nth_page(row.index()));
        }
        if let Some(popover) = weak_popover.upgrade() {
            popover.popdown();
        }
    });
    button.set_popover(Some(&popover));
    tab_bar.bind_property("is-overflowing", &button, "visible").sync_create().build();
    let actions = gtk::Box::new(gtk::Orientation::Horizontal, 0);
    actions.append(&new_tab);
    actions.append(&button);
    tab_bar.set_end_action_widget(Some(&actions));
}

fn install_status_choice_menu(
    button: &gtk::MenuButton,
    options: &[&'static str],
    reset: Option<(&'static str, &'static str)>,
    show_other_value: bool,
    select: impl Fn(&str) + 'static,
) {
    let popover = gtk::Popover::new();
    let list = gtk::Box::new(gtk::Orientation::Vertical, 0);
    list.set_margin_top(6);
    list.set_margin_bottom(6);
    let select = Rc::new(select);
    let mut markers = Vec::new();

    let current = gtk::Label::new(None);
    let current_row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
    current_row.append(&gtk::Image::from_icon_name("object-select-symbolic"));
    current_row.append(&current);
    current_row.set_margin_start(8);
    current_row.set_margin_end(8);
    current_row.set_margin_top(6);
    current_row.set_margin_bottom(6);
    current_row.set_visible(false);
    if show_other_value {
        list.append(&current_row);
    }

    for (label, value, track) in options
        .iter()
        .map(|&value| (value, value, true))
        .chain(reset.map(|(label, value)| (label, value, false)))
    {
        if !track {
            list.append(&gtk::Separator::new(gtk::Orientation::Horizontal));
        }
        let row = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        let check = gtk::Image::from_icon_name("object-select-symbolic");
        check.set_opacity(0.0);
        row.append(&check);
        row.append(&gtk::Label::new(Some(label)));
        if track {
            markers.push((value, check));
        }
        let item = gtk::Button::builder().has_frame(false).child(&row).build();
        item.update_property(&[gtk::accessible::Property::Label(label)]);
        let weak_button = button.downgrade();
        let select = select.clone();
        item.connect_clicked(move |_| {
            if let Some(button) = weak_button.upgrade() {
                button.popdown();
                if button.label().as_deref() != Some(value) {
                    select(value);
                }
            }
        });
        list.append(&item);
    }
    button.connect_label_notify(move |button| {
        let label = button.label();
        let mut matched = false;
        for (value, check) in &markers {
            let selected = label.as_deref() == Some(*value);
            check.set_opacity(if selected { 1.0 } else { 0.0 });
            matched |= selected;
        }
        let label = label.as_deref().unwrap_or("");
        current.set_text(label);
        current_row.set_visible(show_other_value && !matched && !label.is_empty());
    });
    popover.set_child(Some(&list));
    button.set_popover(Some(&popover));
    button.notify("label");
}

/// 会话清单条目(退出时由 app 层汇总)。
pub struct WindowSnapshot {
    pub tabs: Vec<String>,
    pub active_tab: usize,
}

/// 每标签的文档状态(标签关闭即释放;WebView 常驻由 TabView 托管)。
struct TabState {
    page: adw::TabPage,
    webview: EditorWebView,
    startup_cover: gtk::Box,
    session: Rc<RefCell<EditorSessionState>>,
    document: RefCell<TabDocument>,
    /// dirty 后恢复快照防抖(glib 定时源 id;标签关闭时移除)。
    recovery_source: RefCell<Option<glib::SourceId>>,
    /// 该标签的 WebView 缩放(zoomWheel 驱动)。
    zoom: std::cell::Cell<f64>,
    /// 源码模式镜像(selectionChanged.sourceMode 驱动;大纲跳转分流用)。
    source_mode: std::cell::Cell<bool>,
    /// 页面 ready 携带的自生成占位 documentId(handle_editor_message 捕获,
    /// send_after_ready 的 ready 回调采纳重绑会话;对齐 Android)。
    frontend_ready_id: RefCell<Option<String>>,
    /// 最近一次 editorStatusChanged 载荷(状态栏/统计对话框数据源)。
    last_editor_status: RefCell<Option<serde_json::Value>>,
    close_after_save: std::cell::Cell<bool>,
    /// 只读模式(宿主切换;内核状态以 readOnly 面板字段为准)。
    read_only: std::cell::Cell<bool>,
}

struct TabDocument {
    path: Option<PathBuf>,
    disk: DocumentSnapshot,
    encoding: EncodingDecision,
    newline: crate::documents::encoding::NewLinePolicy,
    document_id: String,
    /// 文档类型(markdown/plainText;新建纯文本时与 md 区分)。
    document_type: String,
    dirty: bool,
    /// 等待 snapshot 回包的保存会话(有 requestId 的保存)。
    pending_save: bool,
    /// 恢复载入的内容(stylesApplied 后作为 loadDocument 正文,置脏)。
    recovered_markdown: Option<String>,
}

impl Default for TabDocument {
    fn default() -> Self {
        Self {
            path: None,
            disk: DocumentSnapshot::default(),
            encoding: EncodingDecision {
                encoding: encoding_rs::UTF_8,
                bom: false,
            },
            newline: crate::documents::encoding::NewLinePolicy::Lf,
            document_id: String::new(),
            document_type: "markdown".into(),
            dirty: false,
            pending_save: false,
            recovered_markdown: None,
        }
    }
}

struct SidebarAnimation {
    source: glib::SourceId,
    opening: bool,
}

pub struct AppWindow {
    window: adw::ApplicationWindow,
    context: WebContext,
    tabs: RefCell<adw::TabView>,
    /// 标签状态按 TabPage 指针索引。
    tab_states: RefCell<Vec<Rc<TabState>>>,
    status_text: RefCell<gtk::Label>,
    /// 临时命令状态文本的清空定时器(Temporary 模式)。
    status_timeout: RefCell<Option<glib::SourceId>>,
    /// 状态栏右区控件(可见性由 settings.status_bar 驱动)。
    sidebar_toggle_button: RefCell<gtk::Button>,
    character_count_button: RefCell<gtk::Button>,
    block_type_label: RefCell<gtk::Label>,
    position_label: RefCell<gtk::Label>,
    encoding_button: RefCell<gtk::MenuButton>,
    newline_button: RefCell<gtk::MenuButton>,
    mode_button: RefCell<gtk::Button>,
    zoom_button: RefCell<gtk::MenuButton>,
    #[allow(dead_code)] // M4 主题接线时消费(深浅/主题 id 下发 applyStyles)
    settings: Rc<RefCell<AppSettings>>,
    /// 上下文菜单浮层(每次请求重建内容后弹出)。
    context_popover: RefCell<Option<PopoverMenu>>,
    /// 当前打开的动态右键 Popover(防消息多播导致重复弹出)。
    active_context_popover: RefCell<Option<gtk::Popover>>,

    sidebar: Rc<super::sidebar::Sidebar>,
    paned: gtk::Paned,
    /// 侧栏显示时记忆的宽度(隐藏切换用)。
    sidebar_width_memory: std::cell::Cell<i32>,
    sidebar_animation: RefCell<Option<SidebarAnimation>>,
    /// 界面语言(设置显式值或 glib locale 解析)。
    lang: String,
    /// 工作区根(供 watcher 重扫)。
    workspace_root: RefCell<Option<PathBuf>>,
    /// 工作区监视器(保持存活维持 inotify 订阅)。
    watcher: RefCell<Option<crate::workspace::watcher::WorkspaceWatcher>>,
    /// 监视器回调(notify 线程)置位,主循环 500ms 轮询消费——Rc 非 Send 的桥接。
    needs_rescan: std::sync::Arc<std::sync::atomic::AtomicBool>,
    find_bar: gtk::SearchBar,
    find_entry: gtk::SearchEntry,
    windows: Rc<RefCell<Vec<Rc<AppWindow>>>>,
}

/// 恢复快照防抖间隔(对齐 settings.snapshot_interval_seconds 的下限用法:脏即定时落)。
const RECOVERY_DEBOUNCE_MS: u64 = 3_000;

/// 内核 blockType → 状态栏显示名(对齐 macOS blockTypeDisplayName 精简集)。
fn block_type_display_name(block_type: &str) -> String {
    match block_type {
        "paragraph" => "段落".into(),
        "codeBlock" => "代码块".into(),
        "heading" => "标题".into(),
        "blockquote" => "引用".into(),
        "listItem" | "bulletList" | "orderedList" | "taskList" => "列表".into(),
        "table" => "表格".into(),
        "mathBlock" => "公式".into(),
        "mermaid" => "Mermaid".into(),
        "frontMatter" => "Front Matter".into(),
        "" => "—".into(),
        other => other.into(),
    }
}

impl AppWindow {
    pub fn new(
        app: &adw::Application,
        context: &WebContext,
        settings: Rc<RefCell<AppSettings>>,
        windows: Rc<RefCell<Vec<Rc<AppWindow>>>>,
    ) -> Rc<Self> {
        let window = adw::ApplicationWindow::builder()
            .application(app)
            .title("MarkLeaf")
            .default_width(1100)
            .default_height(760)
            .build();

        let tab_view = adw::TabView::new();
        tab_view.add_css_class("view");
        // WebKit can report a zero minimum height; give the active page the
        // space left below the tab bar.
        tab_view.set_vexpand(true);
        let tab_bar = adw::TabBar::builder().view(&tab_view).autohide(false).build();

        // 状态栏:左(侧栏按钮+命令状态文本)+弹性+右(字数/块类型/位置/编码/换行/模式/缩放)。
        let status_bar = gtk::Box::new(gtk::Orientation::Horizontal, 8);
        status_bar.set_margin_start(10);
        status_bar.set_margin_end(10);
        status_bar.set_margin_top(3);
        status_bar.set_margin_bottom(3);

        let sidebar_toggle_button = gtk::Button::from_icon_name("sidebar-show-symbolic");
        sidebar_toggle_button.set_tooltip_text(Some("显示/隐藏侧栏"));
        sidebar_toggle_button.add_css_class("flat");
        let status_label = gtk::Label::builder()
            .halign(gtk::Align::Start)
            .ellipsize(pango::EllipsizeMode::End)
            .build();
        let status_spacer = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        status_spacer.set_hexpand(true);

        let character_count_button = gtk::Button::new();
        character_count_button.add_css_class("flat");
        character_count_button.set_tooltip_text(Some("文档统计"));
        let block_type_label = gtk::Label::default();
        let position_label = gtk::Label::default();

        let make_menu_button = |label: &str, tip: &str| {
            gtk::MenuButton::builder()
                .label(label)
                .has_frame(false)
                .tooltip_text(tip)
                .build()
        };
        let encoding_button = make_menu_button("", "编码");
        let newline_button = make_menu_button("", "换行符");
        let mode_button = gtk::Button::with_label("可视化");
        mode_button.add_css_class("flat");
        mode_button.set_tooltip_text(Some("切换编辑模式"));
        let zoom_button = make_menu_button("100%", "设置缩放");

        status_bar.append(&sidebar_toggle_button);
        status_bar.append(&status_label);
        status_bar.append(&status_spacer);
        status_bar.append(&character_count_button);
        status_bar.append(&block_type_label);
        status_bar.append(&position_label);
        status_bar.append(&encoding_button);
        status_bar.append(&newline_button);
        status_bar.append(&mode_button);
        status_bar.append(&zoom_button);

        // 查找面板:宿主侧输入,向内核发 findText/findNext/findPrev/findClose。
        let find_search_bar = gtk::SearchBar::builder().show_close_button(true).build();
        let find_entry = gtk::SearchEntry::new();
        let find_next = gtk::Button::from_icon_name("go-down-symbolic");
        let find_prev = gtk::Button::from_icon_name("go-up-symbolic");
        let find_close = gtk::Button::from_icon_name("window-close-symbolic");
        let find_box = gtk::Box::new(gtk::Orientation::Horizontal, 4);
        find_box.append(&find_entry);
        find_box.append(&find_prev);
        find_box.append(&find_next);
        find_box.append(&find_close);
        find_search_bar.set_child(Some(&find_box));

        // 界面语言:设置显式值或 glib locale 解析(菜单/工具提示/状态文案用)。
        let lang = crate::l10n::resolve_language(&settings.borrow().display_language);
        install_tab_bar_actions(&tab_bar, &tab_view, &lang);

        // HeaderBar:打开/保存/菜单/源码切换/缩放。
        let header = gtk::HeaderBar::new();
        let open_button = gtk::Button::from_icon_name("document-open-symbolic");
        let save_button = gtk::Button::from_icon_name("document-save-symbolic");
        let source_button = gtk::ToggleButton::new();
        source_button.set_icon_name("view-reveal-symbolic");
        let menu_button = gtk::MenuButton::new();
        menu_button.set_icon_name("open-menu-symbolic");
        let workspace_root: Rc<RefCell<Option<PathBuf>>> = Rc::new(RefCell::new(None));
        let workspace_root_for_menu = workspace_root.clone();
        let recent_files_for_menu: Vec<(String, String)> = settings
            .borrow()
            .recent_files
            .iter()
            .map(|entry| (entry.name.clone(), entry.path.clone()))
            .collect();
        let recent_folders_for_menu: Vec<(String, String)> = settings
            .borrow()
            .recent_folders
            .iter()
            .map(|entry| (entry.name.clone(), entry.path.clone()))
            .collect();
        let has_workspace_for_menu = workspace_root_for_menu.borrow().is_some();
        menu_button.set_menu_model(Some(&menus::main_menu_model(
            &lang,
            &recent_files_for_menu,
            &recent_folders_for_menu,
            has_workspace_for_menu,
        )));
        let tr_open = crate::l10n::tr(&lang, "打开").unwrap_or_default();
        let tr_save = crate::l10n::tr(&lang, "保存").unwrap_or_default();
        let tr_source = crate::l10n::tr(&lang, "源码模式").unwrap_or_default();
        open_button.set_tooltip_text(Some(&tr_open));
        save_button.set_tooltip_text(Some(&tr_save));
        source_button.set_tooltip_text(Some(&tr_source));
        header.pack_start(&open_button);
        header.pack_start(&save_button);
        header.pack_start(&source_button);
        header.pack_end(&menu_button);

        let sidebar = super::sidebar::Sidebar::new(&lang);
        let tab_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        tab_box.append(&tab_bar);
        tab_box.append(&tab_view);
        // Paned 分隔条可拖拽调整侧栏宽度(OverlaySplitView 不支持)。
        let paned = gtk::Paned::new(gtk::Orientation::Horizontal);
        paned.set_start_child(Some(&sidebar.root));
        paned.set_end_child(Some(&tab_box));
        paned.set_position(settings.borrow().sidebar_width.clamp(232, 800));

        let content = gtk::Box::new(gtk::Orientation::Vertical, 0);
        content.append(&header);
        content.append(&find_search_bar);
        content.append(&paned);
        content.append(&status_bar);
        window.set_content(Some(&content));

        let sidebar_for_handler = sidebar.clone();

        let initial_sidebar_width = settings.borrow().sidebar_width.clamp(232, 800);
        let this = Rc::new(Self {
            window,
            context: context.clone(),
            tabs: RefCell::new(tab_view),
            tab_states: RefCell::new(Vec::new()),
            status_text: RefCell::new(status_label),
            status_timeout: RefCell::new(None),
            sidebar_toggle_button: RefCell::new(sidebar_toggle_button),
            character_count_button: RefCell::new(character_count_button),
            block_type_label: RefCell::new(block_type_label),
            position_label: RefCell::new(position_label),
            encoding_button: RefCell::new(encoding_button),
            newline_button: RefCell::new(newline_button),
            mode_button: RefCell::new(mode_button),
            zoom_button: RefCell::new(zoom_button),
            settings,
            context_popover: RefCell::new(None),
            active_context_popover: RefCell::new(None),
            sidebar,
            paned: paned.clone(),
            sidebar_width_memory: std::cell::Cell::new(initial_sidebar_width),
            sidebar_animation: RefCell::new(None),
            lang,
            workspace_root,
            watcher: RefCell::new(None),
            needs_rescan: std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false)),
            find_bar: find_search_bar.clone(),
            find_entry: find_entry.clone(),
            windows: windows.clone(),
        });

        {
            let window = Rc::downgrade(&this);
            sidebar_for_handler.set_open_handler(move |path| {
                if let Some(window) = window.upgrade() {
                    window.open_document_in_tab(path);
                }
            });
        }
        // 工作区右键命令分派(文件系统变更/剪贴板/刷新/关闭)。
        {
            let this_for_ctx = this.clone();
            sidebar_for_handler.set_context_command_handler(
                move |target: &crate::ui::sidebar::ContextTarget, action, relative| {
                    this_for_ctx.handle_workspace_context_command(target, action, relative);
                },
            );
        }
        // 树内拖拽移动:文件系统移动 + 已打开标签同步(path/标题/恢复快照)。
        {
            let this_for_move = this.clone();
            let root_for_move = this_for_move.workspace_root.clone();
            sidebar_for_handler.set_move_entry_handler(move |source_relative, target_dir| {
                let Some(root) = root_for_move.borrow().clone() else {
                    return;
                };
                let name = source_relative.rsplit('/').next().unwrap_or(source_relative);
                let new_relative = if target_dir.is_empty() {
                    name.to_string()
                } else {
                    format!("{target_dir}/{name}")
                };
                let old_abs = root.join(source_relative);
                match crate::workspace::mutation::rename_entry(&root, source_relative, &new_relative) {
                    Ok(_) => {
                        this_for_move.sidebar.set_workspace_root(Some(root.clone()));
                        let new_abs = root.join(&new_relative);
                        this_for_move.sync_tabs_after_rename(&old_abs, &new_abs);
                        this_for_move.set_status(&format!(
                            "已移动到 {}",
                            if target_dir.is_empty() { "工作区根".into() } else { target_dir.to_string() }
                        ));
                    }
                    Err(err) => this_for_move.set_status(&format!("移动失败:{err}")),
                }
            });
        }
        // 大纲双击 → 跳转。源码模式以标题文本为锚(scrollToSourceHeading,
        // 对齐 macOS/Windows/Android 契约):可视化大纲的 position 是
        // ProseMirror 偏移,比源码字符偏移小,直接当源码位置用会固定落在
        // 标题上方。可视化模式走 scrollToPosition。
        {
            let this_for_outline = this.clone();
            sidebar_for_handler.set_outline_handler(move |position, text| {
                let Some(state) = this_for_outline.active_tab() else {
                    return;
                };
                let document_id = state.document.borrow().document_id.clone();
                let command = if state.source_mode.get() {
                    "scrollToSourceHeading"
                } else {
                    "scrollToPosition"
                };
                let text_payload = if state.source_mode.get() {
                    format!("{position}\t{text}")
                } else {
                    position.to_string()
                };
                state.webview.post_to_editor(&HostMessage::command_with_request(
                    &document_id,
                    0,
                    serde_json::json!({ "command": command, "text": text_payload }),
                    None,
                ));
            });
        }
        this.wire_find_bar(&find_entry, &find_next, &find_prev, &find_close);
        this.install_actions(app, windows);
        this.wire_tab_view();
        this.wire_status_bar();
        this.install_command_action();
        this.apply_shortcuts(app);
        this.follow_dark_mode();
        this
    }

    // ---- 对外(app 层)接口 ----

    pub fn widget(self: &Rc<Self>) -> &adw::ApplicationWindow {
        &self.window
    }

    /// 显示窗口(app 层调用)。
    pub fn present(self: &Rc<Self>) {
        self.window.present();
    }

    pub fn can_accept_tabs(self: &Rc<Self>) -> bool {
        true
    }

    /// 会话恢复:选中指定序号的标签。
    pub fn select_tab(self: &Rc<Self>, index: usize) {
        let view = self.tabs.borrow();
        let pages = view.n_pages();
        if pages == 0 {
            return;
        }
        let page = view.nth_page((index.min(pages as usize - 1)) as i32);
        view.set_selected_page(&page);
    }

    /// 会话恢复:异步读取清单内各标签,全部完成后选中 active_tab(越界钳制)。
    /// 不复用 open_document_in_tab:恢复不应改写最近文件记录。
    pub fn restore_tabs(self: &Rc<Self>, paths: &[String], active_tab: usize) {
        let expected = paths.len();
        if expected == 0 {
            return;
        }
        let remaining = Rc::new(std::cell::Cell::new(expected));
        let active = Rc::new(std::cell::Cell::new(active_tab));
        for path_str in paths {
            let path = PathBuf::from(path_str);
            let this = self.clone();
            let remaining = remaining.clone();
            let active = active.clone();
            let path_for_task = path.clone();
            glib::spawn_future_local(async move {
                match gio::spawn_blocking(move || read_document(&path_for_task)).await {
                    Ok(Ok(snapshot)) => {
                        this.create_tab_with_snapshot(path.clone(), snapshot, None);
                    }
                    Ok(Err(err)) => {
                        log::warn!("[restore] 无法读取 {}: {err}", path.display());
                    }
                    Err(_) => {
                        log::warn!("[restore] 读取任务 panic:{}", path.display());
                    }
                }
                let left = remaining.get() - 1;
                remaining.set(left);
                if left == 0 {
                    this.select_tab(active.get());
                }
            });
        }
    }

    /// 根窗口(对话框 transient 用)。
    pub fn gtk_window(&self) -> &adw::ApplicationWindow {
        &self.window
    }

    pub fn tab_paths(self: &Rc<Self>) -> Vec<String> {
        self.tab_states
            .borrow()
            .iter()
            .filter_map(|state| state.document.borrow().path.as_ref().map(|p| p.to_string_lossy().into_owned()))
            .collect()
    }

    pub fn active_tab_index(self: &Rc<Self>) -> usize {
        let view = self.tabs.borrow();
        let Some(selected) = view.selected_page() else {
            return 0;
        };
        for index in 0..view.n_pages() {
            if view.nth_page(index) == selected {
                return index as usize;
            }
        }
        0
    }

    pub fn open_document_in_tab(self: &Rc<Self>, path: &Path) {
        // 记录最近文件(设置持久化;菜单下次打开时刷新)。
        {
            let mut settings = self.settings.borrow_mut();
            let path_str = path.display().to_string();
            let name = path
                .file_name()
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| path_str.clone());
            settings.remember_recent_file(&path_str, &name);
            crate::app::persist_settings(&settings);
        }

        let path = path.to_path_buf();
        // 文件读取与编码探测移出主线程(审计 Important #3);结果回主循环建标签。
        let this = self.clone();
        let path_for_task = path.to_path_buf();
        let path_for_msg = path.to_path_buf();
        glib::spawn_future_local(async move {
            // JoinHandle::Output = Result<T, Panic>;双层解包(任务 panic 视为读失败)。
            match gio::spawn_blocking(move || read_document(&path_for_task)).await {
                Ok(Ok(snapshot)) => {
                    let page = this.create_tab_with_snapshot(path, snapshot, None);
                    this.tabs.borrow().set_selected_page(&page);
                }
                Ok(Err(err)) => {
                    log::warn!("[window] 无法读取 {}: {err}", path_for_msg.display());
                    this.set_status(&format!("无法读取 {}: {err}", path_for_msg.display()));
                }
                Err(_) => {
                    log::warn!("[window] 读取任务 panic:{}", path_for_msg.display());
                    this.set_status(&format!("无法读取 {}", path_for_msg.display()));
                }
            }
        });
    }

    /// 以恢复内容打开:文件存在则以恢复 markdown 载入并置脏(保留快照至下次保存)。
    pub fn open_document_recovered(self: &Rc<Self>, path: Option<&Path>, markdown: String) {
        let Some(path) = path else {
            let page = self.create_tab_inner(
                None,
                DocumentSnapshot::default(),
                Some(markdown),
                "markdown",
            );
            self.tabs.borrow().set_selected_page(&page);
            return;
        };
        let path = path.to_path_buf();
        let path_for_task = path.clone();
        let this = self.clone();
        glib::spawn_future_local(async move {
            let recovered = markdown.clone();
            let snapshot =
                match gio::spawn_blocking(move || read_document(&path_for_task)).await {
                    Ok(Ok(snapshot)) => snapshot,
                    _ => DocumentSnapshot::default(),
                };
            this.create_tab_with_snapshot(path, snapshot, Some(recovered));
        });
    }

    fn create_tab_with_snapshot(
        self: &Rc<Self>,
        path: PathBuf,
        snapshot: DocumentSnapshot,
        recovered_markdown: Option<String>,
    ) -> adw::TabPage {
        self.create_tab_inner(Some(path), snapshot, recovered_markdown, "markdown")
    }

    fn create_tab_blank_with_type(self: &Rc<Self>, document_type: &str) {
        let page = self.create_tab_inner(None, DocumentSnapshot::default(), None, document_type);
        self.tabs.borrow().set_selected_page(&page);
    }

    fn create_tab_inner(
        self: &Rc<Self>,
        path: Option<PathBuf>,
        snapshot: DocumentSnapshot,
        recovered_markdown: Option<String>,
        document_type: &str,
    ) -> adw::TabPage {
        let detection = detect(&snapshot.bytes);
        let decision = EncodingDecision::from_detection(&detection);
        let newline = detect_newline(&detection.decoded);
        let document_id = glib::uuid_string_random().to_string();

        let webview = EditorWebView::new(&self.context);
        // Keep an opaque native surface over the WebView until the editor has
        // loaded the document. The view still receives its viewport while loading.
        let content = gtk::Overlay::new();
        content.set_child(Some(webview.widget()));
        let startup_cover = gtk::Box::new(gtk::Orientation::Vertical, 0);
        startup_cover.add_css_class("view");
        content.add_overlay(&startup_cover);
        #[allow(deprecated)] // Named GTK colors are available on the supported GTK versions.
        if let Some(background) = self.window.style_context().lookup_color("view_bg_color") {
            webview.widget().set_background_color(&background);
        }
        let page = {
            let view = self.tabs.borrow();
            view.append(&content)
        };
        let tab_name = path
            .as_deref()
            .and_then(Path::file_name)
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "未命名".into());
        page.set_title(tab_name.as_str());

        let session = Rc::new(RefCell::new(EditorSessionState::new()));
        session.borrow_mut().start_document(&document_id);

        let state = Rc::new(TabState {
            page,
            webview: webview.clone(),
            startup_cover,
            session,
            document: RefCell::new(TabDocument {
                path: path.clone(),
                disk: snapshot,
                encoding: decision,
                newline,
                document_id: document_id.clone(),
                document_type: document_type.to_string(),
                dirty: recovered_markdown.is_some(),
                pending_save: false,
                recovered_markdown: recovered_markdown.clone(),
            }),
            recovery_source: RefCell::new(None),
            zoom: std::cell::Cell::new(1.0),
            source_mode: std::cell::Cell::new(false),
            frontend_ready_id: RefCell::new(None),
            last_editor_status: RefCell::new(None),
            close_after_save: std::cell::Cell::new(false),
            read_only: std::cell::Cell::new(false),
        });
        self.tab_states.borrow_mut().push(state.clone());
        log::debug!("[create_tab] document_id={document_id} path={path:?}");

        {
            let this = self.clone();
            let state = state.clone();
            webview.connect_message(move |message| this.handle_editor_message(&state, message));
        }

        self.send_after_ready(&state);

        webview.load_editor_page();
        if recovered_markdown.is_none() {
            if let Some(path) = &path {
                self.set_status(&format!("已打开 {}", path.display()));
            }
        } else {
            // 恢复载入:载入恢复内容并置脏(载入 payload 在 stylesApplied 回调读取)。
            state
                .document
                .borrow_mut()
                .recovered_markdown = recovered_markdown;
        }
        self.update_tab_title(&state);
        self.refresh_status_bar_content(&state);
        // 打开文档时在工作区树中定位选中(对齐 macOS revealWorkspacePath)。
        let path_for_reveal = state.document.borrow().path.clone();
        if let Some(path) = path_for_reveal.as_deref() {
            self.sidebar.reveal_workspace_path(path);
        }
        state.page.clone()
    }

    // ---- 内部 ----

    fn set_status(self: &Rc<Self>, text: &str) {
        let label = self.status_text.borrow();
        let (visible, mode) = {
            let bar = &self.settings.borrow().status_bar;
            (
                bar.command_status_visible,
                bar.command_display_mode,
            )
        };
        label.set_visible(visible && mode != StatusBarCommandDisplayMode::Hidden);
        if !visible || mode == StatusBarCommandDisplayMode::Hidden {
            return;
        }
        label.set_text(text);
        if mode == StatusBarCommandDisplayMode::Temporary {
            // 临时显示:3 秒后清空;新消息重置计时。
            if let Some(old) = self.status_timeout.borrow_mut().take() {
                old.remove();
            }
            let label = label.clone();
            let timeout = glib::timeout_add_local_once(
                std::time::Duration::from_millis(3_000),
                move || label.set_text(""),
            );
            *self.status_timeout.borrow_mut() = Some(timeout);
        }
    }

    fn send_after_ready(self: &Rc<Self>, state: &Rc<TabState>) {
        // 与冒烟同构:ready → applyStyles → loadDocument。
        // ready 回调经 TabState 的共享 RefCell 通道注册(闭包捕获 state 克隆,无借用逃逸)。
        // ready 携带页面自生成的占位 documentId;对齐 Android:宿主采纳占位 id
        // 重绑会话,applyStyles 用占位 id 发(页面回执才匹配;loadDocument 载荷
        // 使页面切换到宿主 id,documentLoaded 起两边一致)。
        let pending: Rc<RefCell<bool>> = Rc::new(RefCell::new(true));
        let webview = state.webview.clone();
        let state_for_ready = state.clone();
        let pending_for_ready = pending.clone();
        state.webview.connect_on_message_type(EditorMessageType::Ready, move || {
            let mut flag = pending_for_ready.borrow_mut();
            if !*flag {
                return;
            }
            *flag = false;
            drop(flag);
            let frontend_id = state_for_ready
                .frontend_ready_id
                .borrow()
                .clone()
                .unwrap_or_else(|| state_for_ready.document.borrow().document_id.clone());
            {
                let mut session = state_for_ready.session.borrow_mut();
                session.start_document(&frontend_id);
            }
            state_for_ready.document.borrow_mut().document_id = frontend_id.clone();
            log::debug!("[create_tab] 采纳页面占位 document_id={frontend_id}");
            let apply = HostMessage::apply_styles(
                &frontend_id,
                serde_json::json!({ "frontendFormatMenu": false }),
            );
            webview.post_to_editor(&apply);
        });
        // stylesApplied → loadDocument(时序链第二拍)。
        let webview = state.webview.clone();
        let state_for_styles = state.clone();
        let pending_styles: Rc<RefCell<bool>> = Rc::new(RefCell::new(true));
        let pending_styles_for_cb = pending_styles.clone();
        state.webview.connect_on_message_type(EditorMessageType::StylesApplied, move || {
            let mut flag = pending_styles_for_cb.borrow_mut();
            if !*flag {
                return;
            }
            *flag = false;
            drop(flag);
            let document = state_for_styles.document.borrow();
            let markdown = document
                .recovered_markdown
                .clone()
                .unwrap_or_else(|| detect(&document.disk.bytes).decoded);
            let initial_dirty = document.recovered_markdown.is_some();
            let document_type = document.document_type.clone();
            let payload = serde_json::json!({
                "markdown": markdown,
                "documentType": document_type,
                "readOnly": false,
                "initialDirty": initial_dirty,
                "scrollTop": 0,
                "restoreViewState": true,
            });
            let load = HostMessage::load_document(&document.document_id, payload);
            drop(document);
            webview.post_to_editor(&load);
        });
        let _ = pending_styles;
        // localizeFindBar:载荷键对齐 Windows EditorHostController.SendFindBarLocalization。
        {
            let webview = state.webview.clone();
            let lang = self.lang.clone();
            let state_for_loc = state.clone();
            let pending_loc: Rc<RefCell<bool>> = Rc::new(RefCell::new(true));
            let pending_loc_cb = pending_loc.clone();
            state.webview.connect_on_message_type(EditorMessageType::Ready, move || {
                let mut flag = pending_loc_cb.borrow_mut();
                if !*flag {
                    return;
                }
                *flag = false;
                drop(flag);
                let key = |k: &str| crate::l10n::tr(&lang, k).unwrap_or_else(|| k.to_string());
                let localize = HostMessage {
                    protocol_version: 1,
                    message_type: crate::bridge::messages::HostMessageType::LocalizeFindBar,
                    // 页面入站校验要求 documentId 非空(对齐 macOS:带会话 id)。
                    document_id: state_for_loc.document.borrow().document_id.clone(),
                    revision: 0,
                    request_id: None,
                    payload: Some(serde_json::json!({
                        "alertNote": key("备注"),
                        "alertTip": key("提示"),
                        "alertImportant": key("重要"),
                        "alertWarning": key("警告"),
                        "alertCaution": key("注意"),
                        "formatPromoteHeading": key("升为标题"),
                        "formatDemoteHeading": key("降为标题"),
                    })),
                };
                webview.post_to_editor(&localize);
            });
        }
    }

    fn handle_editor_message(self: &Rc<Self>, state: &Rc<TabState>, message: EditorMessage) {
        // M2 会话纪律接入运行时:ready 直通(握手前 documentId 尚未绑定),
        // 其余消息过门控;拒绝时 log::warn 不静默(Plan Global Constraint)。
        // ready 与空 documentId 的消息(文档加载前的前端 UI 初始化)直通,
        // 与桌面端行为一致;其余消息过会话门控。
        if message.message_type == EditorMessageType::Ready {
            // 记录页面占位 id(send_after_ready 的 ready 回调采纳,对齐 Android)。
            if !message.document_id.is_empty() {
                *state.frontend_ready_id.borrow_mut() = Some(message.document_id.clone());
            }
        } else if !message.document_id.is_empty() {
            let decision = state.session.borrow().accept(&message);
            match decision {
                crate::bridge::session_state::AcceptDecision::Accept => {
                    state.session.borrow_mut().confirm(&message);
                }
                other => {
                    log::warn!(
                        "[bridge] 拒绝消息 type={} doc={} rev={}: {other:?} (会话 doc={})",
                        message.message_type_name(),
                        message.document_id,
                        message.revision,
                        state.session.borrow().document_id()
                    );
                    return;
                }
            }
        }
        self.dispatch_editor_message(state, message);
    }

    fn dispatch_editor_message(self: &Rc<Self>, state: &Rc<TabState>, message: EditorMessage) {
        match message.message_type {
            EditorMessageType::Ready => { /* ready 握手由 send_after_ready 的类型化回调处理 */ }
            EditorMessageType::StylesApplied => {
                // loadDocument 在 ready 闭包链里发送;此处确认样式落地。
            }
            EditorMessageType::DocumentLoaded => {
                let document_id = message.document_id.clone();
                if document_id == state.document.borrow().document_id {
                    state.startup_cover.set_visible(false);
                    self.set_status(&format!(
                        "已加载 {}",
                        state
                            .document
                            .borrow()
                            .path
                            .as_ref()
                            .map(|p| p.display().to_string())
                            .unwrap_or_default()
                    ));
                }
            }
            EditorMessageType::DirtyChanged => {
                let dirty = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("dirty"))
                    .and_then(|v| v.as_bool())
                    .unwrap_or(false);
                state.document.borrow_mut().dirty = dirty;
                self.schedule_recovery(state, dirty);
                self.update_tab_title(state);
            }
            EditorMessageType::RequestSave => self.save_tab(state),
            EditorMessageType::Snapshot => self.handle_snapshot(state, &message),
            EditorMessageType::ContextMenuRequested => self.show_context_menu(state, &message),
            EditorMessageType::BlockMenuRequested => self.show_context_menu(state, &message),
            EditorMessageType::CommandStateChanged => {
                if let Some(source_mode) = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("sourceMode"))
                    .and_then(|v| v.as_bool())
                {
                    if state.source_mode.replace(source_mode) != source_mode {
                        self.refresh_status_bar_content(state);
                    }
                }
            }
            EditorMessageType::OutlineChanged => {
                // 内核发 {headings:[...]};兼容旧键 items。
                let items = message
                    .payload
                    .as_ref()
                    .and_then(|payload| {
                        payload
                            .get("headings")
                            .or_else(|| payload.get("items"))
                    })
                    .and_then(|v| v.as_array())
                    .cloned()
                    .unwrap_or_default();
                self.sidebar.set_outline(&items);
            }
            EditorMessageType::EditorStatusChanged => self.update_status_from_editor(state, &message),
            EditorMessageType::ExportContent => {
                if message.request_id.as_deref() != Some("export-html") {
                    return;
                }
                let Some(html) = message.payload.as_ref().and_then(|p| p.get("html")).and_then(|v| v.as_str()).map(str::to_string) else {
                    self.set_status("导出失败:内核未返回 HTML");
                    return;
                };
                let dialog = gtk::FileDialog::new();
                let initial = state
                    .document
                    .borrow()
                    .path
                    .as_ref()
                    .and_then(|p| p.file_stem().map(|n| format!("{}.html", n.to_string_lossy())))
                    .unwrap_or_else(|| "export.html".into());
                dialog.set_initial_name(Some(&initial));
                dialog.save(Some(&self.window), None::<&gio::Cancellable>, move |result| {
                    if let Ok(file) = result {
                        if let Some(path) = file.path() {
                            match std::fs::write(&path, html) {
                                Ok(()) => println!("[export] HTML 已保存 {}", path.display()),
                                Err(err) => eprintln!("[export] 写入失败 {err}"),
                            }
                        }
                    }
                });
            }
            EditorMessageType::OpenLink => {
                if let Some(url) = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("url"))
                    .and_then(|v| v.as_str())
                {
                    self.open_link(url);
                }
            }
            EditorMessageType::ZoomWheel => {
                let delta = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("deltaY"))
                    .and_then(|v| v.as_f64())
                    .unwrap_or(0.0);
                self.adjust_zoom(state, delta);
            }
            EditorMessageType::Error => {
                let text = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("message"))
                    .and_then(|v| v.as_str())
                    .unwrap_or("未知错误")
                    .to_string();
                self.set_status(&format!("错误:{text}"));
            }
            // outlineSelectionChanged:光标所在标题位置,侧栏大纲就近选中
            // (对齐 macOS outlineSelectionChanged)。
            EditorMessageType::OutlineSelectionChanged => {
                if let Some(position) = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("position"))
                    .and_then(|v| v.as_i64())
                {
                    self.sidebar.highlight_outline_position(position);
                }
            }
            _ => {}
        }
    }

    fn schedule_recovery(self: &Rc<Self>, state: &Rc<TabState>, dirty: bool) {
        if !dirty {
            return;
        }
        if state.recovery_source.borrow().is_some() {
            return;
        }
        let this = self.clone();
        let state_for_timer = state.clone();
        let source = glib::timeout_add_local(std::time::Duration::from_millis(RECOVERY_DEBOUNCE_MS), move || {
            *state_for_timer.recovery_source.borrow_mut() = None;
            this.write_recovery_snapshot(&state_for_timer);
            glib::ControlFlow::Break
        });
        *state.recovery_source.borrow_mut() = Some(source);
    }

    fn write_recovery_snapshot(self: &Rc<Self>, state: &Rc<TabState>) {
        let document = state.document.borrow();
        if !document.dirty {
            return;
        }
        // 内容:发起快照请求,回包在 handle_snapshot 的 recovery 分支落盘。
        drop(document);
        state
            .webview
            .post_to_editor(&crate::bridge::messages::HostMessage::request_snapshot(
                &state.document.borrow().document_id,
                0,
                "recovery",
            ));
    }

    fn handle_snapshot(self: &Rc<Self>, state: &Rc<TabState>, message: &EditorMessage) {
        let Some(markdown) = message
            .payload
            .as_ref()
            .and_then(|payload| payload.get("markdown"))
            .and_then(|v| v.as_str())
            .map(str::to_string)
        else {
            return;
        };
        let request_id = message.request_id.as_deref().unwrap_or("");
        // 分享:requestId="share" → 全文写系统剪贴板(见 share_active_document)。
        if request_id == "copy-contents" {
            if let Some(display) = gtk::gdk::Display::default() {
                display.clipboard().set_text(&markdown);
            }
            self.set_status("文件内容已复制");
            return;
        }
        if request_id == "share" {
            if let Some(display) = gtk::gdk::Display::default() {
                display.clipboard().set_text(&markdown);
            }
            self.set_status("已复制文件内容");
            return;
        }
        let document = state.document.borrow();
        let document_id = document.document_id.clone();
        let encoding = document.encoding.clone();
        let newline = document.newline.clone();
        let path = document.path.clone();
        let disk_snapshot = document.disk.clone();
        let pending_save = document.pending_save;
        drop(document);

        if request_id == "recovery" {
            // 恢复快照:原子写 .recovery.json;竞态复查——回包落地前若已保存
            // (dirty=false),不得为已保存文档复活恢复文件(M3 审计 Minor)。
            if !state.document.borrow().dirty {
                return;
            }
            let (_config, state_dir) = crate::app::storage_dirs();
            let entry = RecoveryEntry {
                document_id,
                path: path.map(|p| p.to_string_lossy().into_owned()),
                markdown,
                revision: message.revision.to_string(),
                saved_at: glib::DateTime::now_utc().map(|d| d.to_unix()).unwrap_or(0),
            };
            let _ = save_recovery(&state_dir.join("recovery"), &entry);
            return;
        }

        if !pending_save || request_id != "save" {
            return;
        }
        if message.document_id != document_id {
            log::warn!("[save] 快照文档 id 不匹配:{}", message.document_id);
            return;
        }
        // 保存会话:按原编码与换行回写。
        let normalized = apply_newline(&markdown, newline);
        let bytes = encode(&normalized, encoding.encoding, encoding.bom);
        let save_target = path.clone();
        let result = match &save_target {
            Some(path) => write_document(path, &bytes, &disk_snapshot).map(|_| path.clone()),
            None => Err(WriteError::Io(std::io::Error::other("无目标路径"))),
        };
        state.document.borrow_mut().pending_save = false;
        let saved = result.is_ok();
        match result {
            Ok(path) => {
                // 更新磁盘基线,回 markSaved,清恢复快照。
                if let Ok(fresh) = read_document(&path) {
                    state.document.borrow_mut().disk = fresh;
                }
                state.document.borrow_mut().dirty = false;
                state
                    .webview
                    .post_to_editor(&HostMessage::mark_saved(&document_id, message.revision));
                let (_config, state_dir) = crate::app::storage_dirs();
                discard_recovery(&state_dir.join("recovery"), &document_id);
                self.set_status(&format!("已保存 {}", path.display()));
                self.update_tab_title(state);
            }
            Err(WriteError::Conflict) => {
                self.set_status("保存被拒绝:文件已被外部修改(冲突)");
            }
            Err(WriteError::Io(err)) => {
                self.set_status(&format!("保存失败:{err}"));
            }
        }
        if state.close_after_save.replace(false) {
            state.webview.widget().set_sensitive(true);
            if saved {
                self.tabs.borrow().close_page(&state.page);
            }
        }
    }

    fn save_tab(self: &Rc<Self>, state: &Rc<TabState>) {
        let needs_path = state.document.borrow().path.is_none();
        if needs_path {
            self.run_save_as_dialog(state);
            return;
        }
        state.document.borrow_mut().pending_save = true;
        let document_id = state.document.borrow().document_id.clone();
        state
            .webview
            .post_to_editor(&HostMessage::request_snapshot(&document_id, 0, "save"));
    }

    fn run_save_as_dialog(self: &Rc<Self>, state: &Rc<TabState>) {
        let dialog = gtk::FileDialog::new();
        let initial_name = if state.document.borrow().document_type == "plainText" {
            "未命名.txt"
        } else {
            "未命名.md"
        };
        dialog.set_initial_name(Some(initial_name));
        let this = self.clone();
        let state = state.clone();
        dialog.save(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            let Some(path) = result.ok().and_then(|file| file.path()) else {
                if state.close_after_save.replace(false) {
                    state.webview.widget().set_sensitive(true);
                }
                return;
            };
            // 另存:切换目标路径后走保存闭环。
            {
                let mut document = state.document.borrow_mut();
                document.path = Some(path);
                document.disk = DocumentSnapshot::default();
            }
            this.save_tab(&state);
        });
    }

    fn run_open_dialog(self: &Rc<Self>) {
        let dialog = gtk::FileDialog::new();
        let this = self.clone();
        dialog.open(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            if let Ok(file) = result {
                if let Some(path) = file.path() {
                    this.open_document_in_tab(&path);
                }
            }
        });
    }

    fn dismiss_context_menu(&self) {
        if let Some(popover) = self.context_popover.take() {
            popover.popdown();
        }
    }

    fn show_context_menu(self: &Rc<Self>, state: &Rc<TabState>, message: &EditorMessage) {
        let (x, y) = (
            message.payload.as_ref().and_then(|p| p.get("clientX")).and_then(|v| v.as_f64()).unwrap_or(0.0),
            message.payload.as_ref().and_then(|p| p.get("clientY")).and_then(|v| v.as_f64()).unwrap_or(0.0),
        );
        let read_only = message
            .payload
            .as_ref()
            .and_then(|p| p.get("readOnly"))
            .and_then(|v| v.as_bool())
            .unwrap_or(false);

        // 对象上下文:最近一次 commandStateChanged 缓存(对齐 macOS editorMenuState)。
        let context = state.last_editor_status.borrow().clone();
        let flag = |key: &str| -> bool {
            context
                .as_ref()
                .and_then(|p| p.get(key))
                .and_then(|v| v.as_bool())
                .unwrap_or(false)
        };
        let semantic = context
            .as_ref()
            .and_then(|p| p.get("semanticContext"))
            .and_then(|v| v.as_str())
            .unwrap_or("ordinaryBlock")
            .to_string();
        let link_href = context
            .as_ref()
            .and_then(|p| p.get("linkHref"))
            .and_then(|v| v.as_str())
            .map(str::to_string);
        let footnote_label = context
            .as_ref()
            .and_then(|p| p.get("footnoteReferenceLabel"))
            .and_then(|v| v.as_str())
            .map(str::to_string);
        let math_block = flag("mathBlock");
        let actions = context.as_ref().and_then(|p| p.get("actions")).cloned();

        // WebView 坐标 → 窗口坐标(考虑缩放)。
        let zoom = state.zoom.get();
        let origin_point = graphene::Point::new((x * zoom) as f32, (y * zoom) as f32);
        let (origin_x, origin_y) = state
            .webview
            .widget()
            .compute_point(&self.window, &origin_point)
            .map(|point| (point.x() as i32 as f64, point.y() as i32 as f64))
            .unwrap_or((0.0, 0.0));
        let rect = gdk::Rectangle::new(origin_x as i32, origin_y as i32, 1, 1);

        // 菜单构建器:条目启用态读 actions 表(灰化对齐桌面)。
        let menu_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        menu_box.set_margin_top(6);
        menu_box.set_margin_bottom(6);
        menu_box.set_margin_start(4);
        menu_box.set_margin_end(4);
        let command_enabled = |command: &str| -> bool {
            actions
                .as_ref()
                .and_then(|a| a.get(command))
                .and_then(|c| c.get("enabled"))
                .and_then(|v| v.as_bool())
                .unwrap_or(true)
        };
        let add_item = |menu_box: &gtk::Box, label: &str, command: &str, this: &Rc<Self>, state: &Rc<TabState>| {
            let enabled = command_enabled(command);
            let item = gtk::Button::builder()
                .label(label)
                .has_frame(false)
                .halign(gtk::Align::Fill)
                .sensitive(enabled)
                .build();
            let popover_weak = menu_box.downgrade();
            let this = this.clone();
            let state = state.clone();
            let command = command.to_string();
            item.connect_clicked(move |_| {
                if let Some(menu_box) = popover_weak.upgrade() {
                    if let Some(popover) = menu_box.parent().and_then(|p| p.downcast::<gtk::Popover>().ok()) {
                        popover.popdown();
                    }
                }
                this.dispatch_command(&command);
                let _ = &state;
            });
            menu_box.append(&item);
        };
        let add_clip_item = |menu_box: &gtk::Box, label: &str, command: &str, this: &Rc<Self>, state: &Rc<TabState>| {
            let item = gtk::Button::builder()
                .label(label)
                .has_frame(false)
                .halign(gtk::Align::Fill)
                .build();
            let popover_weak = menu_box.downgrade();
            let this = this.clone();
            let state = state.clone();
            let command = command.to_string();
            item.connect_clicked(move |_| {
                if let Some(menu_box) = popover_weak.upgrade() {
                    if let Some(popover) = menu_box.parent().and_then(|p| p.downcast::<gtk::Popover>().ok()) {
                        popover.popdown();
                    }
                }
                this.exec_editing_command(&command);
            });
            menu_box.append(&item);
        };
        let add_separator = |menu_box: &gtk::Box| {
            menu_box.append(&gtk::Separator::new(gtk::Orientation::Horizontal));
        };
        // 子菜单:MenuButton(标签)指向子 Box;返回子 Box 供继续填充。
        let add_submenu = |menu_box: &gtk::Box, label: &str| -> gtk::Box {
            let sub_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
            sub_box.set_margin_top(6);
            sub_box.set_margin_bottom(6);
            sub_box.set_margin_start(4);
            sub_box.set_margin_end(4);
            let button = gtk::MenuButton::builder()
                .label(label)
                .has_frame(false)
                .halign(gtk::Align::Fill)
                .build();
            button.set_popover(Some(&Self::popover_from_box(&sub_box)));
            menu_box.append(&button);
            sub_box
        };

        let mut used_ordinary_layout = false;

        // ---- 语义分区(对齐 macOS buildEditorContextMenu) ----
        if read_only && semantic == "math" {
            add_item(&menu_box, "查看公式源码", "viewMathSource", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
        } else if semantic == "footnoteDefinition" {
            add_item(&menu_box, "转到引用", "goToFootnoteReference", &self, state);
            add_item(&menu_box, "更改脚注标识…", "resetFootnoteLabel", &self, state);
            add_item(&menu_box, "删除全部引用", "clearFootnoteReferences", &self, state);
            add_item(&menu_box, "删除脚注及全部引用", "deleteFootnote", &self, state);
            add_separator(&menu_box);
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
        } else if semantic == "frontMatter" {
            add_item(&menu_box, "复制元数据", "copyCodeBlock", &self, state);
            if !read_only {
                add_separator(&menu_box);
                add_item(&menu_box, "移到元数据之后", "exitCode", &self, state);
            }
            add_separator(&menu_box);
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
            add_clip_item(&menu_box, "全选", "selectAll", &self, state);
        } else if semantic == "table" {
            add_item(&menu_box, "编辑表格标题", "editTableCaption", &self, state);
            add_separator(&menu_box);
            add_item(&menu_box, "在上方添加行", "addRowBefore", &self, state);
            add_item(&menu_box, "在下方添加行", "addRowAfter", &self, state);
            add_item(&menu_box, "删除当前行", "deleteRow", &self, state);
            add_separator(&menu_box);
            add_item(&menu_box, "在左侧添加列", "addColumnBefore", &self, state);
            add_item(&menu_box, "在右侧添加列", "addColumnAfter", &self, state);
            add_item(&menu_box, "删除当前列", "deleteColumn", &self, state);
            add_separator(&menu_box);
            let widths = add_submenu(&menu_box, "调整列宽");
            add_item(&widths, "根据内容调整列宽", "fitTableContents", &self, state);
            add_item(&widths, "平均分配列宽", "distributeTableColumns", &self, state);
            add_item(&widths, "恢复自动列宽", "resetTableWidths", &self, state);
            let align = add_submenu(&menu_box, "对齐");
            add_item(&align, "左对齐", "alignTableLeft", &self, state);
            add_item(&align, "居中对齐", "alignTableCenter", &self, state);
            add_item(&align, "右对齐", "alignTableRight", &self, state);
            add_separator(&menu_box);
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
            add_separator(&menu_box);
            add_item(&menu_box, "删除表格", "deleteTable", &self, state);
        } else if semantic == "mermaid" {
            if read_only {
                add_item(&menu_box, "查看图表源码", "viewMermaidSource", &self, state);
                add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            } else {
                add_item(&menu_box, "编辑 Mermaid 源码", "editMermaid", &self, state);
                add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            }
            add_item(&menu_box, "重新渲染 Mermaid 图表", "rerenderMermaid", &self, state);
            add_item(&menu_box, "将图表另存为图片", "saveMermaidAs", &self, state);
            if !read_only {
                add_separator(&menu_box);
                add_item(&menu_box, "删除 Mermaid 图表", "deleteMermaid", &self, state);
            }
        } else if semantic == "image" {
            // 顺序对齐 Windows ImageContextMenu:更换/标题/缩放/旋转/另存为 + 剪贴板。
            add_item(&menu_box, "更换图片…", "changeImage", &self, state);
            add_item(&menu_box, "编辑图片标题", "editImageCaption", &self, state);
            let resize = add_submenu(&menu_box, "调整图片大小");
            add_item(&resize, "50%", "resizeImage50", &self, state);
            add_item(&resize, "75%", "resizeImage75", &self, state);
            add_item(&resize, "90%", "resizeImage90", &self, state);
            add_item(&resize, "100%", "resizeImage100", &self, state);
            add_item(&menu_box, "顺时针旋转图片", "rotateImage", &self, state);
            add_item(&menu_box, "将图片另存为…", "saveImageAs", &self, state);
            add_separator(&menu_box);
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
        } else if semantic == "math" {
            // 公式:编辑/编号(块级)/互转/删除。
            add_item(&menu_box, "编辑公式源码", "editMath", &self, state);
            if math_block {
                add_item(&menu_box, "公式编号…", "setMathNumber", &self, state);
            }
            add_item(
                &menu_box,
                if math_block { "转为行内公式" } else { "转为块级公式" },
                "convertMath",
                &self,
                state,
            );
            add_separator(&menu_box);
            add_item(&menu_box, "删除公式", "deleteMath", &self, state);
        } else if semantic == "codeBlock" {
            // 代码块:[撤销/重做] 声明代码语言 格式化 复制整段代码 | 转为正文 | 剪贴板/全选。
            if !read_only {
                add_item(&menu_box, "撤销", "undo", &self, state);
                add_item(&menu_box, "重做", "redo", &self, state);
                add_separator(&menu_box);
            }
            add_item(&menu_box, "声明代码语言…", "declareCodeLanguage", &self, state);
            let format_label = if command_enabled("copy") {
                "格式化选中行"
            } else {
                "格式化代码块"
            };
            add_item(&menu_box, format_label, "formatCodeBlock", &self, state);
            add_item(&menu_box, "复制整段代码", "copyCodeBlock", &self, state);
            add_separator(&menu_box);
            if !read_only {
                add_item(&menu_box, "转为正文", "exitCode", &self, state);
                add_separator(&menu_box);
            }
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
            add_clip_item(&menu_box, "全选", "selectAll", &self, state);
        } else if read_only {
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
        } else {
            used_ordinary_layout = true;
            // 普通段落(对齐 macOS 默认分支):整理行内代码/历史/剪贴板/插入/段落。
            if command_enabled("normalizeInlineCode") {
                add_item(&menu_box, "整理行内代码", "normalizeInlineCode", &self, state);
                add_separator(&menu_box);
            }
            add_item(&menu_box, "撤销", "undo", &self, state);
            add_item(&menu_box, "重做", "redo", &self, state);
            add_separator(&menu_box);
            add_clip_item(&menu_box, "剪切", "cut", &self, state);
            add_clip_item(&menu_box, "拷贝", "copy", &self, state);
            add_clip_item(&menu_box, "粘贴", "paste", &self, state);
            add_clip_item(&menu_box, "粘贴为纯文本", "pastePlainText", &self, state);
            add_separator(&menu_box);
            add_item(&menu_box, "行内代码", "toggleCode", &self, state);
            add_item(&menu_box, "行内公式", "insertMathInline", &self, state);
            add_item(&menu_box, "插入超链接…", "insertLink", &self, state);
            let images = add_submenu(&menu_box, "插入图片");
            add_item(&images, "插入本地图片…", "insertImage", &self, state);
            let paragraph = add_submenu(&menu_box, "段落");
            add_item(&paragraph, "正文", "setParagraph", &self, state);
            let headings = add_submenu(&paragraph, "标题");
            add_item(&headings, "一级标题", "setHeading1", &self, state);
            add_item(&headings, "二级标题", "setHeading2", &self, state);
            add_item(&headings, "三级标题", "setHeading3", &self, state);
            add_item(&headings, "四级标题", "setHeading4", &self, state);
            add_item(&headings, "五级标题", "setHeading5", &self, state);
            add_item(&headings, "六级标题", "setHeading6", &self, state);
            add_item(&paragraph, "引用", "toggleBlockquote", &self, state);
            add_item(&paragraph, "块级公式", "insertMathBlock", &self, state);
            add_item(&paragraph, "代码块", "toggleCodeBlock", &self, state);
            add_item(&paragraph, "水平线", "insertHorizontalRule", &self, state);
            let alerts = add_submenu(&paragraph, "提示框");
            add_item(&alerts, "备注", "insertAlertNote", &self, state);
            add_item(&alerts, "提示", "insertAlertTip", &self, state);
            add_item(&alerts, "重要", "insertAlertImportant", &self, state);
            add_item(&alerts, "警告", "insertAlertWarning", &self, state);
            add_item(&alerts, "注意", "insertAlertCaution", &self, state);
            let tables = add_submenu(&paragraph, "表格");
            add_item(&tables, "插入 3×3 表格", "insertTable", &self, state);
            let lists = add_submenu(&paragraph, "列表");
            add_item(&lists, "无序列表", "toggleBulletList", &self, state);
            add_item(&lists, "有序列表", "toggleOrderedList", &self, state);
            add_item(&lists, "任务列表", "toggleTaskList", &self, state);
            add_separator(&lists);
            add_item(&lists, "增加列表缩进", "indentListItem", &self, state);
            add_item(&lists, "减少列表缩进", "outdentListItem", &self, state);
            add_item(&paragraph, "插入脚注", "insertFootnote", &self, state);
            add_separator(&paragraph);
            add_item(&paragraph, "段前插入行", "insertLineBefore", &self, state);
            add_item(&paragraph, "段后插入行", "insertLineAfter", &self, state);
            add_separator(&paragraph);
            add_item(&paragraph, "重复该段", "duplicateParagraph", &self, state);
            add_item(&paragraph, "删除该段", "deleteParagraph", &self, state);
        }

        // ---- 尾区(对齐 macOS):链接/脚注引用/取消提示块 ----
        if link_href.is_some() {
            add_separator(&menu_box);
            add_item(&menu_box, "打开链接", "openLink", &self, state);
            add_item(&menu_box, "复制链接地址", "copyLinkAddress", &self, state);
            if !read_only {
                add_item(&menu_box, "编辑链接…", "insertLink", &self, state);
                add_item(&menu_box, "移除链接", "removeLink", &self, state);
            }
        }
        if footnote_label.is_some() {
            add_separator(&menu_box);
            add_item(&menu_box, "转到脚注正文", "goToFootnoteDefinition", &self, state);
            if !read_only {
                add_item(&menu_box, "删除此引用", "deleteFootnoteReference", &self, state);
            }
        }
        if command_enabled("cancelAlert") {
            add_separator(&menu_box);
            add_item(&menu_box, "取消提示块", "cancelAlert", &self, state);
        }
        let _ = used_ordinary_layout;

        // 消息多播会让本函数在一次右键里被调用多次:弹新前关旧,只留一个。
        if let Some(old) = self.active_context_popover.take() {
            old.popdown();
            old.unparent();
        }
        let popover = gtk::Popover::new();
        popover.set_has_arrow(false);
        popover.set_pointing_to(Some(&rect));
        popover.set_child(Some(&menu_box));
        let popover_for_closed = popover.clone();
        popover.connect_closed(move |popover| {
            popover.unparent();
            let _ = popover_for_closed;
        });
        popover.set_parent(&self.window);
        *self.active_context_popover.borrow_mut() = Some(popover.clone());
        popover.popup();
    }

    fn adjust_zoom(self: &Rc<Self>, state: &Rc<TabState>, delta_y: f64) {
        let current = state.zoom.get();
        let next = if delta_y < 0.0 { current * 1.1 } else { current / 1.1 };
        let clamped = next.clamp(0.3, 3.0);
        state.zoom.set(clamped);
        state.webview.set_zoom_level(clamped);
        self.refresh_status_bar_content(state);
        self.set_status(&format!("缩放 {:.0}%", clamped * 100.0));
    }

    fn update_status_from_editor(self: &Rc<Self>, state: &Rc<TabState>, message: &EditorMessage) {
        let payload = message.payload.clone().unwrap_or_default();
        // 缓存供统计对话框读取。
        *state.last_editor_status.borrow_mut() = Some(payload.clone());
        let character_count = payload
            .get("characterCount")
            .and_then(|v| v.as_i64())
            .unwrap_or(0);
        let line = payload.get("line").and_then(|v| v.as_i64()).unwrap_or(0);
        let column = payload.get("column").and_then(|v| v.as_i64()).unwrap_or(0);
        let block_type = payload
            .get("blockType")
            .and_then(|v| v.as_str())
            .unwrap_or("");
        self.character_count_button
            .borrow()
            .set_label(&format!("{character_count} 字符"));
        self.block_type_label
            .borrow()
            .set_text(&block_type_display_name(block_type));
        self.position_label
            .borrow()
            .set_text(&format!("行 {line} 列 {column}"));
        self.refresh_status_bar_content(state);
    }

    /// 文档统计对话框(对齐 macOS showStatistics 的字段)。
    fn show_statistics_dialog(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let Some(stats) = state.last_editor_status.borrow().clone() else {
            self.set_status("尚未加载编辑器状态");
            return;
        };
        let field = |key: &str| -> i64 {
            stats
                .get(key)
                .and_then(|v| v.as_i64())
                .unwrap_or(0)
        };
        let dialog = gtk::Dialog::builder()
            .title("文档统计")
            .transient_for(&self.window)
            .modal(true)
            .default_width(320)
            .build();
        dialog.add_button("关闭", gtk::ResponseType::Close);
        let area = dialog.content_area();
        let grid = gtk::Grid::new();
        grid.set_row_spacing(8);
        grid.set_column_spacing(16);
        grid.set_margin_top(12);
        grid.set_margin_bottom(12);
        grid.set_margin_start(16);
        grid.set_margin_end(16);
        let rows = [
            ("字符数", field("characterCount")),
            ("总字符数(含空格)", field("totalCharacterCount")),
            ("非空白字符", field("nonWhitespaceCharacterCount")),
            ("西文单词", field("westernWordCount")),
            ("中日韩字符", field("cjkCharacterCount")),
            ("段落", field("paragraphCount")),
            ("代码行", field("codeLineCount")),
            ("公式", field("formulaCount")),
            ("选中字符", field("selectedCharacterCount")),
        ];
        for (index, (name, value)) in rows.iter().enumerate() {
            let name_label = gtk::Label::builder().halign(gtk::Align::Start).build();
            name_label.set_text(name);
            let value_label = gtk::Label::builder().halign(gtk::Align::Start).build();
            value_label.set_text(&value.to_string());
            grid.attach(&name_label, 0, index as i32, 1, 1);
            grid.attach(&value_label, 1, index as i32, 1, 1);
        }
        area.append(&grid);
        dialog.connect_response(move |dialog, _response| {
            dialog.close();
        });
        dialog.present();
    }

    /// 显示查找面板;回车发 findText(query\tcase\twhole)。
    fn show_find_bar(self: &Rc<Self>) {
        self.find_bar.set_search_mode(true);
        self.find_entry.grab_focus();
    }

    fn send_find_command(self: &Rc<Self>, command: &str) {
        let query = self.find_entry.text().to_string();
        let payload = serde_json::json!({ "command": command, "text": format!("{query}\t0\t0") });
        if let Some(state) = self.active_tab() {
            let document_id = state.document.borrow().document_id.clone();
            state
                .webview
                .post_to_editor(&HostMessage::command_with_request(&document_id, 0, payload, None));
        }
    }

    fn wire_find_bar(
        self: &Rc<Self>,
        find_entry: &gtk::SearchEntry,
        find_next: &gtk::Button,
        find_prev: &gtk::Button,
        find_close: &gtk::Button,
    ) {
        {
            let this = self.clone();
            find_entry.connect_activate(move |_entry| this.send_find_command("findText"));
        }
        {
            let this = self.clone();
            find_next.connect_clicked(move |_| this.send_find_command("findNext"));
        }
        {
            let this = self.clone();
            find_prev.connect_clicked(move |_| this.send_find_command("findPrev"));
        }
        {
            let this = self.clone();
            find_close.connect_clicked(move |_| {
                this.find_bar.set_search_mode(false);
                if let Some(state) = this.active_tab() {
                    let document_id = state.document.borrow().document_id.clone();
                    state.webview.post_to_editor(&HostMessage::command_with_request(
                        &document_id,
                        0,
                        serde_json::json!({ "command": "findClose" }),
                        None,
                    ));
                }
            });
        }
        let _ = find_entry;
    }

    fn export_active_html(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else {
            return;
        };
        let document = state.document.borrow();
        let title = document
            .path
            .as_ref()
            .and_then(|p| p.file_stem())
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "未命名".into());
        let document_id = document.document_id.clone();
        // 载荷形态对齐 macOS EditorSession.requestExportHTML(spec §6)。
        let payload = serde_json::json!({
            "format": "html",
            "style": "serif",
            "header": "",
            "footer": "",
            "fontSize": 16,
            "lineHeight": 1.75,
            "maxWidth": 820,
            "visualCjkAutoSpacing": true,
            "colorSchemeCss": "",
            "title": title,
            "keepTablesTogether": false,
            "keepHeadingsWithNextBlock": false,
        });
        drop(document);
        state.webview.post_to_editor(&HostMessage::command_with_request(
            &document_id,
            0,
            serde_json::json!({ "command": "exportDocument", "text": payload.to_string() }),
            Some("export-html"),
        ));
        self.set_status("正在生成 HTML…");
    }

    fn export_active_image(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else {
            return;
        };
        let webview = state.webview.clone();
        let window = self.window.clone();
        webview.widget().snapshot(
            SnapshotRegion::FullDocument,
            SnapshotOptions::empty(),
            None::<&gtk::gio::Cancellable>,
            move |outcome| match outcome {
                Ok(texture) => {
                    let dialog = gtk::FileDialog::new();
                    dialog.set_initial_name(Some("export.png"));
                    dialog.save(Some(&window), None::<&gio::Cancellable>, move |result| {
                        if let Ok(file) = result {
                            if let Some(path) = file.path() {
                                // 分片数学见 markleaf::export::slice_heights
                                // (v1 先整图落盘;按高度分片在打磨阶段接 cairo 裁切)。
                                if let Err(err) = texture.save_to_png(&path) {
                                    eprintln!("[export] PNG 写入失败 {err}");
                                } else {
                                    println!("[export] 长图已保存 {}", path.display());
                                }
                            }
                        }
                    });
                }
                Err(err) => eprintln!("[export] 全文档快照失败 {err}"),
            },
        );
    }

    fn print_active(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else {
            return;
        };
        let operation = PrintOperation::new(state.webview.widget());
        operation.run_dialog(Some(&self.window));
    }

    /// 启动工作区 inotify 监视:防抖变更置位标志,主循环 500ms 轮询消费后重扫侧栏
    /// (watcher 回调在 notify 线程,而 Rc<AppWindow> 非 Send,故用原子标志桥接)。
    fn start_workspace_watcher(self: &Rc<Self>, root: &Path) {
        *self.workspace_root.borrow_mut() = Some(root.to_path_buf());
        let needs_rescan = self.needs_rescan.clone();
        match crate::workspace::watcher::WorkspaceWatcher::new(root, 200, move || {
            needs_rescan.store(true, std::sync::atomic::Ordering::Relaxed);
        }) {
            Ok(watcher) => {
                *self.watcher.borrow_mut() = Some(watcher);
                let this = self.clone();
                let flag = self.needs_rescan.clone();
                glib::timeout_add_local(std::time::Duration::from_millis(500), move || {
                    if flag.swap(false, std::sync::atomic::Ordering::Relaxed) {
                        let Some(root) = this.workspace_root.borrow().clone() else {
                            return glib::ControlFlow::Continue;
                        };
                        this.sidebar.set_workspace_root(Some(root));
                    }
                    glib::ControlFlow::Continue
                });
            }
            Err(err) => log::warn!("[workspace] 监视启动失败:{err}"),
        }
    }

    fn update_tab_title(self: &Rc<Self>, state: &Rc<TabState>) {
        let document = state.document.borrow();
        let name = document
            .path
            .as_ref()
            .and_then(|p| p.file_name())
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| "未命名".into());
        let marker = if document.dirty { " •" } else { "" };
        state.page.set_title(&format!("{name}{marker}"));
    }

    fn wire_tab_view(self: &Rc<Self>) {
        let this = self.clone();
        self.tabs.borrow().connect_close_page(move |view, page| {
            let matched = this
                .tab_states
                .borrow()
                .iter()
                .find(|s| s.page == *page)
                .cloned();
            let Some(state) = matched else {
                return glib::Propagation::Proceed;
            };
            if !state.document.borrow().dirty {
                return glib::Propagation::Proceed;
            }
            // 脏文档关闭需确认(对齐桌面):保存并关闭 / 不保存 / 取消。
            let name = state
                .document
                .borrow()
                .path
                .as_ref()
                .and_then(|p| p.file_name())
                .map(|n| n.to_string_lossy().into_owned())
                .unwrap_or_else(|| "未命名".into());
            let dialog = adw::MessageDialog::builder()
                .heading(&format!("是否保存对“{name}”的更改?"))
                .body("不保存将丢弃本次编辑内容。")
                .transient_for(&this.window)
                .modal(true)
                .build();
            dialog.add_response("cancel", "取消");
            dialog.add_response("discard", "不保存");
            dialog.add_response("save", "保存并关闭");
            dialog.set_response_appearance("save", adw::ResponseAppearance::Suggested);
            dialog.set_close_response("cancel");
            let this = this.clone();
            let view = view.clone();
            dialog.connect_response(None, move |_, response| {
                match response {
                    "save" => {
                        // 保存成功后再关闭。先解除原请求，保存期间仍可重试关闭。
                        view.close_page_finish(&state.page, false);
                        state.close_after_save.set(true);
                        state.webview.widget().set_sensitive(false);
                        this.save_tab(&state);
                    }
                    "discard" => {
                        state.close_after_save.set(false);
                        let document_id = {
                            let mut document = state.document.borrow_mut();
                            document.dirty = false;
                            document.pending_save = false;
                            document.document_id.clone()
                        };
                        let (_config, state_dir) = crate::app::storage_dirs();
                        discard_recovery(&state_dir.join("recovery"), &document_id);
                        view.close_page_finish(&state.page, true);
                    }
                    _ => {
                        if state.close_after_save.replace(false) {
                            state.webview.widget().set_sensitive(true);
                        }
                        view.close_page_finish(&state.page, false);
                    }
                }
            });
            dialog.present();
            glib::Propagation::Stop
        });

        // 页面移除(确认关闭/正常关闭):取消恢复定时器并从标签表摘除。
        {
            let this = self.clone();
            self.tabs.borrow().connect_page_detached(move |_view, page, _position| {
                let matched = this
                    .tab_states
                    .borrow()
                    .iter()
                    .find(|s| s.page == *page)
                    .cloned();
                if let Some(state) = matched {
                    if state.startup_cover.is_visible() && !state.document.borrow().pending_save {
                        state.webview.widget().stop_loading();
                    }
                    if let Some(source) = state.recovery_source.borrow_mut().take() {
                        source.remove();
                    }
                    this.tab_states
                        .borrow_mut()
                        .retain(|candidate| !Rc::ptr_eq(candidate, &state));
                }
            });
        }
    }

    /// 状态栏接线:侧栏切换/统计对话框/编码/换行/模式/缩放菜单与可见性。
    fn wire_status_bar(self: &Rc<Self>) {
        // 侧栏切换按钮。
        {
            let this = self.clone();
            self.sidebar_toggle_button
                .borrow()
                .connect_clicked(move |_| this.animate_sidebar_toggle());
        }

        // 字符数按钮 → 文档统计对话框。
        {
            let this = self.clone();
            self.character_count_button
                .borrow()
                .connect_clicked(move |_| this.show_statistics_dialog());
        }

        let window = Rc::downgrade(self);
        install_status_choice_menu(
            &self.encoding_button.borrow(),
            &["UTF-8", "UTF-16LE", "GB18030", "GBK", "GB2312", "Big5", "Shift_JIS", "US-ASCII"],
            None,
            true,
            move |value| {
                if let Some(window) = window.upgrade() {
                    window.reload_active_tab_with_encoding(value.to_string());
                }
            },
        );
        let window = Rc::downgrade(self);
        install_status_choice_menu(
            &self.newline_button.borrow(), &["LF", "CRLF"], None, false,
            move |value| {
                if let Some(window) = window.upgrade() {
                    window.set_active_tab_newline(value);
                }
            },
        );
        let window = Rc::downgrade(self);
        self.mode_button.borrow().connect_clicked(move |_| {
            if let Some(window) = window.upgrade() {
                window.send_command_to_active("toggleSourceMode");
            }
        });
        let window = Rc::downgrade(self);
        install_status_choice_menu(
            &self.zoom_button.borrow(),
            &["50%", "75%", "90%", "100%", "110%", "125%", "150%", "175%", "200%"],
            Some(("重置为100%", "100%")),
            true,
            move |value| {
                if let (Some(window), Ok(percent)) =
                    (window.upgrade(), value.trim_end_matches('%').parse())
                {
                    window.set_active_tab_zoom(percent);
                }
            },
        );

        let window = Rc::downgrade(self);
        self.tabs.borrow().connect_selected_page_notify(move |_| {
            if let Some(window) = window.upgrade() {
                window.dismiss_context_menu();
                window.refresh_status_bar_selection();
            }
        });
        self.refresh_status_bar_selection();

        // 拖拽分隔条 → 钳制最小宽度 + 记忆宽度到设置(关闭时统一写盘)。
        // GTK 对 Paned 拖拽不强制 child minimum(分配小于内容最小宽度时
        // 内容居中裁切、文案溢出窗口外),这里按内容实测最小宽度动态钳制。
        {
            let window = Rc::downgrade(self);
            self.paned.connect_position_notify(move |paned| {
                let Some(window) = window.upgrade() else { return };
                // 中间帧允许低于最小宽度，也不应保存为用户设置。
                if window.sidebar_animation.borrow().is_some() {
                    return;
                }
                let position = paned.position();
                if position == 0 {
                    return; // 隐藏态(宽度记忆切换)不干预。
                }
                let clamped = window.clamp_sidebar_width(position);
                if position != clamped {
                    paned.set_position(clamped);
                    return;
                }
                window.sidebar_width_memory.set(position);
                window.settings.borrow_mut().sidebar_width = position;
            });
        }
    }

    /// 注册纯内核命令的统一入口(win.command::<id>):command_router 按桌面
    /// 路由表分派,Passthrough 直达内核,Host 走 dispatch_host_command。
    fn install_command_action(self: &Rc<Self>) {
        let this = self.clone();
        let command_action = gio::SimpleAction::new("command", Some(glib::VariantTy::STRING));
        command_action.connect_activate(move |_, parameter| {
            let command = parameter.and_then(|v| v.str()).unwrap_or("");
            if !command.is_empty() {
                this.dispatch_command(command);
            }
        });
        self.window.add_action(&command_action);
    }

    /// 按当前标签和设置刷新状态栏各控件可见性。
    fn refresh_status_bar_visibility(self: &Rc<Self>) {
        let has_document = self.tabs.borrow().selected_page().is_some();
        let bar = &self.settings.borrow().status_bar;
        self.sidebar_toggle_button
            .borrow()
            .set_visible(bar.sidebar_toggle_visible);
        self.status_text.borrow().set_visible(
            bar.command_status_visible && bar.command_display_mode != StatusBarCommandDisplayMode::Hidden,
        );
        self.character_count_button
            .borrow()
            .set_visible(has_document && bar.word_count_visible);
        self.block_type_label
            .borrow()
            .set_visible(has_document && bar.block_type_visible);
        self.position_label.borrow().set_visible(has_document && bar.position_visible);
        self.encoding_button.borrow().set_visible(has_document && bar.encoding_visible);
        self.newline_button.borrow().set_visible(has_document && bar.new_line_visible);
        self.mode_button.borrow().set_visible(has_document && bar.mode_toggle_visible);
        self.zoom_button.borrow().set_visible(has_document && bar.zoom_visible);
    }

    /// 按指定编码重读当前标签文档并重载(loadDocument 原地刷新)。
    /// 脏文档先确认:切换编码以磁盘内容为基准,未保存修改将被丢弃。
    fn reload_active_tab_with_encoding(self: &Rc<Self>, label: String) {
        let Some(state) = self.active_tab() else { return };
        if encoding_rs::Encoding::for_label(label.as_bytes())
            == Some(state.document.borrow().encoding.encoding)
        {
            return;
        }
        let Some(path) = state.document.borrow().path.clone() else {
            self.set_status("未命名文档无法切换编码");
            return;
        };
        if state.document.borrow().dirty {
            let dialog = adw::MessageDialog::builder()
                .heading("切换编码将重读文件")
                .body("当前文档有未保存的修改,切换编码会以磁盘内容重新载入,未保存的修改将丢失。")
                .transient_for(&self.window)
                .modal(true)
                .build();
            dialog.add_response("cancel", "取消");
            dialog.add_response("discard", "放弃修改并切换");
            dialog.set_response_appearance("discard", adw::ResponseAppearance::Destructive);
            let this = self.clone();
            let state = state.clone();
            let label = label.clone();
            dialog.connect_response(None, move |dialog, response| {
                dialog.close();
                if response == "discard" {
                    let state = state.clone();
                    let label = label.clone();
                    this.reload_active_tab_with_encoding_now(state, label);
                }
            });
            dialog.present();
            return;
        }
        self.reload_active_tab_with_encoding_now(state, label);
    }

    fn reload_active_tab_with_encoding_now(
        self: &Rc<Self>,
        state: Rc<TabState>,
        label: String,
    ) {
        let Some(path) = state.document.borrow().path.clone() else {
            return;
        };
        let this = self.clone();
        glib::spawn_future_local(async move {
            let path_for_task = path.clone();
            let snapshot = gio::spawn_blocking(move || {
                crate::documents::store::read_document(&path_for_task)
            })
            .await;
            let Ok(Ok(snapshot)) = snapshot else {
                this.set_status("编码切换失败:无法读取文件");
                return;
            };
            let encoding = encoding_rs::Encoding::for_label(label.as_bytes());
            let Some(encoding) = encoding else {
                this.set_status("未知编码");
                return;
            };
            let (decoded, _, had_errors) = encoding.decode(&snapshot.bytes);
            if had_errors {
                this.set_status(&format!("用 {label} 解码存在有损字符"));
            }
            let decoded = decoded.into_owned();
            {
                let mut document = state.document.borrow_mut();
                document.disk = snapshot;
                document.encoding = crate::documents::controller::EncodingDecision {
                    encoding,
                    bom: false,
                };
                document.dirty = true;
            }
            this.update_tab_title(&state);
            let markdown = decoded;
            let document_id = state.document.borrow().document_id.clone();
            let payload = serde_json::json!({
                "markdown": markdown,
                "documentType": state.document.borrow().document_type,
                "readOnly": false,
                "initialDirty": true,
                "scrollTop": 0,
                "restoreViewState": true,
            });
            state.webview.post_to_editor(&HostMessage::load_document(&document_id, payload));
            this.refresh_status_bar_content(&state);
        });
    }

    /// 换行符切换:写回 TabDocument(保存时经 apply_newline 生效)并置脏。
    fn set_active_tab_newline(self: &Rc<Self>, name: &str) {
        let Some(state) = self.active_tab() else { return };
        let policy = match name {
            "LF" => crate::documents::encoding::NewLinePolicy::Lf,
            "CRLF" => crate::documents::encoding::NewLinePolicy::Crlf,
            _ => return,
        };
        if state.document.borrow().newline == policy {
            return;
        }
        state.document.borrow_mut().newline = policy;
        state.document.borrow_mut().dirty = true;
        self.update_tab_title(&state);
        self.refresh_status_bar_content(&state);
    }

    /// 缩放:webview zoom level + 状态栏文本 + 设置持久化。
    fn set_active_tab_zoom(self: &Rc<Self>, percent: i32) {
        let Some(state) = self.active_tab() else { return };
        let level = percent as f64 / 100.0;
        state.zoom.set(level);
        state.webview.set_zoom_level(level);
        self.refresh_status_bar_content(&state);
        {
            let mut settings = self.settings.borrow_mut();
            settings.zoom_percent = percent;
        }
        crate::app::persist_settings(&self.settings.borrow());
    }

    fn refresh_status_bar_selection(self: &Rc<Self>) {
        self.refresh_status_bar_visibility();
        if let Some(state) = self.active_tab() {
            self.refresh_status_bar_content(&state);
        } else {
            self.character_count_button.borrow().set_label("");
            self.block_type_label.borrow().set_text("");
            self.position_label.borrow().set_text("");
            self.encoding_button.borrow().set_sensitive(false);
            self.newline_button.borrow().set_sensitive(false);
            self.mode_button.borrow().set_sensitive(false);
            self.zoom_button.borrow().set_sensitive(false);
        }
    }

    /// 刷新状态栏中依赖当前标签的文本(编码/换行/模式/缩放)。
    fn refresh_status_bar_content(self: &Rc<Self>, state: &Rc<TabState>) {
        if self.tabs.borrow().selected_page().as_ref() != Some(&state.page) {
            return;
        }
        let document = state.document.borrow();
        let encoding_name = document.encoding.encoding.name();
        self.encoding_button.borrow().set_label(encoding_name);
        self.encoding_button.borrow().set_sensitive(true);
        self.newline_button
            .borrow()
            .set_label(document.newline.as_str());
        self.newline_button.borrow().set_sensitive(true);
        self.mode_button.borrow().set_label(if state.source_mode.get() {
            "源码"
        } else {
            "可视化"
        });
        self.mode_button.borrow().set_sensitive(document.document_type != "plainText");
        self.zoom_button
            .borrow()
            .set_label(&format!("{}%", (state.zoom.get() * 100.0).round() as i32));
        self.zoom_button.borrow().set_sensitive(true);
    }

    fn install_actions(self: &Rc<Self>, app: &adw::Application, windows: Rc<RefCell<Vec<Rc<AppWindow>>>>) {
        // startup 时挂载 WebContext 数据(窗口构造时 context() 需要)。
        // 由 app.rs 保证顺序;此处防御:若缺失,首标签打开会 panic(可观测)。

        let this = self.clone();
        let open_action = gio::SimpleAction::new("open", None);
        open_action.connect_activate(move |_, _| this.run_open_dialog());
        self.window.add_action(&open_action);

        // 新建:Markdown / 纯文本(对齐桌面「新建」子菜单)。
        for (action_name, doc_type) in [("newMarkdown", "markdown"), ("newPlainText", "plainText")] {
            let this = self.clone();
            let action = gio::SimpleAction::new(action_name, None);
            action.connect_activate(move |_, _| this.create_tab_blank_with_type(doc_type));
            self.window.add_action(&action);
        }

        // 最近项目:openRecent::<index>(菜单按 settings.recent_files 顺序生成)。
        let this = self.clone();
        let open_recent = gio::SimpleAction::new("openRecent", Some(glib::VariantTy::STRING));
        open_recent.connect_activate(move |_, parameter| {
            let index = parameter
                .and_then(|v| v.str())
                .and_then(|s| s.parse::<usize>().ok())
                .unwrap_or(0);
            if let Some(entry) = this.settings.borrow().recent_files.get(index) {
                let path = std::path::PathBuf::from(&entry.path);
                this.open_document_in_tab(&path);
            }
        });
        self.window.add_action(&open_recent);

        // 菜单分组标题占位(GIO 菜单项必须指向 action 才渲染文本)。
        let disabled_placeholder = gio::SimpleAction::new("disabled-placeholder", None);
        self.window.add_action(&disabled_placeholder);

        // 最近文件夹:openRecentFolder::<index>(按 settings.recent_folders 顺序)。
        let this = self.clone();
        let open_recent_folder =
            gio::SimpleAction::new("openRecentFolder", Some(glib::VariantTy::STRING));
        open_recent_folder.connect_activate(move |_, parameter| {
            let index = parameter
                .and_then(|v| v.str())
                .and_then(|s| s.parse::<usize>().ok())
                .unwrap_or(0);
            if let Some(entry) = this.settings.borrow().recent_folders.get(index) {
                let path = std::path::PathBuf::from(&entry.path);
                this.sidebar.set_workspace_root(Some(path.clone()));
                this.start_workspace_watcher(&path);
            }
        });
        self.window.add_action(&open_recent_folder);

        // 分享:当前文档内容走 xdg-open 无意义;复制内容到剪贴板(桌面 shareActiveTab 等价物)。
        let this = self.clone();
        let share_action = gio::SimpleAction::new("share", None);
        share_action.connect_activate(move |_, _| this.share_active_document());
        self.window.add_action(&share_action);

        let this = self.clone();
        let save_action = gio::SimpleAction::new("save", None);
        save_action.connect_activate(move |_, _| this.save_active_tab());
        self.window.add_action(&save_action);

        let this = self.clone();
        let save_as_action = gio::SimpleAction::new("saveAs", None);
        save_as_action.connect_activate(move |_, _| this.save_active_tab_as());
        self.window.add_action(&save_as_action);

        let this = self.clone();
        let source_action = gio::SimpleAction::new("sourceMode", None);
        source_action.connect_activate(move |this_window, _| {
            this.send_command_to_active("toggleSourceMode");
            let _ = this_window;
        });
        self.window.add_action(&source_action);

        // 上下文菜单命令(参数 = 命令 id)。
        let this = self.clone();
        let ctx_action = gio::SimpleAction::new("ctx-command", Some(glib::VariantTy::STRING));
        ctx_action.connect_activate(move |_, parameter| {
            if let Some(id) = parameter.and_then(|v| v.str()) {
                this.dispatch_command(id);
            }
        });
        self.window.add_action(&ctx_action);

        // 导出 HTML:发 exportDocument 命令,回包落盘(见 ExportContent 分支)。
        let this_export = self.clone();
        let export_action = gio::SimpleAction::new("exportHtml", None);
        export_action.connect_activate(move |_, _| this_export.export_active_html());
        self.window.add_action(&export_action);
        app.set_accels_for_action("win.exportHtml", &["<Control><Shift>e"]);

        // 导出长图:全文档快照 → save_to_png。
        let this_image = self.clone();
        let image_action = gio::SimpleAction::new("exportImage", None);
        image_action.connect_activate(move |_, _| this_image.export_active_image());
        self.window.add_action(&image_action);

        // 打印/PDF:WebKit 打印对话框(GTK 打印面板内可选“另存为 PDF”——
        // 页眉页脚/页码为显式降级项,spec §6)。
        let this_print = self.clone();
        let print_action = gio::SimpleAction::new("print", None);
        print_action.connect_activate(move |_, _| this_print.print_active());
        self.window.add_action(&print_action);
        app.set_accels_for_action("win.print", &["<Control>p"]);

        // 打开工作区目录 → 侧栏扫描 + inotify 监视。
        let sidebar_for_folder = self.sidebar.clone();
        let sidebar_watch_holder: Rc<RefCell<Option<Rc<Self>>>> = Rc::new(RefCell::new(None));
        let holder_for_cb = sidebar_watch_holder.clone();
        let open_folder_action = gio::SimpleAction::new("openFolder", None);
        {
            let window = self.window.clone();
            open_folder_action.connect_activate(move |_, _| {
                let dialog = gtk::FileDialog::new();
                let sidebar = sidebar_for_folder.clone();
                let holder_for_cb = holder_for_cb.clone();
                dialog.select_folder(Some(&window), None::<&gio::Cancellable>, move |result| {
                    if let Ok(file) = result {
                        if let Some(path) = file.path() {
                            sidebar.set_workspace_root(Some(path.clone()));
                            if let Some(this) = holder_for_cb.borrow().as_ref() {
                                this.start_workspace_watcher(&path);
                                {
                                    let name = path
                                        .file_name()
                                        .map(|n| n.to_string_lossy().into_owned())
                                        .unwrap_or_else(|| path.to_string_lossy().into_owned());
                                    let mut settings = this.settings.borrow_mut();
                                    settings.remember_recent_folder(
                                        path.to_string_lossy().as_ref(),
                                        &name,
                                    );
                                }
                                crate::app::persist_settings(&this.settings.borrow());
                            }
                        }
                    }
                });
            });
        }
        *sidebar_watch_holder.borrow_mut() = Some(self.clone());
        let _ = &sidebar_watch_holder;
        self.window.add_action(&open_folder_action);
        app.set_accels_for_action("win.openFolder", &["<Control><Shift>o"]);
        app.set_accels_for_action("win.toggleSidebar", &["<Control>backslash"]);

        let this = self.clone();
        let new_workspace_file = gio::SimpleAction::new("newWorkspaceMarkdown", None);
        new_workspace_file.connect_activate(move |_, _| this.create_markdown_from_sidebar());
        self.window.add_action(&new_workspace_file);

        // 侧栏开关动作(动画开合)。
        let this_for_toggle = self.clone();
        let toggle_sidebar_action = gio::SimpleAction::new("toggleSidebar", None);
        toggle_sidebar_action.connect_activate(move |_, _| {
            this_for_toggle.animate_sidebar_toggle();
        });
        self.window.add_action(&toggle_sidebar_action);

        // 关闭窗口动作(菜单)。
        let close_action = gio::SimpleAction::new("close-window", None);
        {
            let windows = windows.clone();
            close_action.connect_activate(move |_, _| {
                if let Some(w) = windows.borrow().last().cloned() {
                    w.window.close();
                }
            });
        }
        self.window.add_action(&close_action);

        // 加速键:注册在 app 层(win. 前缀)。
        let table = crate::app::shortcuts(&self.settings.borrow());
        for command in ["open", "save", "saveAs", "find", "sourceMode"] {
            if let Some(accel_string) = table.accel_for(command) {
                app.set_accels_for_action(&format!("win.{command}"), &[accel_string.as_str()]);
            }
        }
        app.set_accels_for_action("win.close-window", &["<Control><Shift>w"]);
    }

    fn apply_shortcuts(self: &Rc<Self>, _app: &adw::Application) {
        // 补充的按键(编辑类)在 M4 打磨;核心集已在 install_actions 注册。
    }

    fn active_tab(self: &Rc<Self>) -> Option<Rc<TabState>> {
        let view = self.tabs.borrow();
        let selected = view.selected_page()?;
        self.tab_states
            .borrow()
            .iter()
            .find(|state| state.page == selected)
            .cloned()
    }

    /// 分享当前文档:requestSnapshot(requestId="share")回包取全文写入 GDK
    /// 系统剪贴板(Linux 桌面 shareActiveTab 等价物:无系统分享面板,复制即分享)。
    fn share_active_document(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let document_id = state.document.borrow().document_id.clone();
        state.webview.post_to_editor(&HostMessage::command_with_request(
            &document_id,
            0,
            serde_json::json!({ "command": "requestSnapshot" }),
            Some("share"),
        ));
    }

    /// 代码语言声明对话框(对齐桌面 declareCodeBlockLanguage):预设下拉 + 自由输入,
    /// 确认后发 setCodeBlockLanguage(text)。
    #[allow(deprecated)]
    fn show_code_language_dialog(self: &Rc<Self>) {
        let dialog = gtk::Dialog::builder()
            .title(crate::l10n::tr(&self.lang, "声明代码语言").unwrap_or_else(|| "声明代码语言".into()))
            .transient_for(&self.window)
            .modal(true)
            .build();
        let content = dialog.content_area();
        let box_ = gtk::Box::new(gtk::Orientation::Vertical, 8);
        box_.set_margin_top(12);
        box_.set_margin_bottom(4);
        box_.set_margin_start(12);
        box_.set_margin_end(12);
        let entry = gtk::Entry::new();
        entry.set_placeholder_text(Some("kotlin"));
        let dropdown = gtk::DropDown::from_strings(&[
            "swift", "c", "cpp", "objective-c", "java", "kotlin", "python",
            "javascript", "typescript", "jsx", "tsx", "html", "css", "json",
            "yaml", "xml", "shell", "powershell", "sql", "markdown", "go",
            "rust", "php", "ruby", "r", "latex", "mermaid",
        ]);
        dropdown.connect_selected_notify({
            let entry = entry.clone();
            move |dd| {
                if let Some(text) = dd.selected_item().and_then(|i| i.downcast::<gtk::StringObject>().ok()) {
                    entry.set_text(&text.string());
                }
            }
        });
        box_.append(&entry);
        box_.append(&dropdown);
        content.append(&box_);
        dialog.add_button(
            crate::l10n::tr(&self.lang, "取消").unwrap_or_else(|| "取消".into()).as_str(),
            gtk::ResponseType::Cancel,
        );
        dialog.add_button(
            crate::l10n::tr(&self.lang, "确定").unwrap_or_else(|| "确定".into()).as_str(),
            gtk::ResponseType::Ok,
        );
        let this = self.clone();
        dialog.connect_response(move |dialog, response| {
            if response == gtk::ResponseType::Ok {
                let language = entry.text().to_string();
                if let Some(state) = this.active_tab() {
                    let document_id = state.document.borrow().document_id.clone();
                    state.webview.post_to_editor(&HostMessage::command_with_request(
                        &document_id,
                        0,
                        serde_json::json!({ "command": "setCodeBlockLanguage", "text": language }),
                        None,
                    ));
                    this.set_status(
                        &crate::l10n::tr(&this.lang, "代码语言已更新")
                            .unwrap_or_else(|| "代码语言已更新".into()),
                    );
                }
            }
            dialog.close();
        });
        dialog.present();
    }

    fn save_active_tab(self: &Rc<Self>) {
        if let Some(state) = self.active_tab() {
            self.save_tab(&state);
        }
    }

    /// 保存全部:遍历标签逐个保存(无路径的未命名文档弹另存为)。
    /// 工作区右键命令分派:文件系统变更走 mutation + 重扫;剪贴板/打开直达。
    fn handle_workspace_context_command(
        self: &Rc<Self>,
        target: &crate::ui::sidebar::ContextTarget,
        action: &str,
        relative: &str,
    ) {
        let Some(root) = self.workspace_root.borrow().clone() else {
            return;
        };
        let refresh = || {
            self.sidebar.set_workspace_root(Some(root.clone()));
        };
        match action {
            "open" => {
                self.open_document_in_tab(&root.join(relative));
            }
            "newMarkdown" | "newPlainText" => {
                self.prompt_new_workspace_file(&root, relative, action == "newMarkdown");
            }
            "newFolder" => {
                self.prompt_new_workspace_folder(&root, relative);
            }
            "rename" => {
                self.prompt_rename_entry(&root, relative);
            }
            "delete" => {
                self.confirm_delete_entry(&root, target);
            }
            "copyPath" => {
                self.copy_text_to_clipboard(&root.join(relative).to_string_lossy());
                self.set_status("路径已复制");
            }
            "copyContent" => match crate::workspace::mutation::read_text(&root, relative) {
                Ok(text) => {
                    self.copy_text_to_clipboard(&text);
                    self.set_status("文件内容已复制");
                }
                Err(err) => self.set_status(&format!("读取失败:{err}")),
            },
            "refresh" => {
                refresh();
            }
            "closeWorkspace" => {
                self.sidebar.set_workspace_root(None);
                *self.workspace_root.borrow_mut() = None;
                *self.watcher.borrow_mut() = None;
                self.set_status("已关闭工作区");
            }
            _ => {}
        }
    }

    fn clamp_sidebar_width(&self, width: i32) -> i32 {
        let min_width = self.sidebar.min_width();
        let max_width = (self.paned.width() - 320).min(520).max(min_width);
        width.clamp(min_width, max_width)
    }

    fn animate_sidebar_toggle(self: &Rc<Self>) {
        let current = self.paned.position();
        let previous = self.sidebar_animation.borrow_mut().take();
        let opening = if let Some(animation) = previous {
            animation.source.remove();
            !animation.opening
        } else {
            if current > 0 {
                self.sidebar_width_memory.set(self.clamp_sidebar_width(current));
            }
            current == 0
        };
        let target = if opening {
            self.clamp_sidebar_width(self.sidebar_width_memory.get())
        } else {
            0
        };
        if current == target {
            return;
        }
        if !self.paned.settings().is_gtk_enable_animations() {
            self.paned.set_position(target);
            return;
        }

        // 虚拟机中的帧时钟可能停顿，用主循环定时更新，并按实际耗时插值。
        use std::time::{Duration, Instant};
        let start = Instant::now();
        let duration = Duration::from_millis(180);
        let window = Rc::downgrade(self);
        let source = glib::timeout_add_local(Duration::from_millis(15), move || {
            let Some(window) = window.upgrade() else { return glib::ControlFlow::Break };
            let progress = (start.elapsed().as_secs_f64() / duration.as_secs_f64()).min(1.0);
            let eased = 1.0 - (1.0 - progress).powi(3);
            let position = current as f64 + (target - current) as f64 * eased;
            window.paned.set_position(position.round() as i32);
            if progress >= 1.0 {
                window.sidebar_animation.borrow_mut().take();
                glib::ControlFlow::Break
            } else {
                glib::ControlFlow::Continue
            }
        });
        self.sidebar_animation.replace(Some(SidebarAnimation { source, opening }));
    }

    /// 只读模式切换:内核 setReadOnly(1/0);状态写 TabDocument.read_only。
    fn toggle_read_only_mode(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let next = !state.read_only.get();
        state.read_only.set(next);
        let document_id = state.document.borrow().document_id.clone();
        state.webview.post_to_editor(&HostMessage::command_with_request(
            &document_id,
            0,
            serde_json::json!({ "command": "setReadOnly", "text": if next { "1" } else { "0" } }),
            None,
        ));
        self.set_status(if next { "已开启只读模式" } else { "已关闭只读模式" });
    }

    /// 文本写入系统剪贴板。
    fn copy_text_to_clipboard(self: &Rc<Self>, text: &str) {
        self.window
            .clipboard()
            .set_text(text);
    }

    /// 输入对话框(单行 Entry,确认回调拿到文本)。
    fn prompt_text_input(
        self: &Rc<Self>,
        title: &str,
        initial: &str,
        on_confirm: impl Fn(String) + 'static,
    ) {
        let dialog = gtk::Dialog::builder()
            .title(title)
            .transient_for(&self.window)
            .modal(true)
            .default_width(360)
            .build();
        dialog.add_button("取消", gtk::ResponseType::Cancel);
        dialog.add_button("确定", gtk::ResponseType::Apply);
        let entry = gtk::Entry::builder()
            .text(initial)
            .margin_top(12)
            .margin_bottom(12)
            .margin_start(16)
            .margin_end(16)
            .build();
        dialog.content_area().append(&entry);
        let dialog_for_entry = dialog.clone();
        let entry_for_activate = entry.clone();
        entry.connect_activate(move |_| {
            dialog_for_entry.response(gtk::ResponseType::Apply);
        });
        dialog.connect_response(move |dialog, response| {
            if response == gtk::ResponseType::Apply {
                on_confirm(entry_for_activate.text().to_string());
            }
            dialog.close();
        });
        dialog.present();
        entry.grab_focus();
    }

    /// 重命名后同步已打开标签:路径、磁盘基线、标题、恢复快照 path 字段。
    fn sync_tabs_after_rename(self: &Rc<Self>, old_abs: &Path, new_abs: &Path) {
        let states: Vec<_> = self.tab_states.borrow().clone();
        for state in &states {
            let matches = state
                .document
                .borrow()
                .path
                .as_ref()
                .map(|p| p == old_abs)
                .unwrap_or(false);
            if !matches {
                continue;
            }
            {
                let mut document = state.document.borrow_mut();
                document.path = Some(new_abs.to_path_buf());
                document.disk.path = new_abs.to_path_buf();
            }
            self.update_tab_title(state);
            // 恢复快照(若存在)同步 path,避免还原时打开旧路径。
            let document_id = state.document.borrow().document_id.clone();
            let (_config, state_dir) = crate::app::storage_dirs();
            let recovery_dir = state_dir.join("recovery");
            for entry in crate::data::recovery::list_recovery(&recovery_dir) {
                if entry.document_id == document_id {
                    let updated = crate::data::recovery::RecoveryEntry {
                        document_id: entry.document_id,
                        path: Some(new_abs.to_string_lossy().into_owned()),
                        markdown: entry.markdown,
                        revision: entry.revision,
                        saved_at: entry.saved_at,
                    };
                    let _ = crate::data::recovery::save_recovery(&recovery_dir, &updated);
                }
            }
        }
    }

    fn create_markdown_from_sidebar(self: &Rc<Self>) {
        let Some(root) = self.workspace_root.borrow().clone() else {
            if let Some(open_folder) = self.window.lookup_action("openFolder") {
                open_folder.activate(None);
            }
            return;
        };
        let directory = self.active_tab().and_then(|tab| {
            let document = tab.document.borrow();
            let path = document.path.as_deref()?;
            path.strip_prefix(&root).ok()?;
            path.parent().map(Path::to_path_buf)
        });
        let directory = directory.as_deref().unwrap_or(&root);
        let result = crate::workspace::mutation::create_markdown(directory, "");
        self.finish_workspace_file_creation(&root, result);
    }

    /// 新建工作区文档(相对目录;空 = 根)。
    fn prompt_new_workspace_file(self: &Rc<Self>, root: &Path, dir_relative: &str, markdown: bool) {
        let root = root.to_path_buf();
        let dir_relative = dir_relative.to_string();
        let default_name = if markdown { "未命名.md" } else { "未命名.txt" };
        let this = self.clone();
        let dir_relative = dir_relative.to_string();
        self.prompt_text_input("新建文件", default_name, move |name| {
            if name.is_empty() {
                return;
            }
            let relative_dir = if dir_relative.is_empty() {
                String::new()
            } else {
                format!("{dir_relative}/")
            };
            let result = if markdown {
                crate::workspace::mutation::create_markdown(&root, &relative_dir)
            } else {
                crate::workspace::mutation::create_plain_text(&root, &relative_dir)
            };
            this.finish_workspace_file_creation(&root, result);
        });
    }

    fn finish_workspace_file_creation(self: &Rc<Self>, root: &Path, result: std::io::Result<PathBuf>) {
        match result {
            Ok(path) => {
                self.sidebar.set_workspace_root(Some(root.to_path_buf()));
                self.open_document_in_tab(&path);
            }
            Err(err) => self.set_status(&format!("新建失败:{err}")),
        }
    }

    /// 新建工作区文件夹(相对目录;空 = 根)。
    fn prompt_new_workspace_folder(self: &Rc<Self>, root: &Path, dir_relative: &str) {
        let root = root.to_path_buf();
        let dir_relative = dir_relative.to_string();
        let this = self.clone();
        self.prompt_text_input("新建文件夹", "", move |name| {
            if name.is_empty() {
                return;
            }
            let relative_dir = if dir_relative.is_empty() {
                String::new()
            } else {
                format!("{dir_relative}/")
            };
            match crate::workspace::mutation::create_folder(&root, &relative_dir, &name) {
                Ok(_) => {
                    this.sidebar.set_workspace_root(Some(root.to_path_buf()));
                    this.set_status("文件夹已创建");
                }
                Err(err) => this.set_status(&format!("新建失败:{err}")),
            }
        });
    }

    /// 重命名工作区条目(Entry 预填当前名)。
    fn prompt_rename_entry(self: &Rc<Self>, root: &Path, relative: &str) {
        let root = root.to_path_buf();
        let relative = relative.to_string();
        let this = self.clone();
        let current_name = relative
            .rsplit('/')
            .next()
            .unwrap_or(relative.as_str())
            .to_string();
        let current_name_for_check = current_name.clone();
        self.prompt_text_input("重命名", &current_name, move |name| {
            if name.is_empty() || name == current_name_for_check {
                return;
            }
            let old_abs = root.join(&relative);
            match crate::workspace::mutation::rename_entry(&root, &relative, &name) {
                Ok(new_relative) => {
                    let new_abs = root.join(&new_relative);
                    this.sidebar.set_workspace_root(Some(root.to_path_buf()));
                    this.sync_tabs_after_rename(&old_abs, &new_abs);
                    this.set_status(&format!(
                        "已重命名为 {}",
                        new_abs.file_name().map(|n| n.to_string_lossy()).unwrap_or_default()
                    ));
                }
                Err(err) => this.set_status(&format!("重命名失败:{err}")),
            }
        });
    }

    /// 删除确认(文件 remove/文件夹递归)。
    fn confirm_delete_entry(self: &Rc<Self>, root: &Path, target: &crate::ui::sidebar::ContextTarget) {
        use crate::ui::sidebar::ContextTarget;
        let (relative, detail) = match target {
            ContextTarget::File(relative) => (relative, ""),
            ContextTarget::Folder(relative) => (relative, "\n文件夹将连同内容一起删除。"),
            ContextTarget::Root => return,
        };
        let root = root.to_path_buf();
        let relative = relative.clone();
        let dialog = gtk::Dialog::builder()
            .title("删除")
            .transient_for(&self.window)
            .modal(true)
            .default_width(360)
            .build();
        dialog.add_button("取消", gtk::ResponseType::Cancel);
        dialog.add_button("删除", gtk::ResponseType::Apply);
        let label = gtk::Label::builder()
            .label(format!("确定删除 {relative}?{detail}"))
            .margin_top(12)
            .margin_bottom(12)
            .margin_start(16)
            .margin_end(16)
            .build();
        dialog.content_area().append(&label);
        let this = self.clone();
        let relative = relative.to_string();
        dialog.connect_response(move |dialog, response| {
            if response == gtk::ResponseType::Apply {
                match crate::workspace::mutation::delete_entry(&root, &relative) {
                    Ok(_) => {
                        this.sidebar.set_workspace_root(Some(root.to_path_buf()));
                        this.set_status("已删除");
                    }
                    Err(err) => this.set_status(&format!("删除失败:{err}")),
                }
            }
            dialog.close();
        });
        dialog.present();
    }

    /// 保存全部:遍历脏标签逐个发起保存;未命名文档走另存为(逐个弹框)。
    /// 保存为异步闭环,这里统计请求状态而非落盘结果(落盘失败各自报状态栏)。
    fn save_all_tabs(self: &Rc<Self>) {
        let states: Vec<_> = self.tab_states.borrow().clone();
        let mut requested = 0usize;
        let mut prompted = 0usize;
        let mut clean = 0usize;
        for state in &states {
            let dirty = state.document.borrow().dirty;
            let has_path = state.document.borrow().path.is_some();
            if !dirty {
                clean += 1;
                continue;
            }
            if has_path {
                self.save_tab(state);
                requested += 1;
            } else {
                self.save_tab(state);
                prompted += 1;
            }
        }
        let mut parts = Vec::new();
        if requested > 0 {
            parts.push(format!("已请求保存 {requested} 个"));
        }
        if prompted > 0 {
            parts.push(format!("{prompted} 个新文档请逐个选择保存位置"));
        }
        if clean > 0 && requested == 0 && prompted == 0 {
            parts.push(format!("{clean} 个文档均无更改"));
        }
        if parts.is_empty() {
            parts.push("没有打开的文档".into());
        }
        self.set_status(&format!("保存全部:{}", parts.join(";")));
    }

    /// 关于对话框(版本/平台;对齐桌面 showAbout 的信息子集)。
    fn show_about_dialog(self: &Rc<Self>) {
        let dialog = gtk::Dialog::builder()
            .title("关于 MarkLeaf")
            .transient_for(&self.window)
            .modal(true)
            .default_width(360)
            .build();
        dialog.add_button("关闭", gtk::ResponseType::Close);
        let about_box = gtk::Box::new(gtk::Orientation::Vertical, 8);
        about_box.set_margin_top(20);
        about_box.set_margin_bottom(20);
        about_box.set_margin_start(24);
        about_box.set_margin_end(24);
        let name_label = gtk::Label::new(None);
        name_label.set_markup("<big><b>MarkLeaf</b></big>");
        let version_label = gtk::Label::new(Some(&format!(
            "版本 {} · Linux (GTK4 + WebKitGTK)",
            env!("CARGO_PKG_VERSION")
        )));
        about_box.append(&name_label);
        about_box.append(&version_label);
        dialog.content_area().append(&about_box);
        dialog.present();
    }

    fn save_active_tab_as(self: &Rc<Self>) {
        if let Some(state) = self.active_tab() {
            self.run_save_as_dialog(&state);
        }
    }

fn popover_from_box(child: &gtk::Box) -> gtk::Popover {
    let popover = gtk::Popover::new();
    popover.set_child(Some(child));
    popover
}

    /// 链接打开:http(s)/mailto 走系统浏览器;相对路径按文档目录解析,
    /// Markdown/纯文本在工作区内打开为新标签,其余交给系统默认应用。
    fn open_link(self: &Rc<Self>, url: &str) {
        let lower = url.to_lowercase();
        if lower.starts_with("http://")
            || lower.starts_with("https://")
            || lower.starts_with("mailto:")
        {
            open_uri(url);
            return;
        }
        // 相对/绝对本地路径:先按文档所在目录解析,再回退工作区目录。
        let Some(state) = self.active_tab() else { return };
        let document_dir = state
            .document
            .borrow()
            .path
            .as_ref()
            .and_then(|p| p.parent().map(|p| p.to_path_buf()));
        let workspace = self.workspace_root.borrow().clone();
        let candidates = [
            document_dir.as_ref().map(|d| d.join(url)),
            workspace.as_ref().map(|w| w.join(url)),
        ];
        for candidate in candidates.iter().flatten() {
            if candidate.is_file() {
                let ext = candidate
                    .extension()
                    .map(|e| e.to_string_lossy().to_lowercase())
                    .unwrap_or_default();
                if ext == "md" || ext == "markdown" || ext == "txt" {
                    self.open_document_in_tab(candidate);
                } else {
                    open_uri(&candidate.to_string_lossy());
                }
                return;
            }
        }
        open_uri(url);
    }

    /// 本地图片插入:文件对话框选择 → 拷贝到文档目录 assets/ →
    /// 发 insertImage(相对路径 + 提示文本)命令(对齐 macOS copyToAssets)。
    fn insert_local_image(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let Some(document_path) = state.document.borrow().path.clone() else {
            self.set_status("请先将文档保存到磁盘后再插入图片");
            return;
        };
        let dialog = gtk::FileDialog::new();
        let this = self.clone();
        dialog.open(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            let Ok(file) = result else { return };
            let Some(source) = file.path() else { return };
            let Some(target) = this.copy_image_to_assets(&document_path, &source) else {
                return;
            };
            let Some(relative) = target
                .strip_prefix(
                    document_path
                        .parent()
                        .map(|p| p.to_path_buf())
                        .as_deref()
                        .unwrap_or(Path::new(".")),
                )
                .map(|p| p.to_string_lossy().into_owned())
                .ok()
            else {
                return;
            };
            let Some(state) = this.active_tab() else { return };
            let document_id = state.document.borrow().document_id.clone();
            state.webview.post_to_editor(&HostMessage::command_with_request(
                &document_id,
                0,
                serde_json::json!({ "command": "insertImage", "text": format!("{relative}\n图片") }),
                None,
            ));
            this.set_status("图片已插入文档");
        });
    }

    /// 拷贝图片到文档同目录 assets/ 下,返回目标路径(无法定位文档目录时
    /// 返回 None 并提示)。
    fn copy_image_to_assets(
        self: &Rc<Self>,
        document_path: &Path,
        source: &Path,
    ) -> Option<PathBuf> {
        let assets_dir = document_path.parent()?.join("assets");
        let _ = std::fs::create_dir_all(&assets_dir);
        let file_name = source
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_else(|| format!("image-{}", glib::uuid_string_random()));
        let mut target = assets_dir.join(&file_name);
        let mut index = 2;
        while target.exists() {
            let stem = Path::new(&file_name)
                .file_stem()
                .map(|s| s.to_string_lossy().into_owned())
                .unwrap_or_default();
            let ext = source
                .extension()
                .map(|e| format!(".{}", e.to_string_lossy()))
                .unwrap_or_default();
            target = assets_dir.join(format!("{stem}-{index}{ext}"));
            index += 1;
        }
        match std::fs::copy(source, &target) {
            Ok(_) => Some(target),
            Err(err) => {
                self.set_status(&format!("复制图片失败:{err}"));
                None
            }
        }
    }

    /// 以只读方式打开:选择文件后以 readOnly=true 载入新标签。
    fn run_open_read_only_dialog(self: &Rc<Self>) {
        let dialog = gtk::FileDialog::new();
        let this = self.clone();
        dialog.open(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            if let Ok(file) = result {
                if let Some(path) = file.path() {
                    this.open_document_in_tab_read_only(&path);
                }
            }
        });
    }

    /// 在新窗口中打开:选择文件后创建新窗口并在其中载入。
    fn run_open_in_new_window_dialog(self: &Rc<Self>) {
        let dialog = gtk::FileDialog::new();
        let this = self.clone();
        dialog.open(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            if let Ok(file) = result {
                if let Some(path) = file.path() {
                    let window = this.create_window();
                    window.open_document_in_tab(&path);
                }
            }
        });
    }

    /// 打开文档为只读标签(工具提示与保存命令受 read_only 标志约束)。
    fn open_document_in_tab_read_only(self: &Rc<Self>, path: &Path) {
        self.open_document_in_tab(path);
        if let Some(state) = self.tab_states.borrow().last().cloned() {
            state.read_only.set(true);
            let document_id = state.document.borrow().document_id.clone();
            state.webview.post_to_editor(&HostMessage::command_with_request(
                &document_id,
                0,
                serde_json::json!({ "command": "setReadOnly", "text": "1" }),
                None,
            ));
            self.set_status(&format!("以只读方式打开 {}", path.display()));
        }
    }

    /// 在工作区定位:侧栏树中选中当前文档(菜单入口)。
    fn reveal_active_tab_in_workspace(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else {
            self.set_status("没有打开的文档");
            return;
        };
        let path_for_reveal = state.document.borrow().path.clone();
        let Some(path) = path_for_reveal.as_deref() else {
            self.set_status("未命名文档无法在工作区定位");
            return;
        };
        self.sidebar.reveal_workspace_path(path);
    }

    /// 复制当前标签文件路径到剪贴板。
    fn copy_active_tab_path(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let Some(path) = state.document.borrow().path.clone() else {
            self.set_status("未命名文档没有路径");
            return;
        };
        self.window
            .clipboard()
            .set_text(path.to_string_lossy().as_str());
        self.set_status("文件路径已复制");
    }

    /// 复制当前标签文件内容到剪贴板(请求内核快照,requestId=copy-contents)。
    fn copy_active_file_contents(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let document_id = state.document.borrow().document_id.clone();
        state
            .webview
            .post_to_editor(&HostMessage::request_snapshot(&document_id, 0, "copy-contents"));
    }

    /// 重命名当前文档(文件菜单入口):输入对话框 + 原地重命名 + 标签同步。
    fn prompt_rename_active_tab(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        let Some(path) = state.document.borrow().path.clone() else {
            self.set_status("未命名文档请使用「另存为」");
            return;
        };
        let this = self.clone();
        let current_name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        self.prompt_text_input("重命名", &current_name, move |name| {
            if name.is_empty() || name == current_name {
                return;
            }
            let Some(parent) = path.parent().map(|p| p.to_path_buf()) else {
                return;
            };
            let new_abs = parent.join(&name);
            if new_abs.exists() {
                this.set_status(&format!("{} 已存在", name));
                return;
            }
            match std::fs::rename(&path, &new_abs) {
                Ok(_) => {
                    this.sync_tabs_after_rename(&path, &new_abs);
                    this.set_status(&format!("已重命名为 {name}"));
                }
                Err(err) => this.set_status(&format!("重命名失败:{err}")),
            }
        });
    }

    /// 关闭当前标签页:走 close_page 信号(脏文档触发确认链)。
    fn close_current_tab(self: &Rc<Self>) {
        let Some(state) = self.active_tab() else { return };
        self.tabs.borrow().close_page(&state.page);
    }

    /// 关闭工作区(文件菜单入口):清侧栏/监视器与根记录。
    fn close_workspace(self: &Rc<Self>) {
        self.sidebar.set_workspace_root(None);
        *self.workspace_root.borrow_mut() = None;
        *self.watcher.borrow_mut() = None;
        self.set_status("已关闭工作区");
    }

    /// WebKit 原生编辑命令(剪切/拷贝/粘贴):作用于编辑器当前选区,
    /// 与 macOS 转发第一响应者的剪贴板行为等价。
    fn exec_editing_command(self: &Rc<Self>, command: &str) {
        let Some(state) = self.active_tab() else { return };
        state.webview.widget().execute_editing_command(command);
    }

    fn send_command_to_active(self: &Rc<Self>, command: &str) {
        if let Some(state) = self.active_tab() {
            let document_id = state.document.borrow().document_id.clone();
            state.webview.post_to_editor(&HostMessage::command_with_request(
                &document_id,
                0,
                serde_json::json!({ "command": command }),
                None,
            ));
        }
    }

    /// 命令入口:路由(透传 vs 宿主)后分发。
    fn dispatch_command(self: &Rc<Self>, command_id: &str) {
        match route(command_id) {
            CommandRoute::Passthrough => self.send_command_to_active(command_id),
            CommandRoute::Host(id) => self.dispatch_host_command(id),
        }
    }

    fn dispatch_host_command(self: &Rc<Self>, id: &str) {
        match id {
            "open" => self.run_open_dialog(),
            "openReadOnly" => self.run_open_read_only_dialog(),
            "openInNewWindow" => self.run_open_in_new_window_dialog(),
            "revealActiveTabInWorkspace" => self.reveal_active_tab_in_workspace(),
            "copyActiveTabPath" => self.copy_active_tab_path(),
            "copyActiveFileContents" => self.copy_active_file_contents(),
            "renameActiveTab" => self.prompt_rename_active_tab(),
            "closeCurrentTab" => self.close_current_tab(),
            "closeFolder" => self.close_workspace(),
            "save" => self.save_active_tab(),
            "saveAll" => self.save_all_tabs(),
            "saveAs" => self.save_active_tab_as(),
            "toggleSidebar" => self.animate_sidebar_toggle(),
            "recoverUnsavedFiles" => crate::app::prompt_recovered_documents(self),
            "toggleReadOnlyMode" => self.toggle_read_only_mode(),
            "showAbout" => self.show_about_dialog(),
            "openLink" => {
                // 右键菜单"打开链接":链接地址取自最近 commandState 缓存。
                let href = self
                    .active_tab()
                    .and_then(|state| state.last_editor_status.borrow().clone())
                    .and_then(|payload| payload.get("linkHref").and_then(|v| v.as_str()).map(str::to_string));
                if let Some(href) = href {
                    self.open_link(&href);
                }
            }
            "sourceMode" => self.send_command_to_active("toggleSourceMode"),
            "find" => self.show_find_bar(),
            "showPreferences" => self.show_preferences_dialog(),
            "newWindow" | "openInNewWindow" => self.create_window(),
            "new" => {
                self.create_tab_blank();
            }
            // 声明代码语言:弹语言选择对话框(对齐桌面 declareCodeBlockLanguage)。
            "declareCodeLanguage" => self.show_code_language_dialog(),
            // 本地图片:文件对话框选择后拷入文档目录 assets 并插入相对引用。
            "insertImage" => self.insert_local_image(),
            "changeImage" => self.insert_local_image(),
            "insertImageFromUrl" => {
                self.set_status("从互联网插入图片在 Linux 版尚未支持");
            }
            // macOS 将菜单 id rotateImage 映射为内核 rotateImageClockwise(M1 审计顺延项)。
            "rotateImage" => self.send_command_to_active("rotateImageClockwise"),
            "undo" | "redo" | "toggleBold" | "toggleItalic" | "toggleUnderline" | "toggleStrike"
            | "toggleHighlight" | "toggleBlockquote" | "promoteHeading" | "demoteHeading"
            | "selectAll" | "exitCode" | "insertTable" | "insertHorizontalRule"
            | "insertLineBefore" | "insertLineAfter" | "clearFormat" | "setParagraph" => {
                self.send_command_to_active(id)
            }
            // 剪贴板:WebKit 原生编辑命令(作用于编辑器选区)。
            "cut" => self.exec_editing_command("cut"),
            "copy" => self.exec_editing_command("copy"),
            "paste" => self.exec_editing_command("paste"),
            "pastePlainText" => self.exec_editing_command("paste-plain-text"),
            // 插入类命令内核自带无参实现:透传。
            "toggleCodeBlock" | "insertMathInline" | "insertMathBlock" | "insertMermaid"
            | "insertFootnote" | "showFrontMatter" | "rerenderAllMermaid" | "formatPainter"
            | "formatCodeBlock" | "duplicateParagraph" | "deleteParagraph" => {
                self.send_command_to_active(id)
            }
            // Linux 首发未映射的宿主命令:显式提示,不静默。
            other => self.set_status(&format!("命令 {other} 在 Linux 版尚未映射")),
        }
    }

    /// 新窗口:复用本窗口的 app/context/settings,注册进窗口表。
    fn create_window(self: &Rc<Self>) {
        let Some(app) = self.window.application().and_then(|a| a.downcast::<adw::Application>().ok()) else {
            return;
        };
        let window = AppWindow::new(&app, &self.context, self.settings.clone(), self.windows.clone());
        window.present();
        self.windows.borrow_mut().push(window);
    }

    /// 空白新标签(未命名文档)。
    pub(crate) fn create_tab_blank(self: &Rc<Self>) {
        self.create_tab_blank_with_type("markdown");
    }

    /// 设置对话框(v1):快捷键自定义(核心命令) + 设置文件路径。
    /// gtk::Dialog 4.10 起 deprecated;M5 打磨迁 AdwDialog(同 TreeView 取舍,台账已记)。
    #[allow(deprecated)]
    fn show_preferences_dialog(self: &Rc<Self>) {
        let dialog = gtk::Dialog::builder()
            .title(crate::l10n::tr(&self.lang, "设置").unwrap_or_else(|| "设置".into()))
            .transient_for(&self.window)
            .modal(true)
            .default_width(560)
            .build();
        let grid = gtk::Grid::new();
        grid.set_row_spacing(8);
        grid.set_column_spacing(8);
        grid.set_margin_top(12);
        grid.set_margin_bottom(12);
        grid.set_margin_start(12);
        grid.set_margin_end(12);

        let table = crate::app::shortcuts(&self.settings.borrow());
        let commands = [
            "save", "saveAs", "open", "new", "find", "sourceMode", "print",
            "toggleBold", "toggleItalic", "zoomIn", "zoomOut", "resetZoom",
        ];
        let entries: Vec<(String, gtk::Entry)> = commands
            .iter()
            .map(|command| {
                let label = gtk::Label::new(Some(command));
                label.set_halign(gtk::Align::Start);
                let entry = gtk::Entry::new();
                entry.set_text(table.accel_for(command).as_deref().unwrap_or(""));
                entry.set_hexpand(true);
                (command.to_string(), entry.clone())
            })
            .collect();
        for (row, (command, entry)) in entries.iter().enumerate() {
            grid.attach(&gtk::Label::new(Some(command)), 0, row as i32, 1, 1);
            grid.attach(entry, 1, row as i32, 1, 1);
        }
        let _ = &entries;
        dialog.content_area().append(&grid);
        let status_bar_button = dialog.add_button("自定义状态栏…", gtk::ResponseType::Other(1));
        status_bar_button.set_halign(gtk::Align::Start);
        dialog.add_button("保存", gtk::ResponseType::Apply);
        dialog.add_button(crate::l10n::tr(&self.lang, "关闭").unwrap_or_else(|| "关闭".into()).as_str(), gtk::ResponseType::Close);

        let this = self.clone();
        let entries_for_save = entries.iter().map(|(command, entry)| (command.clone(), entry.clone())).collect::<Vec<_>>();
        dialog.connect_response(move |dialog, response| {
            if response == gtk::ResponseType::Other(1) {
                this.show_status_bar_settings_dialog();
            } else if response == gtk::ResponseType::Apply {
                let mut settings = this.settings.borrow_mut();
                settings.shortcuts.clear();
                let mut table = crate::ui::shortcuts::default_table();
                let mut conflicts = Vec::new();
                for (command, entry) in &entries_for_save {
                    let accel = entry.text().to_string();
                    if accel.is_empty() {
                        continue;
                    }
                    if let Err(conflict) = table.bind(command, &accel) {
                        conflicts.push(format!("{command}: {accel} 与 {} 冲突", conflict.command));
                    }
                }
                settings.shortcuts = table.custom_bindings().clone();
                crate::app::persist_settings(&settings);
                drop(settings);
                if conflicts.is_empty() {
                    this.set_status("快捷键已保存");
                } else {
                    this.set_status(&format!("部分快捷键被跳过:{}", conflicts.join(";")));
                }
                dialog.close();
            } else if response == gtk::ResponseType::Close || response == gtk::ResponseType::DeleteEvent {
                dialog.close();
            }
        });
        dialog.present();
    }

    /// 「自定义状态栏」对话框:10 项显示开关 + 命令状态显示模式(对齐桌面)。
    /// 确定写回 settings.status_bar 并立即刷新可见性;取消不保存。
    #[allow(deprecated)]
    fn show_status_bar_settings_dialog(self: &Rc<Self>) {
        let dialog = gtk::Dialog::builder()
            .title("自定义状态栏")
            .transient_for(&self.window)
            .modal(true)
            .default_width(460)
            .build();
        dialog.add_button("取消", gtk::ResponseType::Cancel);
        dialog.add_button("确定", gtk::ResponseType::Apply);

        let grid = gtk::Grid::new();
        grid.set_row_spacing(10);
        grid.set_column_spacing(28);
        grid.set_margin_top(12);
        grid.set_margin_bottom(12);
        grid.set_margin_start(16);
        grid.set_margin_end(16);

        let current = self.settings.borrow().status_bar.clone();
        let make_check = |label: &str, on: bool| {
            let check = gtk::CheckButton::with_label(label);
            check.set_active(on);
            check
        };
        let sidebar_toggle_check = make_check("显示侧栏切换按钮", current.sidebar_toggle_visible);
        let command_status_check = make_check("显示命令状态", current.command_status_visible);
        let word_count_check = make_check("显示字符数", current.word_count_visible);
        let block_type_check = make_check("显示块类型", current.block_type_visible);
        let position_check = make_check("显示光标位置", current.position_visible);
        let encoding_check = make_check("显示编码", current.encoding_visible);
        let new_line_check = make_check("显示换行符", current.new_line_visible);
        let mode_toggle_check = make_check("显示源码/可视化切换", current.mode_toggle_visible);
        let zoom_check = make_check("显示缩放比例", current.zoom_visible);

        let command_mode_combo = gtk::ComboBoxText::new();
        for title in ["始终显示", "临时显示", "隐藏"] {
            command_mode_combo.append_text(title);
        }
        let mode_index = match current.command_display_mode {
            StatusBarCommandDisplayMode::Always => 0,
            StatusBarCommandDisplayMode::Temporary => 1,
            StatusBarCommandDisplayMode::Hidden => 2,
        };
        command_mode_combo.set_active(Some(mode_index));
        let command_mode_row = gtk::Label::builder()
            .label("命令状态显示")
            .halign(gtk::Align::Start)
            .build();

        let cells: [(&gtk::CheckButton, i32, i32); 9] = [
            (&sidebar_toggle_check, 0, 0),
            (&word_count_check, 1, 0),
            (&command_status_check, 0, 1),
            (&block_type_check, 1, 1),
            (&position_check, 0, 2),
            (&encoding_check, 1, 2),
            (&new_line_check, 0, 3),
            (&mode_toggle_check, 1, 3),
            (&zoom_check, 0, 4),
        ];
        for (check, column, row) in cells {
            check.set_halign(gtk::Align::Start);
            grid.attach(check, column, row, 1, 1);
        }
        grid.attach(&command_mode_row, 0, 5, 1, 1);
        grid.attach(&command_mode_combo, 1, 5, 1, 1);

        dialog.content_area().append(&grid);

        let this = self.clone();
        let this = self.clone();
        dialog.connect_response(move |dialog, response| {
            if response != gtk::ResponseType::Apply {
                dialog.close();
                return;
            }
            let mode = match command_mode_combo.active() {
                Some(1) => StatusBarCommandDisplayMode::Temporary,
                Some(2) => StatusBarCommandDisplayMode::Hidden,
                _ => StatusBarCommandDisplayMode::Always,
            };
            {
                let mut settings = this.settings.borrow_mut();
                settings.status_bar = crate::data::settings::StatusBarSettings {
                    sidebar_toggle_visible: sidebar_toggle_check.is_active(),
                    command_status_visible: command_status_check.is_active(),
                    command_display_mode: mode,
                    word_count_visible: word_count_check.is_active(),
                    block_type_visible: block_type_check.is_active(),
                    position_visible: position_check.is_active(),
                    encoding_visible: encoding_check.is_active(),
                    new_line_visible: new_line_check.is_active(),
                    mode_toggle_visible: mode_toggle_check.is_active(),
                    zoom_visible: zoom_check.is_active(),
                };
                crate::app::persist_settings(&settings);
            }
            this.refresh_status_bar_visibility();
            this.set_status("状态栏设置已保存");
            dialog.close();
        });
        dialog.present();
    }

    fn follow_dark_mode(self: &Rc<Self>) {
        let style = adw::StyleManager::default();
        let this = self.clone();
        let style_for_cb = style.clone();
        style.connect_color_scheme_notify(move |_style| {
            // 深浅切换:向活动标签推送 applyStyles(theme 字段由内核主题机制消费)。
            let dark = style_for_cb.is_dark();
            if let Some(state) = this.active_tab() {
                let document_id = state.document.borrow().document_id.clone();
                state.webview.post_to_editor(&HostMessage::apply_styles(
                    &document_id,
                    serde_json::json!({ "themeMode": if dark { "dark" } else { "light" } }),
                ));
            }
        });
    }
}

// ---- EditorWebView 的 ready 通知通道 ----
// ready 前缓存时序:connect_ready 由窗口在装载页面前注册。

fn open_uri(uri: &str) {
    let _ = gio::AppInfo::launch_default_for_uri(uri, None::<&gio::AppLaunchContext>);
}
