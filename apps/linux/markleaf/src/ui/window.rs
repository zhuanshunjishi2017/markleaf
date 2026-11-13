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
use crate::documents::controller::EncodingDecision;
use crate::documents::encoding::{apply_newline, detect_newline, detect, encode};
use crate::documents::store::{read_document, write_document, DocumentSnapshot, WriteError};
use crate::editor::webview::{EditorWebView, WebContext};
use webkit::prelude::WebViewExt;
use webkit::{PrintOperation, SnapshotOptions, SnapshotRegion};
use crate::ui::menus;

/// 会话清单条目(退出时由 app 层汇总)。
pub struct WindowSnapshot {
    pub tabs: Vec<String>,
    pub active_tab: usize,
}

/// 每标签的文档状态(标签关闭即释放;WebView 常驻由 TabView 托管)。
struct TabState {
    page: adw::TabPage,
    webview: EditorWebView,
    session: Rc<RefCell<EditorSessionState>>,
    document: RefCell<TabDocument>,
    /// dirty 后恢复快照防抖(glib 定时源 id;标签关闭时移除)。
    recovery_source: RefCell<Option<glib::SourceId>>,
    /// 该标签的 WebView 缩放(zoomWheel 驱动)。
    zoom: std::cell::Cell<f64>,
}

struct TabDocument {
    path: Option<PathBuf>,
    disk: DocumentSnapshot,
    encoding: EncodingDecision,
    newline: crate::documents::encoding::NewLinePolicy,
    document_id: String,
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
            dirty: false,
            pending_save: false,
            recovered_markdown: None,
        }
    }
}

pub struct AppWindow {
    window: adw::ApplicationWindow,
    context: WebContext,
    tabs: RefCell<adw::TabView>,
    /// 标签状态按 TabPage 指针索引。
    tab_states: RefCell<Vec<Rc<TabState>>>,
    status_text: RefCell<gtk::Label>,
    #[allow(dead_code)] // M4 主题接线时消费(深浅/主题 id 下发 applyStyles)
    settings: Rc<RefCell<AppSettings>>,
    /// 上下文菜单浮层(每次请求重建内容后弹出)。
    context_popover: PopoverMenu,

    sidebar: Rc<super::sidebar::Sidebar>,
    split: adw::OverlaySplitView,
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
        let tab_bar = adw::TabBar::builder().view(&tab_view).autohide(false).build();
        let status_label = gtk::Label::builder()
            .halign(gtk::Align::Start)
            .margin_start(8)
            .ellipsize(pango::EllipsizeMode::End)
            .build();

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

        // 上下文菜单浮层(挂在窗口上,弹出于编辑坐标)。
        let context_popover = PopoverMenu::builder().has_arrow(false).build();
        context_popover.set_parent(&window);

        // 界面语言:设置显式值或 glib locale 解析(菜单/工具提示/状态文案用)。
        let lang = crate::l10n::resolve_language(&settings.borrow().display_language);

        // HeaderBar:打开/保存/菜单/源码切换/缩放。
        let header = gtk::HeaderBar::new();
        let open_button = gtk::Button::from_icon_name("document-open-symbolic");
        let save_button = gtk::Button::from_icon_name("document-save-symbolic");
        let source_button = gtk::ToggleButton::new();
        source_button.set_icon_name("view-reveal-symbolic");
        let menu_button = gtk::MenuButton::new();
        menu_button.set_icon_name("open-menu-symbolic");
        menu_button.set_menu_model(Some(&menus::main_menu_model(&lang)));
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

        let sidebar = super::sidebar::Sidebar::new();
        let tab_box = gtk::Box::new(gtk::Orientation::Vertical, 0);
        tab_box.append(&tab_bar);
        tab_box.append(&tab_view);
        let split = adw::OverlaySplitView::builder()
            .sidebar(&sidebar.root)
            .content(&tab_box)
            .collapsed(false)
            .show_sidebar(true)
            .min_sidebar_width(260.0)
            .build();

        let content = gtk::Box::new(gtk::Orientation::Vertical, 0);
        content.append(&header);
        content.append(&find_search_bar);
        content.append(&split);
        let scrolled_status = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        scrolled_status.append(&status_label);
        scrolled_status.set_margin_top(2);
        scrolled_status.set_margin_bottom(2);
        content.append(&scrolled_status);
        window.set_content(Some(&content));

        let sidebar_for_handler = sidebar.clone();

        let this = Rc::new(Self {
            window,
            context: context.clone(),
            tabs: RefCell::new(tab_view),
            tab_states: RefCell::new(Vec::new()),
            status_text: RefCell::new(status_label),
            settings,
            context_popover,
            sidebar,
            split: split.clone(),
            lang,
            workspace_root: RefCell::new(None),
            watcher: RefCell::new(None),
            needs_rescan: std::sync::Arc::new(std::sync::atomic::AtomicBool::new(false)),
            find_bar: find_search_bar.clone(),
            find_entry: find_entry.clone(),
            windows: windows.clone(),
        });

        {
            let this_for_open = this.clone();
            let windows_for_open = windows.clone();
            sidebar_for_handler.set_open_handler(move |path| {
                let window_for_open = this_for_open.clone();
                windows_for_open.borrow_mut().push(window_for_open.clone());
                this_for_open.open_document_in_tab(path);
            });
        }
        this.wire_find_bar(&find_entry, &find_next, &find_prev, &find_close);
        this.install_actions(app, windows);
        this.wire_tab_view();
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
        let page = view.nth_page(index as i32);
        view.set_selected_page(&page);
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
        let path = path.to_path_buf();
        // 文件读取与编码探测移出主线程(审计 Important #3);结果回主循环建标签。
        let this = self.clone();
        let path_for_task = path.to_path_buf();
        let path_for_msg = path.to_path_buf();
        glib::spawn_future_local(async move {
            // JoinHandle::Output = Result<T, Panic>;双层解包(任务 panic 视为读失败)。
            match gio::spawn_blocking(move || read_document(&path_for_task)).await {
                Ok(Ok(snapshot)) => this.create_tab_with_snapshot(path.clone(), snapshot, None),
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
    pub fn open_document_recovered(self: &Rc<Self>, path: &Path, markdown: String) {
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
    ) {
        let detection = detect(&snapshot.bytes);
        let decision = EncodingDecision::from_detection(&detection);
        let newline = detect_newline(&detection.decoded);
        let document_id = glib::uuid_string_random().to_string();

        let webview = EditorWebView::new(&self.context);
        let page = {
            let view = self.tabs.borrow();
            view.append(webview.widget())
        };
        let tab_name = path
            .file_name()
            .map(|n| n.to_string_lossy().into_owned())
            .unwrap_or_default();
        page.set_title(tab_name.as_str());

        let session = Rc::new(RefCell::new(EditorSessionState::new()));
        session.borrow_mut().start_document(&document_id);

        let state = Rc::new(TabState {
            page,
            webview: webview.clone(),
            session,
            document: RefCell::new(TabDocument {
                path: Some(path.clone()),
                disk: snapshot,
                encoding: decision,
                newline,
                document_id: document_id.clone(),
                dirty: recovered_markdown.is_some(),
                pending_save: false,
                recovered_markdown: recovered_markdown.clone(),
            }),
            recovery_source: RefCell::new(None),
            zoom: std::cell::Cell::new(1.0),
        });
        self.tab_states.borrow_mut().push(state.clone());
        log::debug!("[create_tab] document_id={document_id} path={}", path.display());

        {
            let this = self.clone();
            let state = state.clone();
            webview.connect_message(move |message| this.handle_editor_message(&state, message));
        }

        self.send_after_ready(&state);

        webview.load_editor_page();
        if recovered_markdown.is_none() {
            self.set_status(&format!("已打开 {}", path.display()));
        } else {
            // 恢复载入:载入恢复内容并置脏(载入 payload 在 stylesApplied 回调读取)。
            state
                .document
                .borrow_mut()
                .recovered_markdown = recovered_markdown;
        }
        self.update_tab_title(&state);
    }

    // ---- 内部 ----

    fn set_status(self: &Rc<Self>, text: &str) {
        self.status_text.borrow().set_text(text);
    }

    fn send_after_ready(self: &Rc<Self>, state: &Rc<TabState>) {
        // 与冒烟同构:ready → applyStyles → loadDocument。
        // ready 回调经 TabState 的共享 RefCell 通道注册(闭包捕获 state 克隆,无借用逃逸)。
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
            let apply = HostMessage::apply_styles(
                &state_for_ready.document.borrow().document_id,
                serde_json::json!({ "frontendFormatMenu": true }),
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
            let payload = serde_json::json!({
                "markdown": markdown,
                "documentType": "markdown",
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
                    document_id: String::new(),
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
        if message.message_type != EditorMessageType::Ready && !message.document_id.is_empty() {
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
            EditorMessageType::CommandStateChanged => { /* M4:驱动按钮启用态 */ }
            EditorMessageType::OutlineChanged => {
                let items = message
                    .payload
                    .as_ref()
                    .and_then(|payload| payload.get("items"))
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
                if let Some(url) = message.payload.as_ref().and_then(|payload| payload.get("url")).and_then(|v| v.as_str()) {
                    open_uri(url);
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

        if !pending_save {
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
        match result {
            Ok(path) => {
                // 更新磁盘基线,回 markSaved,清恢复快照。
                if let Ok(fresh) = read_document(&path) {
                    state.document.borrow_mut().disk = fresh;
                }
                state.document.borrow_mut().dirty = false;
                state.document.borrow_mut().pending_save = false;
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
        dialog.set_initial_name(Some("未命名.md"));
        let this = self.clone();
        let state = state.clone();
        dialog.save(Some(&self.window), None::<&gio::Cancellable>, move |result| {
            if let Ok(file) = result {
                if let Some(path) = file.path() {
                    // 另存:切换目标路径后走保存闭环。
                    {
                        let mut document = state.document.borrow_mut();
                        document.path = Some(path.clone());
                        document.disk = DocumentSnapshot::default();
                    }
                    this.save_tab(&state);
                }
            }
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

    fn show_context_menu(self: &Rc<Self>, state: &Rc<TabState>, message: &EditorMessage) {
        let (x, y) = (
            message.payload.as_ref().and_then(|p| p.get("clientX")).and_then(|v| v.as_f64()).unwrap_or(0.0),
            message.payload.as_ref().and_then(|p| p.get("clientY")).and_then(|v| v.as_f64()).unwrap_or(0.0),
        );
        let menu = menus::context_menu_model(&self.lang.clone());
        self.context_popover.set_menu_model(Some(&menu));

        // WebView 坐标 → 窗口坐标(考虑缩放)。
        let zoom = state.zoom.get();
        // 4.12+ 用 compute_point;坐标含缩放换算。
        let origin_point = graphene::Point::new((x * zoom) as f32, (y * zoom) as f32);
        let (origin_x, origin_y) = state
            .webview
            .widget()
            .compute_point(&self.window, &origin_point)
            .map(|point| (point.x() as i32 as f64, point.y() as i32 as f64))
            .unwrap_or((0.0, 0.0));
        let rect = gdk::Rectangle::new(origin_x as i32, origin_y as i32, 1, 1);
        self.context_popover.set_pointing_to(Some(&rect));
        self.context_popover.popup();
    }

    fn adjust_zoom(self: &Rc<Self>, state: &Rc<TabState>, delta_y: f64) {
        let current = state.zoom.get();
        let next = if delta_y < 0.0 { current * 1.1 } else { current / 1.1 };
        let clamped = next.clamp(0.3, 3.0);
        state.zoom.set(clamped);
        state.webview.set_zoom_level(clamped);
        self.set_status(&format!("缩放 {:.0}%", clamped * 100.0));
    }

    fn update_status_from_editor(self: &Rc<Self>, _state: &Rc<TabState>, message: &EditorMessage) {
        let payload = message.payload.clone().unwrap_or_default();
        let words = payload.get("words").and_then(|v| v.as_i64()).unwrap_or(0);
        let line = payload.get("line").and_then(|v| v.as_i64()).unwrap_or(0);
        let column = payload.get("column").and_then(|v| v.as_i64()).unwrap_or(0);
        self.set_status(&format!("字数 {words} · 行 {line} 列 {column}"));
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
        self.tabs.borrow().connect_close_page(move |_view, page| {
            // 找到状态,取消恢复定时器并清理恢复快照。
            if let Some(state) = this.tab_states.borrow().iter().find(|s| s.page == *page) {
                if let Some(source) = state.recovery_source.borrow_mut().take() {
                    source.remove();
                }
                let document_id = state.document.borrow().document_id.clone();
                let (_config, state_dir) = crate::app::storage_dirs();
                discard_recovery(&state_dir.join("recovery"), &document_id);
                this.tab_states
                    .borrow_mut()
                    .retain(|candidate| !Rc::ptr_eq(candidate, state));
            }
            glib::Propagation::Proceed
        });
    }

    fn install_actions(self: &Rc<Self>, app: &adw::Application, windows: Rc<RefCell<Vec<Rc<AppWindow>>>>) {
        // startup 时挂载 WebContext 数据(窗口构造时 context() 需要)。
        // 由 app.rs 保证顺序;此处防御:若缺失,首标签打开会 panic(可观测)。

        let this = self.clone();
        let open_action = gio::SimpleAction::new("open", None);
        open_action.connect_activate(move |_, _| this.run_open_dialog());
        self.window.add_action(&open_action);

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

        // 侧栏开关动作。
        let split_ref = RefCell::new(self.split.clone());
        let toggle_sidebar_action = gio::SimpleAction::new("toggleSidebar", None);
        toggle_sidebar_action.connect_activate(move |_, _| {
            let split = split_ref.borrow();
            split.set_show_sidebar(!split.shows_sidebar());
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

    fn save_active_tab(self: &Rc<Self>) {
        if let Some(state) = self.active_tab() {
            self.save_tab(&state);
        }
    }

    fn save_active_tab_as(self: &Rc<Self>) {
        if let Some(state) = self.active_tab() {
            self.run_save_as_dialog(&state);
        }
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
            "save" => self.save_active_tab(),
            "saveAs" => self.save_active_tab_as(),
            "sourceMode" => self.send_command_to_active("toggleSourceMode"),
            "find" => self.show_find_bar(),
            "showPreferences" => self.show_preferences_dialog(),
            "newWindow" | "openInNewWindow" => self.create_window(),
            "new" => {
                self.create_tab_blank();
            }
            // macOS 将菜单 id rotateImage 映射为内核 rotateImageClockwise(M1 审计顺延项)。
            "rotateImage" => self.send_command_to_active("rotateImageClockwise"),
            "undo" | "redo" | "toggleBold" | "toggleItalic" | "toggleUnderline" | "toggleStrike"
            | "toggleHighlight" | "toggleBlockquote" | "promoteHeading" | "demoteHeading"
            | "selectAll" | "exitCode" | "insertTable" | "insertHorizontalRule"
            | "insertLineBefore" | "insertLineAfter" | "clearFormat" | "setParagraph" => {
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
    fn create_tab_blank(self: &Rc<Self>) {
        self.create_tab_with_snapshot(
            PathBuf::from("未命名.md"),
            DocumentSnapshot::default(),
            None,
        );
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
        dialog.add_button("保存", gtk::ResponseType::Apply);
        dialog.add_button(crate::l10n::tr(&self.lang, "关闭").unwrap_or_else(|| "关闭".into()).as_str(), gtk::ResponseType::Close);

        let this = self.clone();
        let entries_for_save = entries.iter().map(|(command, entry)| (command.clone(), entry.clone())).collect::<Vec<_>>();
        dialog.connect_response(move |dialog, response| {
            if response == gtk::ResponseType::Apply {
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
