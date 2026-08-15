import Foundation

/// 工作区扫描器：异步枚举目录（md/txt 文件 + 文件夹），文件夹优先、名称排序。
/// 对应 C# WorkspaceService.EnumerateChildren / EnumerateDocuments。
final class WorkspaceScanner {
    private let root: String
    private let queue = DispatchQueue(label: "com.markleaf.workspace-scan")
    private var cancelled = false

    private static let allowedExtensions: Set<String> = ["md", "txt", "markdown"]

    var onComplete: (([WorkspaceEntry]) -> Void)?

    init(root: String, onComplete: @escaping ([WorkspaceEntry]) -> Void) {
        self.root = root
        self.onComplete = onComplete
    }

    func scan() {
        queue.async { [weak self] in
            guard let self else { return }
            let entries = Self.enumerateChildren(directory: self.root, cancelled: { self.cancelled })
            DispatchQueue.main.async {
                self.onComplete?(entries)
            }
        }
    }

    /// 递归枚举工作区全部文档（对应 C# WorkspaceService.EnumerateDocuments），按修改时间倒序。
    func scanDocuments(completion: @escaping ([WorkspaceEntry]) -> Void) {
        queue.async { [weak self] in
            guard let self else { return }
            let documents = Self.enumerateDocuments(root: self.root, cancelled: { self.cancelled })
            DispatchQueue.main.async {
                completion(documents)
            }
        }
    }

    func cancel() {
        cancelled = true
    }

    private static func enumerateDocuments(root: String, cancelled: () -> Bool) -> [WorkspaceEntry] {
        let fm = FileManager.default
        var results: [WorkspaceEntry] = []
        var stack = [root]
        while let directory = stack.popLast() {
            if cancelled() { break }
            guard let items = try? fm.contentsOfDirectory(atPath: directory) else { continue }
            for item in items {
                if cancelled() { break }
                if item.hasPrefix(".") { continue }
                let path = (directory as NSString).appendingPathComponent(item)
                var isDirectory: ObjCBool = false
                guard fm.fileExists(atPath: path, isDirectory: &isDirectory) else { continue }
                if isDirectory.boolValue {
                    // 跳过符号链接目录，避免循环
                    if (try? fm.destinationOfSymbolicLink(atPath: path)) == nil {
                        stack.append(path)
                    }
                } else {
                    let ext = (item as NSString).pathExtension.lowercased()
                    if allowedExtensions.contains(ext) {
                        results.append(WorkspaceEntry(name: item, path: path, isDirectory: false))
                    }
                }
            }
        }
        return results.sorted { modificationDate($0.path) > modificationDate($1.path) }
    }

    private static func modificationDate(_ path: String) -> Date {
        (try? FileManager.default.attributesOfItem(atPath: path)[.modificationDate] as? Date) ?? .distantPast
    }

    private static func enumerateChildren(directory: String, cancelled: () -> Bool) -> [WorkspaceEntry] {
        let fm = FileManager.default
        guard let items = try? fm.contentsOfDirectory(atPath: directory) else { return [] }

        var entries: [WorkspaceEntry] = []
        for item in items {
            if cancelled() { break }
            let path = (directory as NSString).appendingPathComponent(item)
            var isDirectory: ObjCBool = false
            guard fm.fileExists(atPath: path, isDirectory: &isDirectory) else { continue }
            // 隐藏文件/目录
            if item.hasPrefix(".") { continue }
            if !isDirectory.boolValue {
                let ext = (item as NSString).pathExtension.lowercased()
                guard Self.allowedExtensions.contains(ext) else { continue }
            }
            entries.append(WorkspaceEntry(name: item, path: path, isDirectory: isDirectory.boolValue))
        }

        return entries.sorted { a, b in
            if a.isDirectory != b.isDirectory {
                return a.isDirectory && !b.isDirectory
            }
            return a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
        }
    }
}
