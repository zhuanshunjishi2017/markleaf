// 工作区搜索:名称/内容;内容走 UTF-8 快路径,非 UTF-8 经编码检测解码后搜索
// (GBK 等历史文档可命中);结果带行号与摘录。
use std::fs;
use std::path::{Path, PathBuf};

use crate::documents::encoding::detect;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SearchMode {
    Name,
    Content,
}

#[derive(Debug, Clone, PartialEq)]
pub struct SearchHit {
    pub path: PathBuf,
    /// 1 基行号(名称搜索为 0)。
    pub line: usize,
    pub excerpt: String,
}

fn scan_files(root: &Path) -> Vec<PathBuf> {
    let mut files = Vec::new();
    let mut stack = vec![root.to_path_buf()];
    while let Some(dir) = stack.pop() {
        let Ok(read) = fs::read_dir(&dir) else {
            continue;
        };
        for item in read.flatten() {
            let Ok(file_type) = item.file_type() else {
                continue;
            };
            let name = item.file_name().to_string_lossy().into_owned();
            if name.starts_with('.') {
                continue;
            }
            if file_type.is_dir() {
                stack.push(item.path());
            } else if name.to_ascii_lowercase().ends_with(".md")
                || name.to_ascii_lowercase().ends_with(".txt")
            {
                files.push(item.path());
            }
        }
    }
    files.sort();
    files
}

pub fn search_workspace(root: &Path, query: &str, mode: SearchMode) -> Vec<SearchHit> {
    let query_lower = query.to_lowercase();
    let mut hits = Vec::new();
    if query_lower.is_empty() {
        return hits;
    }
    for path in scan_files(root) {
        match mode {
            SearchMode::Name => {
                let name = path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
                if name.to_lowercase().contains(&query_lower) {
                    hits.push(SearchHit {
                        path,
                        line: 0,
                        excerpt: name,
                    });
                }
            }
            SearchMode::Content => {
                let Ok(bytes) = fs::read(&path) else {
                    continue;
                };
                let text = detect(&bytes).decoded;
                for (index, line) in text.lines().enumerate() {
                    if line.to_lowercase().contains(&query_lower) {
                        let excerpt = line.trim().chars().take(120).collect();
                        hits.push(SearchHit {
                            path,
                            line: index + 1,
                            excerpt,
                        });
                        break;
                    }
                }
            }
        }
    }
    hits
}
