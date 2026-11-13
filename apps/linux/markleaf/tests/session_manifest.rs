// 会话清单测试:窗口/标签/活动标签往返,空会话。
use markleaf::data::session::{SessionManifest, WindowSession};

#[test]
fn manifest_round_trip() {
    let manifest = SessionManifest {
        windows: vec![
            WindowSession {
                tabs: vec!["/home/u/a.md".into(), "/home/u/b.txt".into()],
                active_tab: 1,
                width: 1024,
                height: 768,
            },
            WindowSession {
                tabs: vec!["/home/u/c.md".into()],
                active_tab: 0,
                width: 800,
                height: 600,
            },
        ],
    };
    let path = std::env::temp_dir().join(format!("markleaf-session-{}.json", std::process::id()));
    let _ = std::fs::remove_file(&path);
    markleaf::data::session::save(&path, &manifest).expect("保存");
    let loaded = markleaf::data::session::load(&path).expect("读取");
    assert_eq!(loaded, manifest);
    let _ = std::fs::remove_file(&path);
}

#[test]
fn missing_file_yields_empty_manifest() {
    let path = std::env::temp_dir().join("markleaf-session-nonexistent.json");
    let _ = std::fs::remove_file(&path);
    let manifest = markleaf::data::session::load(&path).unwrap_or_default();
    assert!(manifest.windows.is_empty());
}

#[test]
fn active_tab_clamped_on_load() {
    let manifest = SessionManifest {
        windows: vec![WindowSession {
            tabs: vec!["a.md".into()],
            active_tab: 7,
            width: 0,
            height: 0,
        }],
    };
    let path = std::env::temp_dir().join(format!("markleaf-session-clamp-{}.json", std::process::id()));
    markleaf::data::session::save(&path, &manifest).expect("保存");
    let loaded = markleaf::data::session::load(&path).expect("读取");
    assert_eq!(loaded.windows[0].active_tab, 0, "越界活动标签收拢");
    let _ = std::fs::remove_file(&path);
}
