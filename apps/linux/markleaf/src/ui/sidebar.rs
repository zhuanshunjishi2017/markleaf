// 侧栏:工作区树(ListView)+ 搜索面板 + 大纲。
// 数据源:workspace::model 扫描 / outlineChanged 消息;双击打开文档。
use std::path::{Path, PathBuf};
use std::rc::Rc;

use gtk::glib;
use gtk::prelude::*;
use gtk::{Orientation, ScrolledWindow, SearchEntry, Stack};
#[allow(deprecated)]
use gtk::prelude::TreeModelExtManual;

use crate::workspace::model::{scan_workspace, WorkspaceEntry, WorkspaceKind};
use crate::workspace::search::{search_workspace, SearchMode, SearchHit};

// TreeView/TreeStore 在 GTK4 已 deprecated(4.10 起)但仍完整可用;
// M5 打磨迁 ColumnView/ListView(台账已记,当前优先功能闭环)。
#[allow(deprecated)]
type OpenPathHandler = Box<dyn Fn(&PathBuf)>;
type ContextCommandHandler = Box<dyn Fn(&ContextTarget, &str, &str)>;

/// 右键目标类别:工作区背景 / 文件夹 / 文件。
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ContextTarget {
    Root,
    Folder(String),
    File(String),
}

#[allow(deprecated)]
pub struct Sidebar {
    pub root: gtk::Box,
    /// 工作区/大纲两页切换。
    stack: Stack,
    /// 工作区树模型(重建即刷新;展开状态由 GTK 默认收拢,M3 基线)。
    tree_store: gtk::TreeStore,
    tree_view: gtk::TreeView,
    /// 搜索面板。
    search_entry: SearchEntry,
    search_store: gtk::TreeStore,
    search_view: gtk::TreeView,
    /// 大纲列表(第 0 列显示文本,第 1 列 position 字符串,第 2 列标题文本)。
    outline_store: gtk::TreeStore,
    outline_view: gtk::TreeView,
    /// 大纲条目缓存(过滤重建数据源)。
    outline_cache: RefCell<Vec<(String, i64, String, i32)>>,
    /// 大纲过滤文本(小写;空 = 不过滤)。
    outline_filter: RefCell<String>,
    /// 大纲条目激活回调(窗口注入;参数 = position, 标题文本)。
    on_outline_activate: RefCell<Box<dyn Fn(i64, String)>>,
    /// 双击打开回调(窗口注入)。
    on_open_path: RefCell<OpenPathHandler>,
    /// 当前工作区根(扫描/搜索基准;None = 无工作区)。
    current_workspace_root: RefCell<Option<PathBuf>>,
    /// 实时搜索防抖定时器(输入停顿后触发文件名搜索)。
    search_debounce: RefCell<Option<glib::SourceId>>,
    /// 工作区条目缓存(树/列表切换与排序重建数据源)。
    cached_entries: RefCell<Vec<WorkspaceEntry>>,
    /// 视图模式:false = 树状(默认),true = 列表(卡片)。
    view_list_mode: std::cell::Cell<bool>,
    /// 排序方向:true = 名称升序,false = 降序。
    sort_ascending: std::cell::Cell<bool>,
    /// 右键命令回调(目标 + 动作 id + 上下文相对路径)。
    on_context_command: RefCell<ContextCommandHandler>,
    /// 树内拖拽移动回调(旧相对路径, 目标目录相对路径)。
    on_move_entry: RefCell<Box<dyn Fn(&str, &str)>>,
}

use std::cell::RefCell;

#[allow(deprecated)]
impl Sidebar {
    pub fn new() -> Rc<Self> {
        let stack = Stack::new();

        // ---- 工作区页:搜索框 + 树 ----
        let search_entry = SearchEntry::new();
        // 列:0 显示名,1 相对路径,2 图标名,3 字重(选中/标题层级视觉,对齐桌面)。
        let tree_store = gtk::TreeStore::new(&[
            glib::Type::STRING,
            glib::Type::STRING,
            glib::Type::STRING,
            glib::Type::I32,
        ]);
        let tree_view = gtk::TreeView::with_model(&tree_store);
        tree_view.set_headers_visible(false);
        append_sidebar_column(&tree_view, 0, 2, 3);
        tree_view.add_css_class("markleaf-tree");
        install_sidebar_css();

        let workspace_page = gtk::Box::new(Orientation::Vertical, 4);
        workspace_page.append(&search_entry);
        let tree_scroll = ScrolledWindow::new();
        tree_scroll.set_vexpand(true);
        tree_scroll.set_child(Some(&tree_view));
        workspace_page.append(&tree_scroll);

        // ---- 大纲页(列:显示文本 / position / 标题文本,后两者不渲染) ----
        // 列:0 显示文本,1 position,2 标题文本,3 字重(一二级标题加粗)。
        let outline_store = gtk::TreeStore::new(&[
            glib::Type::STRING,
            glib::Type::STRING,
            glib::Type::STRING,
            glib::Type::I32,
        ]);
        let outline_view = gtk::TreeView::with_model(&outline_store);
        outline_view.set_headers_visible(false);
        append_sidebar_column(&outline_view, 0, -1, 3);
        outline_view.add_css_class("markleaf-tree");
        install_sidebar_css();
        let outline_filter_entry = SearchEntry::new();
        outline_filter_entry.set_placeholder_text(Some("过滤标题"));
        let outline_scroll = ScrolledWindow::new();
        outline_scroll.set_vexpand(true);
        outline_scroll.set_child(Some(&outline_view));
        let outline_page = gtk::Box::new(Orientation::Vertical, 4);
        outline_page.append(&outline_filter_entry);
        outline_page.append(&outline_scroll);

        stack.add_titled(&workspace_page, Some("workspace"), "工作区");
        stack.add_titled(&outline_page, Some("outline"), "大纲");

        // 头部:StackSwitcher(工作区/大纲)+ 新建按钮,对齐 macOS 侧栏头部。
        let switcher = gtk::StackSwitcher::new();
        switcher.set_stack(Some(&stack));
        switcher.set_halign(gtk::Align::Fill);
        switcher.set_hexpand(true);

        let new_file_button = gtk::Button::from_icon_name("list-add-symbolic");
        new_file_button.set_tooltip_text(Some("新建 Markdown 文档"));
        new_file_button.add_css_class("flat");

        let header = gtk::Box::new(Orientation::Horizontal, 8);
        header.append(&switcher);
        header.append(&new_file_button);

        // 空态页:暂未打开工作区(含打开文件夹按钮)。
        let empty_state = gtk::Box::new(Orientation::Vertical, 10);
        empty_state.set_valign(gtk::Align::Center);
        empty_state.set_halign(gtk::Align::Center);
        let empty_label = gtk::Label::new(Some("暂未打开工作区"));
        let open_folder_button = gtk::Button::with_label("打开文件夹");
        empty_state.append(&empty_label);
        empty_state.append(&open_folder_button);


        // ---- 搜索结果页(有结果时推到前台) ----
        let search_store = gtk::TreeStore::new(&[glib::Type::STRING, glib::Type::STRING]);
        let search_view = gtk::TreeView::with_model(&search_store);
        search_view.set_headers_visible(false);
        append_sidebar_column(&search_view, 0, -1, 3);
        search_view.add_css_class("markleaf-tree");
        install_sidebar_css();
        let search_scroll = ScrolledWindow::new();
        search_scroll.set_vexpand(true);
        search_scroll.set_child(Some(&search_view));
        stack.add_named(&search_scroll, Some("search"));
        stack.add_named(&empty_state, Some("empty"));

        let root = gtk::Box::new(Orientation::Vertical, 6);
        root.set_margin_top(6);
        root.set_margin_start(6);
        root.set_margin_end(6);
        // 固定侧栏宽度:不设的话页签组会把自然宽度撑到约 1/3 窗口(实机验收反馈)。
        root.set_size_request(280, -1);
        root.set_hexpand(false);
        root.append(&header);
        root.append(&stack);

        let this = Rc::new(Self {
            root,
            stack,
            tree_store,
            tree_view,
            search_entry,
            search_store,
            search_view: search_view.clone(),
            outline_store,
            outline_view: outline_view.clone(),
            outline_cache: RefCell::new(Vec::new()),
            outline_filter: RefCell::new(String::new()),
            on_outline_activate: RefCell::new(Box::new(|_, _| {})),
            on_open_path: RefCell::new(Box::new(|_| {})),
            current_workspace_root: RefCell::new(None),
            search_debounce: RefCell::new(None),
            cached_entries: RefCell::new(Vec::new()),
            view_list_mode: std::cell::Cell::new(false),
            sort_ascending: std::cell::Cell::new(true),
            on_context_command: RefCell::new(Box::new(|_, _, _| {})),
            on_move_entry: RefCell::new(Box::new(|_, _| {})),
        });

        this.wire_outline_filter(&outline_filter_entry);
        // 大纲双击 → 跳转(position + 标题文本交给窗口层;源码模式契约由窗口决定)。
        {
            let this_for_outline = Rc::clone(&this);
            let store_for_outline = this.outline_store.clone();
            this.outline_view
                .connect_row_activated(move |_view, path, _column| {
                    let store = store_for_outline.clone();
                    if let Some(iter) = store.iter(path) {
                        let position = store
                            .get_value(&iter, 1)
                            .get::<String>()
                            .ok()
                            .and_then(|s| s.parse::<i64>().ok())
                            .unwrap_or(0);
                        let text = store
                            .get_value(&iter, 2)
                            .get::<String>()
                            .unwrap_or_default();
                        (this_for_outline.on_outline_activate.borrow())(position, text);
                    }
                });
        }

        // 工作区树双击 → 打开。
        {
            let this = this.clone();
            let store_for_activate = this.tree_store.clone();
            let root_for_activate = this.current_workspace_root.clone();
            let open_for_activate = Rc::clone(&this);
            this.tree_view.connect_row_activated(move |_view, path, _column| {
                let store = store_for_activate.clone();
                if let Some(iter) = store.iter(path) {
                    let value = store.get_value(&iter, 1);
                    let relative = value.get::<String>().unwrap_or_default();
                    if let Some(root_dir) = root_for_activate.borrow().clone() {
                        (open_for_activate.on_open_path.borrow())(&root_dir.join(relative));
                    }
                }
            });
        }

        // 新建按钮(tab 行右侧)转发 win.newMarkdown。
        {
            let new_for_click = new_file_button.clone();
            new_file_button.connect_clicked(move |_| {
                let _ = new_for_click.activate_action("win.newMarkdown", None);
            });
        }

        // 空态「打开文件夹」按钮转发 win.openFolder。
        {
            let folder_for_click = open_folder_button.clone();
            open_folder_button.connect_clicked(move |_| {
                let _ = folder_for_click.activate_action("win.openFolder", None);
            });
        }

        // 搜索回车 → 内容搜索 → 结果页。
        {
            let root_for_search = this.current_workspace_root.clone();
            let sidebar_for_search = Rc::clone(&this);
            this.search_entry.connect_activate(move |entry| {
                let query = entry.text().to_string();
                let Some(root_dir) = root_for_search.borrow().clone() else {
                    return;
                };
                let hits = if query.is_empty() {
                    Vec::new()
                } else {
                    search_workspace(&root_dir, &query, SearchMode::Content)
                };
                sidebar_for_search.show_search_results(&hits, &root_dir);
            });
        }

        // 搜索实时化:输入停顿 400ms → 文件名搜索(轻量);清空即回工作区页。
        {
            let entry_for_debounce = this.search_entry.clone();
            let root_for_debounce = this.current_workspace_root.clone();
            let sidebar_for_debounce = Rc::clone(&this);
            this.search_entry.connect_search_changed(move |entry| {
                let query = entry.text().to_string();
                if let Some(old) = sidebar_for_debounce.search_debounce.borrow_mut().take() {
                    old.remove();
                }
                if query.is_empty() {
                    sidebar_for_debounce.search_store.clear();
                    let has_workspace = sidebar_for_debounce.current_workspace_root.borrow().is_some();
                    sidebar_for_debounce.stack.set_visible_child_name(if has_workspace {
                        "workspace"
                    } else {
                        "empty"
                    });
                    return;
                }
                let entry_for_task = entry_for_debounce.clone();
                let root_for_task = root_for_debounce.clone();
                let sidebar_for_task = sidebar_for_debounce.clone();
                let query_for_task = query.clone();
                let timeout = glib::timeout_add_local(
                    std::time::Duration::from_millis(400),
                    move || {
                        // 用户可能又输入了:以当前文本为准。
                        let query = entry_for_task.text().to_string();
                        if query.is_empty() {
                            return glib::ControlFlow::Break;
                        }
                        if let Some(root_dir) = root_for_task.borrow().clone() {
                            let hits = search_workspace(
                                &root_dir,
                                &query_for_task,
                                SearchMode::Name,
                            );
                            sidebar_for_task.show_search_results(&hits, &root_dir);
                        }
                        glib::ControlFlow::Break
                    },
                );
                *sidebar_for_debounce.search_debounce.borrow_mut() = Some(timeout);
            });
        }

        // 工作区右键菜单(button 3):文件夹行/文件行/背景三类(对齐桌面)。
        {
            let this_for_menu = this.clone();
            let gesture = gtk::GestureClick::new();
            gesture.set_button(3);
            let view_for_controller = this_for_menu.tree_view.clone();
            let view_for_signal = view_for_controller.clone();
            gesture.connect_pressed(move |gesture, _n, x, y| {
                gesture.set_state(gtk::EventSequenceState::Claimed);
                let view = view_for_signal.clone();
                let (bin_x, bin_y) = view.convert_widget_to_bin_window_coords(x as i32, y as i32);
                let path = view
                    .path_at_pos(bin_x, bin_y)
                    .and_then(|(path, _column, _cx, _cy)| path);
                let root = this_for_menu.current_workspace_root.borrow().clone();
                let Some(root_dir) = root else { return };
                let target = match path.and_then(|p| {
                    let store = &this_for_menu.tree_store;
                    store
                        .iter(&p)
                        .map(|iter| store.get_value(&iter, 1).get::<String>().unwrap_or_default())
                }) {
                    Some(relative) if relative.is_empty() => ContextTarget::Root,
                    Some(relative) => {
                        let is_folder = this_for_menu
                            .cached_entries
                            .borrow()
                            .iter()
                            .any(|entry| entry.relative == relative && entry.kind == WorkspaceKind::Folder);
                        if is_folder {
                            ContextTarget::Folder(relative)
                        } else {
                            ContextTarget::File(relative)
                        }
                    }
                    None => ContextTarget::Root,
                };
                let menu_box = this_for_menu.build_context_menu(&target);
                let popover = gtk::Popover::new();
                popover.set_has_arrow(false);
                popover.set_pointing_to(Some(&gdk::Rectangle::new(
                    x as i32,
                    y as i32,
                    1,
                    1,
                )));
                popover.set_child(Some(&menu_box));
                popover.set_parent(&view);
                popover.connect_closed(move |popover| {
                    popover.unparent();
                });
                popover.popup();
            });
            view_for_controller.add_controller(gesture);
        }

        // 树内拖拽移动(左键按下拖到目标文件夹/背景松开)。
        {
            let this_for_drag = this.clone();
            let drag = gtk::GestureDrag::new();
            drag.set_button(1);
            let start: Rc<RefCell<Option<(f64, f64, String)>>> = Rc::new(RefCell::new(None));
            let this_for_begin = this_for_drag.clone();
            let start_for_begin = start.clone();
            let this_for_end = this_for_drag.clone();
            drag.connect_drag_begin(move |gesture, x, y| {
                let view = &this_for_begin.tree_view;
                let (bin_x, bin_y) = view.convert_widget_to_bin_window_coords(x as i32, y as i32);
                let row_path = view.path_at_pos(bin_x, bin_y).and_then(|(path, _c, _x, _y)| path);
                let relative = row_path.and_then(|path| {
                    let store = &this_for_begin.tree_store;
                    store.iter(&path).map(|iter| {
                        store.get_value(&iter, 1).get::<String>().unwrap_or_default()
                    })
                });
                let mut start_cell = start_for_begin.borrow_mut();
                match relative {
                    Some(relative) if !relative.is_empty() => {
                        *start_cell = Some((x, y, relative));
                    }
                    _ => *start_cell = None,
                }
            });
            drag.connect_drag_end(move |gesture, _x_drag, _y_drag| {
                let Some((start_x, start_y, source_relative)) = start.borrow_mut().take() else {
                    return;
                };
                let (end_x, end_y) = gesture.offset().unwrap_or((start_x, start_y));
                let (end_x, end_y) = (start_x + end_x, start_y + end_y);
                let view = &this_for_end.tree_view;
                let (bin_x, bin_y) = view.convert_widget_to_bin_window_coords(end_x as i32, end_y as i32);
                let row_path = view.path_at_pos(bin_x, bin_y).and_then(|(path, _c, _x, _y)| path);
                // 落点:文件夹行 = 其下;文件行/背景 = 其所在目录(默认根)。
                let target_dir = row_path.and_then(|path| {
                    let store = &this_for_end.tree_store;
                    store.iter(&path).map(|iter| {
                        store.get_value(&iter, 1).get::<String>().unwrap_or_default()
                    })
                });
                let name = source_relative.rsplit('/').next().unwrap_or(&source_relative);
                let target_dir = match target_dir {
                    Some(mut hit) => {
                        let is_folder = this_for_end
                            .cached_entries
                            .borrow()
                            .iter()
                            .any(|entry| entry.relative == hit && entry.kind == WorkspaceKind::Folder);
                        if is_folder {
                            if source_relative == hit
                                || hit.starts_with(&format!("{source_relative}/"))
                            {
                                return; // 拖进自身或自身子目录:忽略。
                            }
                            if let Some(parent) = source_relative.rsplit_once('/') {
                                if parent.0 == hit {
                                    return; // 同目录:忽略。
                                }
                            }
                            hit
                        } else {
                            match hit.rsplit_once('/') {
                                Some((parent, _)) => parent.to_string(),
                                None => String::new(),
                            }
                        }
                    }
                    None => String::new(),
                };
                let same_dir = source_relative.rsplit_once('/').map(|(parent, _)| parent).unwrap_or("")
                    == target_dir;
                if same_dir || source_relative == target_dir {
                    return;
                }
                (this_for_end.on_move_entry.borrow())(&source_relative, &target_dir);
                let _ = name;
            });
            this_for_drag.tree_view.add_controller(drag);
        }

        // 外部文件拖入(DropTarget):.md/.txt 拷贝到工作区根。
        {
            let this_for_drop = this.clone();
            let this_for_drop_signal = this_for_drop.clone();
            let drop_target = gtk::DropTarget::new(gdk::FileList::static_type(), gdk::DragAction::COPY);
            drop_target.connect_drop(move |drop_target, value, _x, _y| {
                let Ok(file_list) = value.get::<gdk::FileList>() else {
                    return false;
                };
                let Some(root_dir) = this_for_drop_signal.current_workspace_root.borrow().clone() else {
                    return false;
                };
                let mut copied = 0usize;
                for file in file_list.files() {
                    let Some(path) = file.path() else { continue };
                    let ext = path
                        .extension()
                        .map(|e| e.to_string_lossy().to_lowercase())
                        .unwrap_or_default();
                    if ext != "md" && ext != "markdown" && ext != "txt" {
                        continue;
                    }
                    let name = path
                        .file_name()
                        .map(|n| n.to_string_lossy().into_owned())
                        .unwrap_or_else(|| format!("import.{ext}"));
                    let target = crate::workspace::mutation::unique_path(&root_dir.join(&name));
                    if std::fs::copy(&path, &target).is_ok() {
                        copied += 1;
                    }
                }
                if copied > 0 {
                    sidebar_refresh_signal(&this_for_drop_signal);
                    return true;
                }
                false
            });
            this_for_drop.tree_view.add_controller(drop_target);
        }

        // 搜索结果双击 → 打开命中文件。
        {
            let store_for_hit = this.search_store.clone();
            let open_for_hit = Rc::clone(&this);
            this.search_view.connect_row_activated(move |_view, path, _column| {
                let store = store_for_hit.clone();
                if let Some(iter) = store.iter(path) {
                    let value = store.get_value(&iter, 1);
                    let file = value.get::<String>().unwrap_or_default();
                    if !file.is_empty() {
                        (open_for_hit.on_open_path.borrow())(&PathBuf::from(&file));
                    }
                }
            });
        }

        this
    }

    /// 注入打开回调(窗口层)。
    pub fn set_open_handler(&self, handler: impl Fn(&PathBuf) + 'static) {
        *self.on_open_path.borrow_mut() = Box::new(handler);
    }

    /// 注入大纲激活回调(窗口层;参数 = ProseMirror position, 标题文本)。
    pub fn set_outline_handler(&self, handler: impl Fn(i64, String) + 'static) {
        *self.on_outline_activate.borrow_mut() = Box::new(handler);
    }

    /// 注入工作区根(设置后扫描;None = 无工作区)。
    pub fn set_workspace_root(self: &Rc<Self>, root: Option<PathBuf>) {
        *self.current_workspace_root.borrow_mut() = root.clone();
        self.tree_store.clear();
        // 搜索框可用性联动(macOS syncSearchAvailability)。
        self.search_entry.set_sensitive(root.is_some());
        let Some(root_dir) = root else {
            *self.cached_entries.borrow_mut() = Vec::new();
            self.stack.set_visible_child_name("empty");
            return;
        };
        let mut entries = scan_workspace(&root_dir);
        self.apply_sort(&mut entries);
        *self.cached_entries.borrow_mut() = entries.clone();
        self.rebuild_store(&entries);
        self.stack.set_visible_child_name("workspace");
    }

    /// 按排序方向翻转条目顺序(目录始终在前)。
    fn apply_sort(&self, entries: &mut [WorkspaceEntry]) {
        let ascending = self.sort_ascending.get();
        entries.sort_by(|a, b| {
            let folder_a = a.kind == WorkspaceKind::Folder;
            let folder_b = b.kind == WorkspaceKind::Folder;
            folder_b
                .cmp(&folder_a)
                .then_with(|| {
                    if ascending {
                        a.relative.cmp(&b.relative)
                    } else {
                        b.relative.cmp(&a.relative)
                    }
                })
        });
    }

    /// 依当前视图模式重建树内容(树状 = 层级;列表 = 扁平卡片)。
    fn rebuild_store(&self, entries: &[WorkspaceEntry]) {
        self.tree_store.clear();
        if self.view_list_mode.get() {
            for entry in entries {
                if entry.kind == WorkspaceKind::Folder {
                    continue;
                }
                let directory = entry
                    .relative
                    .rsplit_once('/')
                    .map(|(head, _)| head.to_string())
                    .unwrap_or_else(|| "·".into());
                let time_text = if entry.modified > 0 {
                    glib::DateTime::from_unix_local(entry.modified)
                        .map(|d| {
                            d.format("%Y-%m-%d %H:%M")
                                .map(|s| s.to_string())
                                .unwrap_or_default()
                        })
                        .unwrap_or_default()
                } else {
                    "—".into()
                };
                let preview = entry.preview.clone();
                let markup = format!(
                    "<b>{}</b>\n<small>{directory} · {time_text}</small>\n<small>{preview}</small>",
                    entry.display_name()
                );
                let iter = self.tree_store.append(None);
                self.tree_store.set_value(&iter, 0, &markup.to_value());
                self.tree_store.set_value(&iter, 1, &entry.relative.to_value());
                self.tree_store.set_value(&iter, 3, &400.to_value());
            }
            return;
        }
        let mut parent_iters: Vec<(String, gtk::TreeIter)> = Vec::new();
        for entry in entries {
            let depth = entry.relative.matches('/').count();
            let parent = if depth == 0 {
                None
            } else {
                let prefix = entry
                    .relative
                    .rsplit_once('/')
                    .map(|(head, _)| head.to_string());
                prefix.and_then(|head| {
                    parent_iters.iter().find(|(path, _)| *path == head).map(|(_, iter)| *iter)
                })
            };
            let iter = self.tree_store.append(parent.as_ref());
            let escaped = glib::markup_escape_text(entry.display_name());
            self.tree_store.set_value(&iter, 0, &escaped.to_value());
            self.tree_store.set_value(&iter, 1, &entry.relative.to_value());
            self.tree_store.set_value(
                &iter,
                2,
                &sidebar_icon_name(entry.kind == WorkspaceKind::Folder).to_value(),
            );
            self.tree_store.set_value(&iter, 3, &400.to_value());
            if entry.kind == WorkspaceKind::Folder {
                parent_iters.push((entry.relative.clone(), iter));
            }
        }
    }

    /// 大纲刷新(outlineChanged 消息驱动)。position/标题文本存隐藏列,
    /// 供激活回调发起跳转(可视化 scrollToPosition / 源码 scrollToSourceHeading)。
    pub fn set_outline(&self, items: &[serde_json::Value]) {
        let mut cache: Vec<(String, i64, String, i32)> = Vec::new();
        for item in items {
            let text = item.get("text").and_then(|v| v.as_str()).unwrap_or("");
            let level = item.get("level").and_then(|v| v.as_i64()).unwrap_or(1);
            let position = item.get("position").and_then(|v| v.as_i64()).unwrap_or(0);
            let indent = "  ".repeat(level.saturating_sub(1) as usize);
            cache.push((
                format!("{indent}{}", glib::markup_escape_text(text)),
                position,
                text.to_string(),
                outline_weight(level),
            ));
        }
        *self.outline_cache.borrow_mut() = cache;
        self.rebuild_outline();
        if !items.is_empty() {
            self.stack.set_visible_child_name("outline");
        }
    }

    /// 按过滤文本重建大纲(小写子串匹配标题)。
    fn rebuild_outline(&self) {
        let filter = self.outline_filter.borrow().to_lowercase();
        self.outline_store.clear();
        for (display, position, text, weight) in self.outline_cache.borrow().iter() {
            if !filter.is_empty() && !text.to_lowercase().contains(&filter) {
                continue;
            }
            let iter = self.outline_store.append(None);
            self.outline_store.set_value(&iter, 0, &display.to_value());
            self.outline_store.set_value(&iter, 1, &position.to_string().to_value());
            self.outline_store.set_value(&iter, 2, &text.to_value());
            self.outline_store.set_value(&iter, 3, &(*weight).to_value());
        }
    }

    /// 接线大纲过滤输入框(实时过滤)。
    pub fn wire_outline_filter(self: &Rc<Self>, entry: &SearchEntry) {
        let sidebar = Rc::clone(self);
        entry.connect_search_changed(move |entry| {
            *sidebar.outline_filter.borrow_mut() = entry.text().to_lowercase();
            sidebar.rebuild_outline();
        });
    }

    /// 大纲选中同步(outlineSelectionChanged 驱动):光标所在 position 向上
    /// 就近选中标题,并把该行滚动到可视区。
    pub fn highlight_outline_position(&self, position: i64) {
        let store = &self.outline_store;
        let mut best: Option<gtk::TreeIter> = None;
        let mut best_position: i64 = i64::MIN;
        let mut iter = store.iter_first();
        while let Some(mut current) = iter {
            let row_position = store
                .get_value(&current, 1)
                .get::<String>()
                .ok()
                .and_then(|s| s.parse::<i64>().ok())
                .unwrap_or(0);
            if row_position <= position && row_position >= best_position {
                best_position = row_position;
                best = Some(current.clone());
            }
            if !store.iter_next(&mut current) {
                break;
            }
        }
        if let Some(target) = best {
            let selection = self.outline_view.selection();
            selection.unselect_all();
            let path = store.path(&target);
            self.outline_view
                .scroll_to_cell(Some(&path), None, false, 0.0, 0.0);
            selection.select_path(&path);
        }
    }

    /// 打开文档时在工作区树中定位并选中(对齐 macOS revealWorkspacePath)。
    pub fn reveal_workspace_path(&self, path: &std::path::Path) {
        let Some(root) = self.current_workspace_root.borrow().clone() else {
            return;
        };
        let Ok(relative) = path.strip_prefix(&root) else {
            return;
        };
        let relative = relative.to_string_lossy().into_owned();
        let store = &self.tree_store;
        let mut iter = store.iter_first();
        while let Some(mut current) = iter {
            let row_relative = store
                .get_value(&current, 1)
                .get::<String>()
                .unwrap_or_default();
            if row_relative == relative {
                // 展开祖先链,选中并滚动到该行。
                let tree_path = store.path(&current);
                self.tree_view.expand_to_path(&tree_path);
                let selection = self.tree_view.selection();
                selection.unselect_all();
                self.tree_view
                    .scroll_to_cell(Some(&tree_path), None, false, 0.0, 0.0);
                selection.select_path(&tree_path);
                self.stack.set_visible_child_name("workspace");
                return;
            }
            if !store.iter_next(&mut current) {
                break;
            }
        }
    }


    /// 右键菜单(对齐桌面三类结构);视图/排序类命令 sidebar 自消化,
    /// 其余经 on_context_command 回调交窗口层(对话框/文件系统/剪贴板)。
    fn build_context_menu(self: &Rc<Self>, target: &ContextTarget) -> gtk::Box {
        let list = gtk::Box::new(Orientation::Vertical, 0);
        list.set_margin_top(6);
        list.set_margin_bottom(6);
        list.set_margin_start(4);
        list.set_margin_end(4);
        let root_dir = self.current_workspace_root.borrow().clone();

        let add_item = |menu_box: &gtk::Box, label: &str, action: &str, context_relative: &str| {
            let item = gtk::Button::builder().label(label).has_frame(false).build();
            item.set_halign(gtk::Align::Fill);
            let menu_box_weak = menu_box.downgrade();
            let sidebar = self.clone();
            let action = action.to_string();
            let context_relative = context_relative.to_string();
            let target = target.clone();
            item.connect_clicked(move |_| {
                if let Some(menu_box) = menu_box_weak.upgrade() {
                    if let Some(popover) = menu_box.parent().and_then(|parent| parent.downcast::<gtk::Popover>().ok()) {
                        popover.popdown();
                    }
                }
                // 视图/排序类:sidebar 自消化。
                match action.as_str() {
                    "switchTree" => {
                        sidebar.view_list_mode.set(false);
                        sidebar.refresh_entries();
                    }
                    "switchList" => {
                        sidebar.view_list_mode.set(true);
                        sidebar.refresh_entries();
                    }
                    "sortAsc" => {
                        sidebar.sort_ascending.set(true);
                        sidebar.refresh_entries();
                    }
                    "sortDesc" => {
                        sidebar.sort_ascending.set(false);
                        sidebar.refresh_entries();
                    }
                    _ => {
                        (sidebar.on_context_command.borrow())(&target, &action, &context_relative);
                    }
                }
            });
            menu_box.append(&item);
        };

        match target {
            ContextTarget::Folder(relative) => {
                add_item(&list, "新建 Markdown 文档", "newMarkdown", relative);
                add_item(&list, "新建纯文本文件", "newPlainText", relative);
                add_item(&list, "新建文件夹", "newFolder", relative);
                list.append(&gtk::Separator::new(Orientation::Horizontal));
                add_item(&list, "复制文件夹路径", "copyPath", relative);
                add_item(&list, "重命名…", "rename", relative);
                add_item(&list, "删除", "delete", relative);
            }
            ContextTarget::File(relative) => {
                add_item(&list, "打开", "open", relative);
                add_item(&list, "复制文件路径", "copyPath", relative);
                add_item(&list, "复制文件内容", "copyContent", relative);
                list.append(&gtk::Separator::new(Orientation::Horizontal));
                add_item(&list, "重命名…", "rename", relative);
                add_item(&list, "删除", "delete", relative);
            }
            ContextTarget::Root => {
                add_item(&list, "新建 Markdown 文档", "newMarkdown", "");
                add_item(&list, "新建纯文本文件", "newPlainText", "");
                add_item(&list, "新建文件夹…", "newFolder", "");
                list.append(&gtk::Separator::new(Orientation::Horizontal));
                add_item(&list, "树状视图", "switchTree", "");
                add_item(&list, "列表视图", "switchList", "");
                list.append(&gtk::Separator::new(Orientation::Horizontal));
                add_item(&list, "按名称升序", "sortAsc", "");
                add_item(&list, "按名称降序", "sortDesc", "");
                list.append(&gtk::Separator::new(Orientation::Horizontal));
                add_item(&list, "刷新工作区", "refresh", "");
                if root_dir.is_some() {
                    add_item(&list, "关闭工作区", "closeWorkspace", "");
                }
            }
        }
        list
    }

    /// 重扫当前工作区并按视图/排序重建(右键视图/排序/刷新命令的落点)。
    fn refresh_entries(self: &Rc<Self>) {
        let Some(root_dir) = self.current_workspace_root.borrow().clone() else {
            return;
        };
        let mut entries = scan_workspace(&root_dir);
        self.apply_sort(&mut entries);
        *self.cached_entries.borrow_mut() = entries.clone();
        self.rebuild_store(&entries);
    }

    /// 注入树内拖拽移动回调(窗口层执行文件系统移动与标签同步)。
    pub fn set_move_entry_handler(&self, handler: impl Fn(&str, &str) + 'static) {
        *self.on_move_entry.borrow_mut() = Box::new(handler);
    }

    /// 注入右键命令回调(窗口层)。
    pub fn set_context_command_handler(
        &self,
        handler: impl Fn(&ContextTarget, &str, &str) + 'static,
    ) {
        *self.on_context_command.borrow_mut() = Box::new(handler);
    }

    fn show_search_results(&self, hits: &[SearchHit], root_dir: &std::path::Path) {
        self.search_store.clear();
        for hit in hits {
            let relative = hit
                .path
                .strip_prefix(root_dir)
                .map(|p| p.to_string_lossy().into_owned())
                .unwrap_or_else(|_| hit.path.to_string_lossy().into_owned());
            let iter = self.search_store.append(None);
            let label = format!("{relative}:{} · {}", hit.line, hit.excerpt);
            self.search_store.set_value(&iter, 0, &label.to_value());
            self.search_store.set_value(&iter, 1, &hit.path.to_string_lossy().to_value());
        }
        self.stack.set_visible_child_name("search");
    }
}

/// 侧栏列:可选图标 + 文本(行高 26 对齐桌面),文本字重随 weight 列。
/// 刷新信号:委托右键命令回调走窗口层 refresh(refresh 动作已在窗口实现)。
fn sidebar_refresh_signal(sidebar: &Rc<Sidebar>) {
    (sidebar.on_context_command.borrow())(
        &ContextTarget::Root,
        "refresh",
        "",
    );
}

/// 工作区根下唯一化文件名(复用 mutation::unique_path)。
fn unique_in_dir(root: &Path, name: &str) -> PathBuf {
    crate::workspace::mutation::unique_path(&root.join(name))
}

#[allow(deprecated)]
fn append_sidebar_column(
    view: &gtk::TreeView,
    text_col: i32,
    icon_col: i32,
    weight_col: i32,
) {
    let column = gtk::TreeViewColumn::new();
    if icon_col >= 0 {
        let icon = gtk::CellRendererPixbuf::new();
        icon.set_padding(2, 0);
        icon.set_icon_name(Some("text-x-generic-symbolic"));
        column.pack_start(&icon, false);
        column.add_attribute(&icon, "icon-name", icon_col);
    }
    let text = gtk::CellRendererText::new();
    text.set_ypad(5);
    column.pack_start(&text, true);
    column.add_attribute(&text, "markup", text_col);
    column.add_attribute(&text, "weight", weight_col);
    view.append_column(&column);
}

/// 侧栏条目图标(文件夹/文档)。
fn sidebar_icon_name(is_folder: bool) -> &'static str {
    if is_folder {
        "folder-symbolic"
    } else {
        "text-x-generic-symbolic"
    }
}

/// 大纲标题字重:一二级 semibold,其余 regular(对齐桌面层级视觉)。
fn outline_weight(level: i64) -> i32 {
    if level <= 2 {
        600
    } else {
        400
    }
}

/// 侧栏视觉:透明背景(融入侧栏底色)、行文字 13px 对齐桌面行高与留白。
/// 幂等:provider 只挂一次。
fn install_sidebar_css() {
    use std::sync::Once;
    static ONCE: Once = Once::new();
    ONCE.call_once(|| {
        let css = "
            .markleaf-tree { background: transparent; }
            .markleaf-tree treeview.view { background: transparent; }
            .markleaf-tree treeview.view:selected { font-weight: 700; }
        ";
        let provider = gtk::CssProvider::new();
        provider.load_from_data(css);
        gtk::style_context_add_provider_for_display(
            &gtk::gdk::Display::default().expect("display"),
            &provider,
            gtk::STYLE_PROVIDER_PRIORITY_APPLICATION,
        );
    });
}
