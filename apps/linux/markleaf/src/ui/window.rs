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
    /// 活动标签坐标换算用的当前缩放(经 zoomWheel 消息维护)。
    zoom_levels: RefCell<std::collections::HashMap<usize, f64>>,
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

        // 上下文菜单浮层(挂在窗口上,弹出于编辑坐标)。
        let context_popover = PopoverMenu::builder().has_arrow(false).build();
        context_popover.set_parent(&window);

        // HeaderBar:打开/保存/菜单/源码切换/缩放。
        let header = gtk::HeaderBar::new();
        let open_button = gtk::Button::from_icon_name("document-open-symbolic");
        let save_button = gtk::Button::from_icon_name("document-save-symbolic");
        let source_button = gtk::ToggleButton::new();
        source_button.set_icon_name("view-reveal-symbolic");
        let menu_button = gtk::MenuButton::new();
        menu_button.set_icon_name("open-menu-symbolic");
        menu_button.set_menu_model(Some(&menus::main_menu_model()));
        open_button.set_tooltip_text(Some("打开"));
        save_button.set_tooltip_text(Some("保存"));
        source_button.set_tooltip_text(Some("源码模式"));
        header.pack_start(&open_button);
        header.pack_start(&save_button);
        header.pack_start(&source_button);
        header.pack_end(&menu_button);

        let content = gtk::Box::new(gtk::Orientation::Vertical, 0);
        content.append(&header);
        content.append(&tab_bar);
        let scrolled_status = gtk::Box::new(gtk::Orientation::Horizontal, 0);
        scrolled_status.append(&status_label);
        scrolled_status.set_margin_top(2);
        scrolled_status.set_margin_bottom(2);
        content.append(&tab_view);
        content.append(&scrolled_status);
        window.set_content(Some(&content));

        let this = Rc::new(Self {
            window,
            context: context.clone(),
            tabs: RefCell::new(tab_view),
            tab_states: RefCell::new(Vec::new()),
            status_text: RefCell::new(status_label),
            settings,
            context_popover,
            zoom_levels: RefCell::new(Default::default()),
        });

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
        let Ok(snapshot) = read_document(path) else {
            eprintln!("[window] 无法读取 {}", path.display());
            return;
        };
        let detection = detect(&snapshot.bytes);
        let decision = EncodingDecision::from_detection(&detection);
        let newline = detect_newline(&detection.decoded);
        let document_id = glib::uuid_string_random().to_string();

        // 标签页:WebView 常驻;标签标题为文件名。
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
                path: Some(path.to_path_buf()),
                disk: snapshot,
                encoding: decision,
                newline,
                document_id: document_id.clone(),
                dirty: false,
                pending_save: false,
            }),
            recovery_source: RefCell::new(None),
        });
        self.tab_states.borrow_mut().push(state.clone());

        // 桥接线:该标签的编辑器消息 → 窗口分发。
        {
            let this = self.clone();
            let state = state.clone();
            webview.connect_message(move |message| this.handle_editor_message(&state, message));
        }

        // 握手:ready 前缓存由 EditorBridge 承担;此处直接时序驱动(M1 冒烟同款)。
        // applyStyles 载荷对齐 macOS applyStylesPayload 形态(baseCss 等),
        // M3 先发最小载荷,主题 CSS 注入在 M4 打磨时补全。
        self.send_after_ready(&state);

        webview.load_editor_page();
        self.set_status(&format!("已打开 {}", path.display()));
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
            let detection = detect(&document.disk.bytes);
            let payload = serde_json::json!({
                "markdown": detection.decoded,
                "documentType": "markdown",
                "readOnly": false,
                "initialDirty": false,
                "scrollTop": 0,
                "restoreViewState": true,
            });
            let load = HostMessage::load_document(&document.document_id, payload);
            drop(document);
            webview.post_to_editor(&load);
        });
        let _ = pending_styles;
    }

    fn handle_editor_message(self: &Rc<Self>, state: &Rc<TabState>, message: EditorMessage) {
        match message.message_type {
            EditorMessageType::Ready => { /* ready 握手由 send_after_ready 的类型化回调处理 */ }
            EditorMessageType::StylesApplied => {
                // loadDocument 在 ready 闭包链里发送;此处确认样式落地。
            }
            EditorMessageType::DocumentLoaded => {
                let document_id = message.document_id.clone();
                if document_id == state.document.borrow().document_id {
                    state.session.borrow_mut().confirm(&message);
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
            EditorMessageType::EditorStatusChanged => self.update_status_from_editor(state, &message),
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
        let (_config, state_dir) = crate::app::storage_dirs();
        // 内容:发起快照请求,回包在 handle_snapshot 检测恢复意图;
        // 简化路径:标记 pending_recovery,快照回包时写入。
        drop(document);
        state
            .webview
            .post_to_editor(&crate::bridge::messages::HostMessage::request_snapshot(
                &state.document.borrow().document_id,
                0,
                "recovery",
            ));
        let _ = state_dir;
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
            // 恢复快照:原子写 .recovery.json。
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
        let menu = menus::context_menu_model();
        self.context_popover.set_menu_model(Some(&menu));

        // WebView 坐标 → 窗口坐标(考虑缩放)。
        let zoom = self
            .zoom_levels
            .borrow()
            .get(&self.tab_index(state))
            .copied()
            .unwrap_or(1.0);
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

    fn tab_index(self: &Rc<Self>, state: &Rc<TabState>) -> usize {
        self.tab_states
            .borrow()
            .iter()
            .position(|candidate| Rc::ptr_eq(candidate, state))
            .unwrap_or(0)
    }

    fn adjust_zoom(self: &Rc<Self>, state: &Rc<TabState>, delta_y: f64) {
        let index = self.tab_index(state);
        let current = self.zoom_levels.borrow().get(&index).copied().unwrap_or(1.0);
        let next = if delta_y < 0.0 { current * 1.1 } else { current / 1.1 };
        let clamped = next.clamp(0.3, 3.0);
        self.zoom_levels.borrow_mut().insert(index, clamped);
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
        let table = crate::ui::shortcuts::default_table();
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
