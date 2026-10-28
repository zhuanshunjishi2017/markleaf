// 工作区监视:notify(inotify)→ 200ms 防抖 → 单次变更回调。
// 回调在 notify 线程触发,GTK 侧需自行派发主线程后做增量扫描与刷新。
use std::path::Path;
use std::sync::mpsc;
use std::time::{Duration, Instant};

use notify::{Event, RecursiveMode, Watcher};

pub struct WorkspaceWatcher {
    // notify Watcher 需存活以维持 inotify 订阅。
    _watcher: notify::RecommendedWatcher,
}

impl WorkspaceWatcher {
    /// `debounce_ms` 内的连续事件合并为一次回调。
    pub fn new(root: &Path, debounce_ms: u64, on_change: impl Fn() + Send + 'static) -> notify::Result<Self> {
        let (tx, rx) = mpsc::channel::<Event>();
        let mut watcher = notify::recommended_watcher(move |result: notify::Result<Event>| {
            if let Ok(event) = result {
                let _ = tx.send(event);
            }
        })?;
        watcher.watch(root, RecursiveMode::Recursive)?;

        // 防抖合并线程:等首个事件 → 窗口内吞后续 → 触发一次。
        std::thread::spawn(move || {
            let debounce = Duration::from_millis(debounce_ms);
            loop {
                if rx.recv().is_err() {
                    break;
                }
                let deadline = Instant::now() + debounce;
                loop {
                    let now = Instant::now();
                    if now >= deadline {
                        break;
                    }
                    match rx.recv_timeout(deadline - now) {
                        Ok(_) => continue,
                        Err(mpsc::RecvTimeoutError::Timeout) => break,
                        Err(mpsc::RecvTimeoutError::Disconnected) => return,
                    }
                }
                on_change();
            }
        });

        Ok(Self { _watcher: watcher })
    }
}
