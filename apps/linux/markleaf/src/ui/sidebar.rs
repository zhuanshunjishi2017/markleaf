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
    /// 大纲列表。
    outline_store: gtk::TreeStore,
    /// 双击打开回调(窗口注入)。
    on_open_path: RefCell<OpenPathHandler>,
    /// 当前工作区根(扫描/搜索基准;None = 无工作区)。
    current_workspace_root: RefCell<Option<PathBuf>>,
}

use std::cell::RefCell;

#[allow(deprecated)]
impl Sidebar {
    pub fn new() -> Rc<Self> {
        let stack = Stack::new();

        // ---- 工作区页:搜索框 + 树 ----
        let search_entry = SearchEntry::new();
        let tree_store = gtk::TreeStore::new(&[glib::Type::STRING, glib::Type::STRING]);
        let tree_view = gtk::TreeView::with_model(&tree_store);
        tree_view.set_headers_visible(false);
        append_text_column(&tree_view, 0);

        let workspace_page = gtk::Box::new(Orientation::Vertical, 4);
        workspace_page.append(&search_entry);
        let tree_scroll = ScrolledWindow::new();
        tree_scroll.set_vexpand(true);
        tree_scroll.set_child(Some(&tree_view));
        workspace_page.append(&tree_scroll);

        // ---- 大纲页 ----
        let outline_store = gtk::TreeStore::new(&[glib::Type::STRING, glib::Type::STRING]);
        let outline_view = gtk::TreeView::with_model(&outline_store);
        outline_view.set_headers_visible(false);
        append_text_column(&outline_view, 0);
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
        append_text_column(&search_view, 0);
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
        root.append(&switcher);
        root.append(&stack);

        let this = Rc::new(Self {
            root,
            stack,
            tree_store,
            tree_view,
            search_entry,
            search_store,
            outline_store,
            on_open_path: RefCell::new(Box::new(|_| {})),
            current_workspace_root: RefCell::new(None),
        });

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

        this
    }

    /// 注入打开回调(窗口层)。
    pub fn set_open_handler(&self, handler: impl Fn(&PathBuf) + 'static) {
        *self.on_open_path.borrow_mut() = Box::new(handler);
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
            if entry.kind == crate::workspace::model::WorkspaceKind::Folder {
                parent_iters.push((entry.relative, iter));
            }
        }
        self.stack.set_visible_child_name("workspace");
    }

    /// 大纲刷新(outlineChanged 消息驱动)。
    pub fn set_outline(&self, items: &[serde_json::Value]) {
        self.outline_store.clear();
        for item in items {
            let text = item.get("text").and_then(|v| v.as_str()).unwrap_or("");
            let level = item.get("level").and_then(|v| v.as_i64()).unwrap_or(1);
            let iter = self.outline_store.append(None);
            self.outline_store.set_value(&iter, 0, &format!("{}{}", "  ".repeat(level.saturating_sub(1) as usize), text).to_value());
            self.outline_store.set_value(&iter, 1, &text.to_value());
        }
        if !items.is_empty() {
            self.stack.set_visible_child_name("outline");
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

#[allow(deprecated)]
fn append_text_column(view: &gtk::TreeView, column_id: i32) {
    let cell = gtk::CellRendererText::new();
    let column = gtk::TreeViewColumn::new();
    column.pack_start(&cell, true);
    column.add_attribute(&cell, "text", column_id);
    view.append_column(&column);
}
