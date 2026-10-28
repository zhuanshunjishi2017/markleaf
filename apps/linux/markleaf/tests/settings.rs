// 设置存储测试:camelCase 对齐 macOS settings.json 键名、缺键容错、
// 未知键保留(merge 策略)、损坏 JSON 回退默认并告警、原子写。
use markleaf::data::settings::{load_settings, save_settings, AppSettings, RecentEntry};

fn temp_path(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("markleaf-settings-{}-{name}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("建目录");
    dir.join("settings.json")
}

#[test]
fn defaults_match_desktop_shape() {
    let settings = AppSettings::default();
    assert_eq!(settings.schema_version, 3);
    assert_eq!(settings.markdown_style, "serif");
    assert!(settings.follow_system_theme);
    assert!(settings.ctrl_wheel_zoom);
    assert_eq!(settings.zoom_percent, 100);
    assert_eq!(settings.source_indent_width, 2);
    assert!(settings.record_recent_files);
    assert!(settings.shortcuts.is_empty());
    assert!(settings.recent_files.is_empty());
}

#[test]
fn round_trip_preserves_values() {
    let path = temp_path("roundtrip");
    let mut settings = AppSettings::default();
    settings.markdown_style = "latex".into();
    settings.color_theme = "nord".into();
    settings.zoom_percent = 150;
    settings.shortcuts.insert("save".into(), "<Control>s".into());
    settings.recent_files.push(RecentEntry {
        path: "/home/u/notes/a.md".into(),
        name: "a.md".into(),
        at: 1_700_000_000,
    });
    save_settings(&path, &settings).expect("保存");
    let loaded = load_settings(&path);
    assert_eq!(loaded.markdown_style, "latex");
    assert_eq!(loaded.color_theme, "nord");
    assert_eq!(loaded.zoom_percent, 150);
    assert_eq!(loaded.shortcuts.get("save").map(String::as_str), Some("<Control>s"));
    assert_eq!(loaded.recent_files.len(), 1);
}

#[test]
fn unknown_keys_survive_save_merge() {
    let path = temp_path("merge");
    // 预置含 Linux 版尚不支持的键(未来版本/其他端写入)。
    std::fs::write(
        &path,
        r#"{"schemaVersion":3,"futureOnlyKey":{"nested":1},"markdownStyle":"print"}"#,
    )
    .expect("预置");
    let mut settings = load_settings(&path);
    assert_eq!(settings.markdown_style, "print", "已有键读出");
    settings.zoom_percent = 125;
    save_settings(&path, &settings).expect("保存");
    let raw = std::fs::read_to_string(&path).expect("读原始");
    assert!(raw.contains("futureOnlyKey"), "未知键必须保留:{raw}");
    let reloaded = load_settings(&path);
    assert_eq!(reloaded.zoom_percent, 125);
}

#[test]
fn corrupted_json_falls_back_to_defaults() {
    let path = temp_path("corrupt");
    std::fs::write(&path, "{ not json !!").expect("写坏");
    let settings = load_settings(&path);
    assert_eq!(settings, AppSettings::default(), "损坏回退默认");
}

#[test]
fn missing_keys_fall_back_per_field() {
    let path = temp_path("partial");
    std::fs::write(&path, r#"{"markdownStyle":"typewriter"}"#).expect("写部分");
    let settings = load_settings(&path);
    assert_eq!(settings.markdown_style, "typewriter");
    assert_eq!(settings.zoom_percent, 100, "缺键回默认");
}
