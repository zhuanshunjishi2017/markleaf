// 工作区监视测试:扫描读访问不触发刷新，写文件触发刷新(仅 Linux)。
#[cfg(target_os = "linux")]
mod linux_tests {
    use std::fs;
    use std::sync::mpsc;
    use std::time::Duration;

    use markleaf::workspace::watcher::WorkspaceWatcher;

    #[test]
    fn scanning_is_ignored_but_file_changes_are_reported() {
        let dir = std::env::temp_dir().join(format!("markleaf-watch-{}", std::process::id()));
        let _ = fs::remove_dir_all(&dir);
        fs::create_dir_all(&dir).expect("建目录");
        fs::write(dir.join("existing.md"), "x").expect("写测试文档");

        let (tx, rx) = mpsc::channel();
        let _watcher = WorkspaceWatcher::new(&dir, 200, move || {
            let _ = tx.send(());
        }).expect("启动工作区监视");

        let entries = markleaf::workspace::model::scan_workspace(&dir);
        assert_eq!(entries.len(), 1);
        assert!(
            matches!(rx.recv_timeout(Duration::from_millis(600)), Err(mpsc::RecvTimeoutError::Timeout)),
            "扫描产生的读访问不应触发刷新"
        );

        // 监视建立后写文件。
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
