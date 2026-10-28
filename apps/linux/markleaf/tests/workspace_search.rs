// 工作区搜索测试:名称/内容两种模式;非 UTF-8 文档按检测解码后搜索。
use std::fs;

use markleaf::workspace::search::{search_workspace, SearchMode};

fn temp_ws(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("markleaf-search-{}-{name}", std::process::id()));
    let _ = fs::remove_dir_all(&dir);
    fs::create_dir_all(&dir).expect("建目录");
    fs::write(dir.join("notes.md"), "# 笔记\n\n包含关键词 needle 的内容").expect("写 md");
    fs::write(dir.join("todo.txt"), "another NEEDLE here").expect("写 txt");
    // GBK 编码文件:iconv -f UTF-8 -t GBK 「中文测试 needle」
    let mut gbk = vec![0xD6, 0xD0, 0xCE, 0xC4, 0xB2, 0xE2, 0xCA, 0xD4];
    gbk.extend_from_slice(b" needle");
    fs::write(dir.join("legacy.md"), gbk).expect("写 GBK");
    fs::write(dir.join("empty.md"), "nothing relevant").expect("写无关");
    dir
}

#[test]
fn name_search_matches_exact_substring() {
    let root = temp_ws("name");
    let hits = search_workspace(&root, "todo", SearchMode::Name);
    assert_eq!(hits.len(), 1);
    assert_eq!(hits[0].path.ends_with("todo.txt"), true);
    assert_eq!(hits[0].line, 0);
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn content_search_hits_utf8_and_decoded_legacy_files() {
    let root = temp_ws("content");
    let hits = search_workspace(&root, "needle", SearchMode::Content);
    let files: Vec<&str> = hits.iter().map(|h| h.path.file_name().unwrap().to_str().unwrap()).collect();
    assert!(files.contains(&"notes.md"), "实得 {files:?}");
    assert!(files.contains(&"todo.txt"));
    assert!(files.contains(&"legacy.md"), "非 UTF-8(GBK)文件解码后命中:{files:?}");
    // 行号与摘录。
    let notes = hits.iter().find(|h| h.path.ends_with("notes.md")).expect("notes 命中");
    assert_eq!(notes.line, 3);
    assert!(notes.excerpt.contains("needle"));
    let _ = fs::remove_dir_all(&root);
}

#[test]
fn case_insensitive_content_search() {
    let root = temp_ws("case");
    let hits = search_workspace(&root, "NEEDLE", SearchMode::Content);
    assert!(hits.iter().any(|h| h.path.ends_with("notes.md")), "大小写不敏感");
    let _ = fs::remove_dir_all(&root);
}
