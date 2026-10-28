// 窗口会话清单:$XDG_STATE_HOME/markleaf/session/manifest.json。
// 启动恢复窗口/标签/活动标签;正常退出保存;越界数据容错收拢。
use std::fs;
use std::path::Path;

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WindowSession {
    pub tabs: Vec<String>,
    pub active_tab: usize,
    pub width: i32,
    pub height: i32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct SessionManifest {
    pub windows: Vec<WindowSession>,
}

pub fn save(path: &Path, manifest: &SessionManifest) -> std::io::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    let json = serde_json::to_string_pretty(manifest).expect("SessionManifest 序列化");
    let temp = path.with_extension("json.tmp");
    fs::write(&temp, json)?;
    fs::rename(&temp, path)
}

pub fn load(path: &Path) -> Option<SessionManifest> {
    let text = fs::read_to_string(path).ok()?;
    let mut manifest: SessionManifest = serde_json::from_str(&text).ok()?;
    // 越界/畸形数据收拢:活动标签落在有效范围,空标签窗口丢弃。
    manifest.windows.retain(|window| !window.tabs.is_empty());
    for window in &mut manifest.windows {
        if window.active_tab >= window.tabs.len() {
            window.active_tab = 0;
        }
    }
    Some(manifest)
}
