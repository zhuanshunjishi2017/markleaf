// 文档存储测试:原子写、外部修改冲突、mtime/哈希比对。
use std::time::Duration;

use markleaf::documents::store::{read_document, write_document, WriteError};

fn temp_doc(name: &str) -> std::path::PathBuf {
    let dir = std::env::temp_dir().join(format!("markleaf-store-test-{}", std::process::id()));
    std::fs::create_dir_all(&dir).expect("建临时目录");
    dir.join(name)
}

#[test]
fn write_then_read_round_trips() {
    let path = temp_doc("roundtrip.md");
    let _ = std::fs::remove_file(&path);
    std::fs::write(&path, b"v1").expect("初值");
    let snapshot = read_document(&path).expect("读初值");
    write_document(&path, b"v2 content", &snapshot).expect("写回");
    let after = read_document(&path).expect("读回");
    assert_eq!(after.bytes, b"v2 content");
    assert_ne!(after.content_hash, snapshot.content_hash);
}

#[test]
fn external_change_conflicts_write() {
    let path = temp_doc("conflict.md");
    std::fs::write(&path, b"original").expect("初值");
    let snapshot = read_document(&path).expect("读快照");
    // 外部修改(模拟另一进程/编辑器写入)。
    std::thread::sleep(Duration::from_millis(20));
    std::fs::write(&path, b"externally changed").expect("外部改");
    let result = write_document(&path, b"my save", &snapshot);
    assert!(matches!(result, Err(WriteError::Conflict)), "外部修改必须冲突,实得 {result:?}");
    // 冲突后文件保持外部内容,不被覆盖。
    assert_eq!(std::fs::read(&path).expect("读"), b"externally changed");
}

#[test]
fn write_is_atomic_no_partial_content_visible() {
    let path = temp_doc("atomic.md");
    std::fs::write(&path, b"before").expect("初值");
    let snapshot = read_document(&path).expect("读");
    let big = vec![b'x'; 512 * 1024];
    write_document(&path, &big, &snapshot).expect("写大文件");
    // 写完成后读到的要么是旧值要么是新值,绝无半文件( rename 语义保证)。
    let current = std::fs::read(&path).expect("读");
    assert!(current == b"before" || current == big);
    assert_eq!(current, big);
}
