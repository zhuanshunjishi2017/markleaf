// 恢复快照测试:.recovery.json 共享格式、64 位修订字符串保精度、原子写。
use markleaf::data::recovery::{discard_recovery, list_recovery, save_recovery, RecoveryEntry};

fn temp_dir(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("markleaf-recovery-{}-{name}", std::process::id()));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).expect("建临时目录");
    dir
}

#[test]
fn save_list_discard_round_trip() {
    let dir = temp_dir("roundtrip");
    let entry = RecoveryEntry {
        document_id: "doc-abc".into(),
        path: Some("/home/u/notes/a.md".into()),
        markdown: "# 草稿\n\n未保存内容".into(),
        revision: "42".into(),
        saved_at: 1_700_000_000,
    };
    save_recovery(&dir, &entry).expect("保存");
    let listed = list_recovery(&dir);
    assert_eq!(listed.len(), 1);
    assert_eq!(listed[0], entry);
    discard_recovery(&dir, "doc-abc");
    assert!(list_recovery(&dir).is_empty());
}

#[test]
fn large_revision_keeps_64bit_precision_as_string() {
    let dir = temp_dir("precision");
    let entry = RecoveryEntry {
        document_id: "doc-big".into(),
        path: None,
        markdown: "x".into(),
        // 超过 i32 的修订号:字符串承载,不丢失精度。
        revision: "9223372036854775807".into(),
        saved_at: 0,
    };
    save_recovery(&dir, &entry).expect("保存");
    let raw = std::fs::read_to_string(dir.join("doc-big.recovery.json")).expect("读原始 JSON");
    assert!(raw.contains("9223372036854775807"), "修订号必须是保精度字符串:{raw}");
    assert_eq!(list_recovery(&dir)[0].revision, "9223372036854775807");
}

#[test]
fn concurrent_save_same_id_leaves_no_partial_file() {
    let dir = temp_dir("concurrent");
    let handles: Vec<_> = (0..8)
        .map(|i| {
            let dir = dir.clone();
            std::thread::spawn(move || {
                save_recovery(
                    &dir,
                    &RecoveryEntry {
                        document_id: "doc-race".into(),
                        path: None,
                        markdown: format!("writer {i}"),
                        revision: "1".into(),
                        saved_at: i as i64,
                    },
                )
            })
        })
        .collect();
    for handle in handles {
        handle.join().expect("线程成功").expect("写入成功");
    }
    // 单文件、完整 JSON(最终写入者胜,内容可解析)。
    let listed = list_recovery(&dir);
    assert_eq!(listed.len(), 1);
    assert!(listed[0].markdown.starts_with("writer "));
}
