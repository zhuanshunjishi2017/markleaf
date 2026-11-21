// 设置存储:$XDG_CONFIG_HOME/markleaf/settings.json(镜像 macOS settings.json
// 哲学:camelCase 键名、缺键容错回默认、原子写)。
// 未知键保留策略:serde 无法表达"保留未知字段",save 以磁盘现有 JSON 为底、
// 逐键覆盖已知字段(merge)——其他端/未来版本写入的键不丢失。
use std::fs;
use std::path::Path;

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
pub struct RecentEntry {
    pub path: String,
    pub name: String,
    pub at: i64,
}

/// 状态栏命令状态的显示策略(对齐 macOS StatusBarCommandDisplayMode)。
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum StatusBarCommandDisplayMode {
    Always,
    Temporary,
    Hidden,
}

/// 状态栏自定义设置(对齐 macOS StatusBarSettings 的 10 项开关)。
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct StatusBarSettings {
    pub sidebar_toggle_visible: bool,
    pub command_status_visible: bool,
    pub command_display_mode: StatusBarCommandDisplayMode,
    pub word_count_visible: bool,
    pub block_type_visible: bool,
    pub position_visible: bool,
    pub encoding_visible: bool,
    pub new_line_visible: bool,
    pub mode_toggle_visible: bool,
    pub zoom_visible: bool,
}

impl Default for StatusBarCommandDisplayMode {
    fn default() -> Self {
        Self::Always
    }
}

impl Default for StatusBarSettings {
    fn default() -> Self {
        Self {
            sidebar_toggle_visible: true,
            command_status_visible: true,
            command_display_mode: StatusBarCommandDisplayMode::Always,
            word_count_visible: true,
            block_type_visible: true,
            position_visible: true,
            encoding_visible: true,
            new_line_visible: true,
            mode_toggle_visible: true,
            zoom_visible: true,
        }
    }
}

impl StatusBarSettings {
    pub const fn all_on() -> Self {
        Self {
            sidebar_toggle_visible: true,
            command_status_visible: true,
            command_display_mode: StatusBarCommandDisplayMode::Always,
            word_count_visible: true,
            block_type_visible: true,
            position_visible: true,
            encoding_visible: true,
            new_line_visible: true,
            mode_toggle_visible: true,
            zoom_visible: true,
        }
    }
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(default, rename_all = "camelCase")]
pub struct AppSettings {
    pub schema_version: i32,
    /// 界面语言(zh-CN/en-US/ja-JP/zh-TW;空 = 跟随系统)。
    pub display_language: String,
    pub markdown_style: String,
    pub color_theme: String,
    pub zoom_percent: i32,
    pub restore_zoom_on_open: bool,
    /// Ctrl+滚轮缩放(桌面非 macOS 默认开)。
    pub ctrl_wheel_zoom: bool,
    pub auto_hide_scrollbars: bool,
    pub follow_system_theme: bool,
    pub default_light_theme_id: String,
    pub default_dark_theme_id: String,
    pub visual_cjk_auto_spacing: bool,
    pub source_indent_width: i32,
    pub new_line_style: String,
    pub default_encoding: String,
    pub snapshot_interval_seconds: i32,
    pub record_recent_files: bool,
    pub record_recent_folders: bool,
    /// 自定义快捷键:命令 id → GTK accel 字符串。
    pub shortcuts: std::collections::HashMap<String, String>,
    pub recent_files: Vec<RecentEntry>,
    pub recent_folders: Vec<RecentEntry>,
    /// 状态栏自定义(对齐 macOS StatusBarSettings)。
    pub status_bar: StatusBarSettings,
    /// 侧栏宽度(px;Paned 分隔条可拖拽调整)。
    pub sidebar_width: i32,
}

impl AppSettings {
    /// 记录最近文件(新开顶置去重,上限 10;对齐桌面 recentFiles 语义)。
    pub fn remember_recent_file(&mut self, path: &str, name: &str) {
        if !self.record_recent_files {
            return;
        }
        self.recent_files.retain(|entry| entry.path != path);
        self.recent_files.insert(
            0,
            RecentEntry {
                path: path.to_string(),
                name: name.to_string(),
                at: chrono_time_secs(),
            },
        );
        self.recent_files.truncate(10);
    }

    /// 记录最近文件夹(对齐桌面 recentFolders 语义)。
    pub fn remember_recent_folder(&mut self, path: &str, name: &str) {
        if !self.record_recent_folders {
            return;
        }
        self.recent_folders.retain(|entry| entry.path != path);
        self.recent_folders.insert(
            0,
            RecentEntry {
                path: path.to_string(),
                name: name.to_string(),
                at: chrono_time_secs(),
            },
        );
        self.recent_folders.truncate(10);
    }

    /// 最近项目子菜单条目构造参数(文件与文件夹两组,供菜单层渲染)。
    pub fn recent_menu_entries(&self) -> (Vec<RecentEntry>, Vec<RecentEntry>) {
        (
            self.recent_files.clone(),
            self.recent_folders.clone(),
        )
    }
}

fn chrono_time_secs() -> i64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map(|d| d.as_secs() as i64)
        .unwrap_or(0)
}

impl Default for AppSettings {
    fn default() -> Self {
        Self {
            schema_version: 3,
            display_language: String::new(),
            markdown_style: "serif".into(),
            color_theme: "apple-blue".into(),
            zoom_percent: 100,
            restore_zoom_on_open: true,
            ctrl_wheel_zoom: true,
            auto_hide_scrollbars: true,
            follow_system_theme: true,
            default_light_theme_id: "apple-blue".into(),
            default_dark_theme_id: "apple-dark".into(),
            visual_cjk_auto_spacing: true,
            source_indent_width: 2,
            new_line_style: "lf".into(),
            default_encoding: "UTF-8".into(),
            snapshot_interval_seconds: 30,
            record_recent_files: true,
            record_recent_folders: true,
            shortcuts: Default::default(),
            recent_files: Vec::new(),
            recent_folders: Vec::new(),
            status_bar: StatusBarSettings::all_on(),
            sidebar_width: 280,
        }
    }
}

/// 读取设置:文件缺失/损坏 → 默认(损坏时 stderr 告警)。
pub fn load_settings(path: &Path) -> AppSettings {
    let Ok(text) = fs::read_to_string(path) else {
        return AppSettings::default();
    };
    match serde_json::from_str(&text) {
        Ok(settings) => settings,
        Err(err) => {
            eprintln!("[settings] {} 解析失败,回退默认:{err}", path.display());
            AppSettings::default()
        }
    }
}

/// 原子保存,并保留磁盘上既有的未知键(见模块注释)。
pub fn save_settings(path: &Path, settings: &AppSettings) -> std::io::Result<()> {
    if let Some(parent) = path.parent() {
        fs::create_dir_all(parent)?;
    }
    // 底稿:现有文件(若有);覆盖:已知字段。
    let mut merged: serde_json::Value = fs::read_to_string(path)
        .ok()
        .and_then(|text| serde_json::from_str(&text).ok())
        .unwrap_or_else(|| serde_json::Value::Object(Default::default()));
    let known = serde_json::to_value(settings).expect("AppSettings 序列化");
    if let (serde_json::Value::Object(base), serde_json::Value::Object(update)) = (&mut merged, known) {
        for (key, value) in update {
            base.insert(key, value);
        }
    }
    let json = serde_json::to_string_pretty(&merged).expect("merge 后序列化");
    let temp = path.with_extension("json.tmp");
    fs::write(&temp, json)?;
    fs::rename(&temp, path)
}
