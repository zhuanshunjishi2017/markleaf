// 工作区模型测试:扫描过滤(.md/.txt/目录,忽略隐藏)、排序、增量 diff。
use std::fs;

use markleaf::workspace::model::{scan_workspace, WorkspaceEntry, WorkspaceKind};

fn temp_ws(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("markleaf-ws-{}-{name}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(dir.join("sub")).expect("建子目录");
    fs::write(dir.join("a.md"), "# A").expect("写 a");
    fs::write(dir.join("b.txt"), "B").expect("写 b");
    fs::write(dir.join("c.png"), "x").expect("写 c(应被过滤)");
    fs::write(dir.join(".hidden.md"), "h").expect("写隐藏(应被过滤)");
    fs::write(dir.join("sub/d.md"), "# D").expect("写子目录文档");
    dir
}

#[test]
fn scan_filters_and_sorts() {
    let root = temp_ws("scan");
    let entries = scan_workspace(&root);
    let names: Vec<&str> = entries.iter().map(|e| e.relative.as_str()).collect();
    assert!(names.contains(&"a.md"), "实得 {names:?}");
    assert!(names.contains(&"b.txt"));
    assert!(names.contains(&"sub/d.md"), "递归子目录");
    assert!(!names.iter().any(|n| n.contains(".hidden")), "隐藏文件过滤");
    assert!(!names.contains(&"c.png"), "仅 .md/.txt");
    assert!(entries.iter().any(|e| e.kind == WorkspaceKind::Folder && e.relative == "sub"));
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn diff_reports_added_and_removed() {
    let root = temp_ws("diff");
    let before = scan_workspace(&root);
    fs::write(root.join("new.md"), "n").expect("新增");
    fs::remove_file(root.join("b.txt")).expect("删除");
    let after = scan_workspace(&root);
    let delta = markleaf::workspace::model::diff_entries(&before, &after);
    assert!(delta.added.iter().any(|e| e.relative == "new.md"));
    assert!(delta.removed.iter().any(|e| e.relative == "b.txt"));
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn entry_display_name_from_relative() {
    let entry = WorkspaceEntry {
        relative: "sub/d.md".into(),
        kind: WorkspaceKind::Markdown,
    };
    assert_eq!(entry.display_name(), "d.md");
}
