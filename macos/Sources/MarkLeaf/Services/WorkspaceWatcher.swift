import CoreServices
import Foundation

/// 工作区目录自动刷新（对应 C# WorkspaceService + FileSystemWatcher）。
/// 用 FSEvents 递归监听工作区根目录，变化去抖后回调重新扫描。
final class WorkspaceWatcher {
    private var stream: FSEventStreamRef?
    private var debounceTimer: Timer?
    private var onChange: (() -> Void)?
    private let queue = DispatchQueue.main

    func start(watching path: String, onChange: @escaping () -> Void) {
        stop()
        self.onChange = onChange
        var context = FSEventStreamContext()
        context.info = Unmanaged.passUnretained(self).toOpaque()

        // 无捕获的回调：通过 info 取回 watcher 实例
        let callback: FSEventStreamCallback = { _, info, _, _, _, _ in
            guard let info else { return }
            let watcher = Unmanaged<WorkspaceWatcher>.fromOpaque(info).takeUnretainedValue()
            watcher.schedule()
        }

        let streamRef = FSEventStreamCreate(
            kCFAllocatorDefault,
            callback,
            &context,
            [path] as CFArray,
            FSEventStreamEventId(kFSEventStreamEventIdSinceNow),
            1.0,
            FSEventStreamCreateFlags(kFSEventStreamCreateFlagFileEvents | kFSEventStreamCreateFlagWatchRoot)
        )
        guard let streamRef else { return }
        self.stream = streamRef
        FSEventStreamSetDispatchQueue(streamRef, queue)
        FSEventStreamStart(streamRef)
        AppLog.info("工作区自动监听已启动: \(path)")
    }

    func stop() {
        debounceTimer?.invalidate()
        debounceTimer = nil
        if let stream {
            FSEventStreamStop(stream)
            FSEventStreamInvalidate(stream)
            FSEventStreamRelease(stream)
            self.stream = nil
        }
        onChange = nil
    }

    private func schedule() {
        debounceTimer?.invalidate()
        let timer = Timer(timeInterval: 0.6, repeats: false) { [weak self] _ in
            DispatchQueue.main.async {
                self?.onChange?()
            }
        }
        RunLoop.main.add(timer, forMode: .common)
        debounceTimer = timer
    }

    deinit {
        stop()
    }
}
