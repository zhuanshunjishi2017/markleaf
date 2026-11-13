// 工作区枚举与增量 diff:仅 .md/.txt 与文件夹(与 README 承诺一致),
// 忽略隐藏条目;排序:目录在前、名称字典序(侧栏树/列表数据源)。
use std::fs;
use std::path::Path;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WorkspaceKind {
    Markdown,
    PlainText,
    Folder,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WorkspaceEntry {
    /// 相对工作区根的 POSIX 风格路径(如 sub/d.md)。
    pub relative: String,
    pub kind: WorkspaceKind,
}

impl WorkspaceEntry {
    pub fn display_name(&self) -> &str {
        self.relative.rsplit('/').next().unwrap_or(&self.relative)
    }
}

#[derive(Debug, Default, Clone, PartialEq)]
pub struct WorkspaceDelta {
    pub added: Vec<WorkspaceEntry>,
    pub removed: Vec<WorkspaceEntry>,
}

fn is_markdown(name: &str) -> bool {
    name.to_ascii_lowercase().ends_with(".md")
}

fn is_plain_text(name: &str) -> bool {
    name.to_ascii_lowercase().ends_with(".txt")
}

fn scan_dir(root: &Path, relative_prefix: &str, out: &mut Vec<WorkspaceEntry>) {
    let Ok(read) = fs::read_dir(root) else {
        return;
    };
    let mut children: Vec<_> = read.flatten().collect();
    children.sort_by_key(|item| item.file_name());
    for item in children {
        let Ok(file_type) = item.file_type() else {
            continue;
        };
        let name = item.file_name().to_string_lossy().into_owned();
        if name.starts_with('.') {
            continue;
        }
        let relative = if relative_prefix.is_empty() {
            name.clone()
        } else {
            format!("{relative_prefix}/{name}")
        };
        if file_type.is_dir() {
            out.push(WorkspaceEntry {
                relative: relative.clone(),
                kind: WorkspaceKind::Folder,
            });
            scan_dir(&item.path(), &relative, out);
        } else if is_markdown(&name) {
            out.push(WorkspaceEntry {
                relative,
                kind: WorkspaceKind::Markdown,
            });
        } else if is_plain_text(&name) {
            out.push(WorkspaceEntry {
                relative,
                kind: WorkspaceKind::PlainText,
            });
        }
    }
}

/// 全量扫描(侧栏刷新数据源;文件数在产品语义内为中小规模,直接递归)。
pub fn scan_workspace(root: &Path) -> Vec<WorkspaceEntry> {
    let mut entries = Vec::new();
    scan_dir(root, "", &mut entries);
    // 排序:目录在前,其余名称字典序(与桌面侧栏一致)。
    entries.sort_by(|a, b| {
        let folder_a = a.kind == WorkspaceKind::Folder;
        let folder_b = b.kind == WorkspaceKind::Folder;
        folder_b.cmp(&folder_a).then_with(|| a.relative.cmp(&b.relative))
    });
    entries
}

/// 增量 diff(监视器防抖后刷新用;展开/选中/滚动状态由 GTK 层保留)。
pub fn diff_entries(before: &[WorkspaceEntry], after: &[WorkspaceEntry]) -> WorkspaceDelta {
    let mut added = Vec::new();
    let mut removed = Vec::new();
    for entry in after {
        if !before.contains(entry) {
            added.push(entry.clone());
        }
    }
    for entry in before {
        if !after.contains(entry) {
            removed.push(entry.clone());
        }
    }
    WorkspaceDelta { added, removed }
}
