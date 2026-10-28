// 工作区监视测试:notify 推荐 watcher 在 tmpdir 建删文件产生事件(仅 Linux)。
#[cfg(target_os = "linux")]
mod linux_tests {
    use std::fs;
    use std::sync::mpsc;
    use std::time::Duration;

    use markleaf::workspace::watcher::WorkspaceWatcher;

    #[test]
    fn create_file_triggers_change_after_debounce() {
        let dir = std::env::temp_dir().join(format!("markleaf-watch-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).expect("建目录");

        let (tx, rx) = mpsc::channel();
        let _watcher = WorkspaceWatcher::new(&dir, 200, move || {
            let _ = tx.send(());
        });

        // 监视建立后写文件。
        std::thread::sleep(Duration::from_millis(300));
        fs::write(dir.join("late.md"), "x").expect("写文件");

        let changed = rx.recv_timeout(Duration::from_secs(5)).is_ok();
        assert!(changed, "建文件应触发防抖后的变更回调");
        let _ = fs::remove_dir_all(&dir);
    }
}

#[cfg(not(target_os = "linux"))]
#[test]
fn watcher_tests_are_linux_only() {
    // inotify 依赖:非 Linux 构建跳过(契约注释)。
}
