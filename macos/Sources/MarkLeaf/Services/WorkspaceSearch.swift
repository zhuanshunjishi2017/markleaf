import AppKit

struct WorkspaceSearchResult {
    let entry: WorkspaceEntry
    let match: String
}

/// 工作区搜索：文件名或 Markdown/TXT 内容匹配，异步执行并可取消。
final class WorkspaceSearchService {
    private var task: DispatchWorkItem?

    func search(root: String, query: String, completion: @escaping ([WorkspaceSearchResult]) -> Void) {
        task?.cancel()
        let normalized = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        guard !normalized.isEmpty else {
            completion([])
            return
        }
        let work = DispatchWorkItem {
            let fm = FileManager.default
            var stack = [root]
            var results: [WorkspaceSearchResult] = []
            while let directory = stack.popLast() {
                guard let items = try? fm.contentsOfDirectory(atPath: directory) else { continue }
                for name in items where !name.hasPrefix(".") {
                    let path = (directory as NSString).appendingPathComponent(name)
                    var isDirectory: ObjCBool = false
                    guard fm.fileExists(atPath: path, isDirectory: &isDirectory) else { continue }
                    if isDirectory.boolValue {
                        if (try? fm.destinationOfSymbolicLink(atPath: path)) == nil {
                            stack.append(path)
                        }
                        continue
                    }
                    let ext = (name as NSString).pathExtension.lowercased()
                    guard ["md", "txt"].contains(ext) else { continue }
                    let content = (try? String(contentsOfFile: path, encoding: .utf8)) ?? ""
                    guard name.lowercased().contains(normalized) || content.lowercased().contains(normalized) else { continue }
                    let snippet = content.split(whereSeparator: { $0 == "\n" }).first(where: { $0.lowercased().contains(normalized) }).map(String.init) ?? ""
                    results.append(WorkspaceSearchResult(entry: WorkspaceEntry(name: name, path: path, isDirectory: false), match: snippet))
                }
            }
            results.sort { $0.entry.path.localizedCaseInsensitiveCompare($1.entry.path) == .orderedAscending }
            DispatchQueue.main.async {
                completion(results)
            }
        }
        task = work
        DispatchQueue.global(qos: .userInitiated).async(execute: work)
    }

    func cancel() {
        task?.cancel()
        task = nil
    }
}

final class WorkspaceSearchResultsView: NSTableView, NSTableViewDataSource, NSTableViewDelegate {
    var results: [WorkspaceSearchResult] = []
    var onActivate: ((WorkspaceSearchResult) -> Void)?

    func configure() {
        headerView = nil
        rowHeight = 42
        style = .sourceList
        backgroundColor = .clear
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("result"))
        column.title = ""
        addTableColumn(column)
        delegate = self
        dataSource = self
    }

    func setResults(_ results: [WorkspaceSearchResult]) {
        self.results = results
        reloadData()
    }

    func numberOfRows(in tableView: NSTableView) -> Int { max(results.count, 1) }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        if results.isEmpty {
            return NSTextField(labelWithString: L10n.t("无搜索结果"))
        }
        let id = NSUserInterfaceItemIdentifier("resultCell")
        let cell = (makeView(withIdentifier: id, owner: self) as? NSTableCellView) ?? {
            let cell = NSTableCellView()
            cell.identifier = id
            let title = NSTextField(labelWithString: "")
            let path = NSTextField(labelWithString: "")
            title.translatesAutoresizingMaskIntoConstraints = false
            path.translatesAutoresizingMaskIntoConstraints = false
            path.textColor = .secondaryLabelColor
            path.font = .systemFont(ofSize: 11)
            path.lineBreakMode = .byTruncatingTail
            cell.addSubview(title)
            cell.addSubview(path)
            cell.textField = title
            NSLayoutConstraint.activate([
                title.leadingAnchor.constraint(equalTo: cell.leadingAnchor, constant: 8),
                title.trailingAnchor.constraint(equalTo: cell.trailingAnchor, constant: -6),
                title.topAnchor.constraint(equalTo: cell.topAnchor, constant: 4),
                path.leadingAnchor.constraint(equalTo: title.leadingAnchor),
                path.trailingAnchor.constraint(equalTo: title.trailingAnchor),
                path.topAnchor.constraint(equalTo: title.bottomAnchor, constant: 2),
            ])
            return cell
        }()
        let result = results[row]
        cell.textField?.stringValue = result.entry.name
        let pathLabel = cell.subviews.compactMap { $0 as? NSTextField }.dropFirst().first
        let detail = result.match.isEmpty ? result.entry.path : "\(result.entry.path) · \(result.match)"
        pathLabel?.stringValue = detail
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard selectedRow >= 0, selectedRow < results.count else { return }
        onActivate?(results[selectedRow])
    }
}
