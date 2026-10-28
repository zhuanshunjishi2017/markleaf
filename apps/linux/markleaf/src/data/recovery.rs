// 恢复快照存储:.recovery.json 共享格式(kernel-boundaries 统一行为节)。
// 单文件原子保存(内容+元数据一个文件),修订号用字符串保留 64 位精度;
// 正常保存后由调用方 discard;旧格式(.md+.meta)的读取在启动迁移处处理(M3)。
use std::fs;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct RecoveryEntry {
    #[serde(rename = "documentId")]
    pub document_id: String,
    #[serde(rename = "path")]
    pub path: Option<String>,
    pub markdown: String,
    /// 修订号字符串(64 位精度)。
    pub revision: String,
    #[serde(rename = "savedAt")]
    pub saved_at: i64,
}

fn entry_path(dir: &Path, document_id: &str) -> PathBuf {
    // documentId 来自 UUID(字母数字连字符),无路径风险;防御性过滤分隔符。
    let safe: String = document_id
        .chars()
        .filter(|c| c.is_ascii_alphanumeric() || *c == '-')
        .collect();
    dir.join(format!("{safe}.recovery.json"))
}

/// 原子保存:temp + rename,并发同 id 最终一致。
pub fn save_recovery(dir: &Path, entry: &RecoveryEntry) -> std::io::Result<()> {
    fs::create_dir_all(dir)?;
    let json = serde_json::to_string_pretty(entry).expect("RecoveryEntry 序列化");
    let target = entry_path(dir, &entry.document_id);
    let temp = dir.join(".recovery.tmp");
    fs::write(&temp, json)?;
    fs::rename(&temp, target)
}

pub fn list_recovery(dir: &Path) -> Vec<RecoveryEntry> {
    let mut entries = Vec::new();
    let Ok(read) = fs::read_dir(dir) else {
        return entries;
    };
    for item in read.flatten() {
        let path = item.path();
        if path.extension().and_then(|e| e.to_str()) != Some("json") {
            continue;
        }
        if path.file_name().and_then(|n| n.to_str()) == Some(".recovery.tmp") {
            continue;
        }
        if let Ok(text) = fs::read_to_string(&path) {
            if let Ok(entry) = serde_json::from_str::<RecoveryEntry>(&text) {
                entries.push(entry);
            }
        }
    }
    entries.sort_by(|a, b| a.document_id.cmp(&b.document_id));
    entries
}

pub fn discard_recovery(dir: &Path, document_id: &str) {
    let target = entry_path(dir, document_id);
    let _ = fs::remove_file(target);
}
