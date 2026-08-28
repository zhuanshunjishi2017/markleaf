import AppKit

/// 「检查更新…」的完整流程：查询 GitHub 最新 Release → 提示/下载 → 打开 DMG → 退出应用。
/// UI 用 NSAlert sheet 呈现；网络/下载由 UpdateCheckService 与 URLSession 完成。
final class UpdateCheckController: NSObject, URLSessionDownloadDelegate {
    private var currentVersion = ""
    private var currentBuild = ""
    private var downloadTask: URLSessionDownloadTask?
    private var downloadSession: URLSession?
    private var downloadProgressAlert: NSAlert?
    private var downloadProgressIndicator: NSProgressIndicator?
    private var downloadDestination: URL?
    private weak var statusSession: EditorSession?
    private var statusBeforeCheck = ""
    private var checkingStatus = ""

    /// 启动一次检查，并在结果返回后恢复发起检查的窗口状态。
    func begin() {
        let session = AppWindowManager.shared.activeSession
        statusSession = session
        statusBeforeCheck = session?.statusText ?? ""
        checkingStatus = L10n.t("正在检查更新…")
        session?.statusText = checkingStatus
        let info = Bundle.main.infoDictionary
        currentVersion = (info?["CFBundleShortVersionString"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "1.0.0"
        currentBuild = (info?["CFBundleVersion"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? "1"

        UpdateCheckService.fetchLatestRelease { [weak self] result in
            guard let self else { return }
            self.finishStatus()
            switch result {
            case .failure:
                self.presentFailure(message: L10n.t("检查更新失败"))
            case .success(let release):
                if UpdateCheckService.hasUpdate(
                    release: release,
                    currentVersion: self.currentVersion,
                    currentBuild: self.currentBuild
                ) {
                    self.presentUpdate(release: release)
                } else {
                    self.presentUpToDate()
                }
            }
        }
    }

    private func finishStatus() {
        guard let session = statusSession else { return }
        session.statusText = UpdateCheckService.statusAfterCheck(
            previousStatus: statusBeforeCheck,
            currentStatus: session.statusText,
            checkingStatus: checkingStatus
        )
        statusSession = nil
    }

    // MARK: - Result dialogs

    private func presentUpToDate() {
        let alert = NSAlert()
        alert.messageText = L10n.t("当前已是最新版本")
        alert.informativeText = L10n.f("当前版本 %@（构建 %@）", currentVersion, currentBuild)
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("好"))
        present(alert)
    }

    private func presentFailure(message: String) {
        let alert = NSAlert()
        alert.messageText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: L10n.t("重试"))
        alert.addButton(withTitle: L10n.t("取消"))
        present(alert) { [weak self] response in
            if response == .alertFirstButtonReturn {
                self?.begin()
            }
        }
    }

    private func presentUpdate(release: UpdateCheckService.Release) {
        let alert = NSAlert()
        alert.messageText = L10n.f("发现新版本 %@", release.tagName)
        alert.informativeText =
            L10n.f("当前版本 %@（构建 %@）", currentVersion, currentBuild)
            + "\n\n"
            + preview(release.body)
        alert.alertStyle = .informational
        alert.addButton(withTitle: L10n.t("下载更新"))
        alert.addButton(withTitle: L10n.t("前往 GitHub Releases"))
        alert.addButton(withTitle: L10n.t("稍后"))
        present(alert) { [weak self] response in
            guard let self else { return }
            switch response {
            case .alertFirstButtonReturn:
                DispatchQueue.main.async { self.downloadAndOpen(release: release) }
            case .alertSecondButtonReturn:
                NSWorkspace.shared.open(UpdateCheckService.releasePageURL(tag: release.tagName))
            default:
                break
            }
        }
    }

    private func preview(_ body: String?) -> String {
        guard let body else { return "" }
        let lines = body
            .split(separator: "\n")
            .map { String($0).trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        let joined = lines.prefix(6).joined(separator: "\n")
        if joined.count > 280 { return String(joined.prefix(277)) + "…" }
        return joined
    }

    // MARK: - Download

    private func downloadAndOpen(release: UpdateCheckService.Release) {
        guard let url = UpdateCheckService.macOSInstallerURL(from: release) else {
            presentFailure(message: L10n.t("找不到适用于本机的安装包"))
            return
        }

        // 下载进度 sheet
        let progress = NSProgressIndicator()
        progress.style = .bar
        progress.isIndeterminate = false
        progress.minValue = 0
        progress.maxValue = 1
        progress.doubleValue = 0
        progress.controlSize = .small
        progress.widthAnchor.constraint(equalToConstant: 260).isActive = true

        let alert = NSAlert()
        alert.messageText = L10n.t("正在下载更新")
        alert.informativeText = url.lastPathComponent
        alert.alertStyle = .informational
        alert.accessoryView = progress
        alert.addButton(withTitle: L10n.t("取消"))
        downloadProgressAlert = alert
        downloadProgressIndicator = progress
        downloadDestination = downloadsURL(for: url.lastPathComponent)
        present(alert) { [weak self] response in
            if response == .alertFirstButtonReturn, let self {
                self.cancelDownload()
            }
        }

        let config = URLSessionConfiguration.default
        downloadSession = URLSession(configuration: config, delegate: self, delegateQueue: nil)
        downloadTask = downloadSession?.downloadTask(with: url)
        downloadTask?.resume()
    }

    private func cancelDownload() {
        downloadTask?.cancel()
        downloadTask = nil
    }

    private func downloadsURL(for filename: String) -> URL {
        let dir = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        var target = dir.appendingPathComponent(filename)
        // 防止覆盖同名文件时误判：加序号（但首选保留原始文件名）。
        var attempt = 1
        while FileManager.default.fileExists(atPath: target.path) {
            let ext = (filename as NSString).pathExtension
            let base = (filename as NSString).deletingPathExtension
            let candidate = ext.isEmpty ? "\(base)-\(attempt)" : "\(base)-\(attempt).\(ext)"
            target = dir.appendingPathComponent(candidate)
            attempt += 1
        }
        return target
    }

    // MARK: - URLSessionDownloadDelegate

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didWriteData bytesWritten: Int64,
        totalBytesWritten: Int64,
        totalBytesExpectedToWrite: Int64
    ) {
        DispatchQueue.main.async { [weak self] in
            guard totalBytesExpectedToWrite > 0 else { return }
            self?.downloadProgressIndicator?.doubleValue =
                Double(totalBytesWritten) / Double(totalBytesExpectedToWrite)
        }
    }

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didFinishDownloadingTo location: URL
    ) {
        guard let destination = downloadDestination else { return }
        do {
            try? FileManager.default.removeItem(at: destination)
            try FileManager.default.moveItem(at: location, to: destination)
        } catch {
            DispatchQueue.main.async { [weak self] in
                self?.presentFailure(message: L10n.t("无法下载更新"))
            }
            return
        }
        DispatchQueue.main.async { [weak self] in
            self?.finishDownload(destination)
        }
    }

    func urlSession(
        _ session: URLSession,
        task: URLSessionTask,
        didCompleteWithError error: Error?
    ) {
        if let error, (error as NSError).code != NSURLErrorCancelled {
            DispatchQueue.main.async { [weak self] in
                self?.presentFailure(message: L10n.t("无法下载更新"))
            }
        } else if error == nil {
            // 成功路径在 didFinishDownloadingTo 中处理。
        }
    }

    private func finishDownload(_ destination: URL) {
        if let progressWindow = downloadProgressAlert?.window,
           let sheetParent = progressWindow.sheetParent {
            sheetParent.endSheet(progressWindow)
        }
        downloadProgressAlert = nil
        // 打开 DMG（挂载 + 打开 Finder 窗口），随后退出当前应用，让用户拖拽覆盖安装。
        NSWorkspace.shared.open(destination)
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.2) {
            NSApp.terminate(nil)
        }
    }

    // MARK: - Alert helper

    private func present(_ alert: NSAlert, completion: ((NSApplication.ModalResponse) -> Void)? = nil) {
        if let window = AppWindowManager.shared.activeSession?.webView?.window {
            if let completion {
                alert.beginSheetModal(for: window, completionHandler: completion)
            } else {
                alert.beginSheetModal(for: window)
            }
        } else if let completion {
            completion(alert.runModal())
        } else {
            alert.runModal()
        }
    }
}
