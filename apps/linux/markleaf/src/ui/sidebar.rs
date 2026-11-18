// 侧栏:工作区树(ListView)+ 搜索面板 + 大纲。
// 数据源:workspace::model 扫描 / outlineChanged 消息;双击打开文档。
use std::path::PathBuf;
use std::rc::Rc;

use gtk::glib;
use gtk::prelude::*;
use gtk::{Orientation, ScrolledWindow, SearchEntry, Stack};
#[allow(deprecated)]
use gtk::prelude::TreeModelExtManual;

use crate::workspace::model::{scan_workspace, WorkspaceEntry};
use crate::workspace::search::{search_workspace, SearchMode, SearchHit};

// TreeView/TreeStore 在 GTK4 已 deprecated(4.10 起)但仍完整可用;
// M5 打磨迁 ColumnView/ListView(台账已记,当前优先功能闭环)。
#[allow(deprecated)]
type OpenPathHandler = Box<dyn Fn(&PathBuf)>;

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
    /// 大纲条目激活回调(窗口注入;参数 = position, 标题文本)。
    on_outline_activate: RefCell<Box<dyn Fn(i64, String)>>,
    /// 双击打开回调(窗口注入)。
    on_open_path: RefCell<OpenPathHandler>,
    /// 当前工作区根(扫描/搜索基准;None = 无工作区)。
    current_workspace_root: RefCell<Option<PathBuf>>,
    /// 实时搜索防抖定时器(输入停顿后触发文件名搜索)。
    search_debounce: RefCell<Option<glib::SourceId>>,
}

use std::cell::RefCell;

#[allow(deprecated)]
impl Sidebar {
    pub fn new() -> Rc<Self> {
        let stack = Stack::new();

        // ---- 工作区页:头部快捷按钮 + 搜索框 + 树 ----
        let header_box = gtk::Box::new(Orientation::Horizontal, 4);
        let new_file_button = gtk::Button::from_icon_name("document-new-symbolic");
        new_file_button.set_tooltip_text(Some("新建 Markdown 文档"));
        let open_folder_button = gtk::Button::from_icon_name("folder-open-symbolic");
        open_folder_button.set_tooltip_text(Some("打开文件夹"));
        header_box.append(&new_file_button);
        header_box.append(&open_folder_button);
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
        workspace_page.append(&header_box);
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
        let outline_scroll = ScrolledWindow::new();
        outline_scroll.set_vexpand(true);
        outline_scroll.set_child(Some(&outline_view));
        let outline_page = gtk::Box::new(Orientation::Vertical, 0);
        outline_page.append(&outline_scroll);

        stack.add_titled(&workspace_page, Some("workspace"), "工作区");
        stack.add_titled(&outline_page, Some("outline"), "大纲");

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
        stack.add_titled(&search_scroll, Some("search"), "搜索结果");

        let switcher = gtk::StackSwitcher::new();
        switcher.set_stack(Some(&stack));

        let root = gtk::Box::new(Orientation::Vertical, 6);
        root.set_margin_top(6);
        root.set_margin_start(6);
        root.set_margin_end(6);
        // 固定侧栏宽度:不设的话页签组会把自然宽度撑到约 1/3 窗口(实机验收反馈)。
        root.set_size_request(280, -1);
        root.set_hexpand(false);
        root.append(&switcher);
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
            on_outline_activate: RefCell::new(Box::new(|_, _| {})),
            on_open_path: RefCell::new(Box::new(|_| {})),
            current_workspace_root: RefCell::new(None),
            search_debounce: RefCell::new(None),
        });

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

        // 头部按钮:转发窗口的 win.* action(新建文档守卫/打开文件夹对话框在窗口层)。
        {
            let new_for_click = new_file_button.clone();
            new_file_button.connect_clicked(move |_| {
                let _ = new_for_click.activate_action("win.newMarkdown", None);
            });
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
                    sidebar_for_debounce.stack.set_visible_child_name("workspace");
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
        let Some(root_dir) = root else {
            self.stack.set_visible_child_name("workspace");
            return;
        };
        let entries: Vec<WorkspaceEntry> = scan_workspace(&root_dir);
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
            self.tree_store.set_value(&iter, 0, &entry.display_name().to_value());
            self.tree_store.set_value(&iter, 1, &entry.relative.to_value());
            self.tree_store.set_value(
                &iter,
                2,
                &sidebar_icon_name(entry.kind == crate::workspace::model::WorkspaceKind::Folder)
                    .to_value(),
            );
            self.tree_store.set_value(&iter, 3, &400.to_value());
            if entry.kind == crate::workspace::model::WorkspaceKind::Folder {
                parent_iters.push((entry.relative, iter));
            }
        }
        self.stack.set_visible_child_name("workspace");
    }

    /// 大纲刷新(outlineChanged 消息驱动)。position/标题文本存隐藏列,
    /// 供激活回调发起跳转(可视化 scrollToPosition / 源码 scrollToSourceHeading)。
    pub fn set_outline(&self, items: &[serde_json::Value]) {
        self.outline_store.clear();
        for item in items {
            let text = item.get("text").and_then(|v| v.as_str()).unwrap_or("");
            let level = item.get("level").and_then(|v| v.as_i64()).unwrap_or(1);
            let position = item.get("position").and_then(|v| v.as_i64()).unwrap_or(0);
            let iter = self.outline_store.append(None);
            self.outline_store.set_value(&iter, 0, &format!("{}{}", "  ".repeat(level.saturating_sub(1) as usize), text).to_value());
            self.outline_store.set_value(&iter, 1, &position.to_string().to_value());
            self.outline_store.set_value(&iter, 2, &text.to_value());
            self.outline_store
                .set_value(&iter, 3, &outline_weight(level).to_value());
        }
        if !items.is_empty() {
            self.stack.set_visible_child_name("outline");
        }
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
    column.add_attribute(&text, "text", text_col);
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
