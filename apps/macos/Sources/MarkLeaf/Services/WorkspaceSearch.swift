import AppKit

struct WorkspaceSearchResult {
    let entry: WorkspaceEntry
    let folderName: String
    let lastWriteTime: Date
    let snippet: String
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
        let rootName = (root as NSString).lastPathComponent
        var work: DispatchWorkItem!
        work = DispatchWorkItem {
            let fm = FileManager.default
            var stack = [root]
            var results: [WorkspaceSearchResult] = []
            while let directory = stack.popLast() {
                if work.isCancelled { return }
                guard let items = try? fm.contentsOfDirectory(atPath: directory) else { continue }
                for name in items where !name.hasPrefix(".") {
                    if work.isCancelled { return }
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
                    if work.isCancelled { return }
                    let lowerName = name.lowercased()
                    let content = (try? String(contentsOfFile: path, encoding: .utf8)) ?? ""
                    let nameMatch = lowerName.contains(normalized)
                    guard nameMatch || content.lowercased().contains(normalized) else { continue }
                    // 与 Windows 一致：文件名命中显示首行，内容命中显示关键词附近的上下文。
                    let snippet = WorkspaceSearchPolicy.snippet(
                        content: content,
                        query: normalized,
                        nameMatches: nameMatch
                    )
                    let parent = (path as NSString).deletingLastPathComponent
                    let folderName: String
                    if parent == root {
                        folderName = rootName
                    } else {
                        folderName = String(parent.dropFirst(root.count + 1))
                    }
                    let lastWriteTime = (try? fm.attributesOfItem(atPath: path)[.modificationDate] as? Date) ?? Date.distantPast
                    results.append(WorkspaceSearchResult(
                        entry: WorkspaceEntry(name: name, path: path, isDirectory: false),
                        folderName: folderName,
                        lastWriteTime: lastWriteTime,
                        snippet: snippet))
                }
            }
            results.sort { $0.entry.path.localizedCaseInsensitiveCompare($1.entry.path) == .orderedAscending }
            guard !work.isCancelled else { return }
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

/// 修改时间文案（对齐 Windows WorkspaceDocumentTimeFormatter）：
/// 今天/昨天带前缀，其余同年日期显示月日，更早显示年/月/日。
enum WorkspaceDocumentTimeFormatter {
    static func format(_ date: Date, now: Date = Date()) -> String {
        let calendar = Calendar.current
        let time = timeFormatter.string(from: date)
        if calendar.isDate(date, inSameDayAs: now) {
            return L10n.f("今天 %@", time)
        }
        if let yesterday = calendar.date(byAdding: .day, value: -1, to: calendar.startOfDay(for: now)),
           calendar.isDate(date, inSameDayAs: yesterday) {
            return L10n.f("昨天 %@", time)
        }
        if calendar.isDate(date, equalTo: now, toGranularity: .year) {
            return monthDayFormatter.string(from: date)
        }
        return yearFormatter.string(from: date)
    }

    private static var locale: Locale {
        let code = SettingsService.shared.settings.displayLanguage
        let identifier: String
        switch code {
        case "zh-Hant": identifier = "zh_TW"
        case "ja": identifier = "ja_JP"
        case "en": identifier = "en_US"
        default: identifier = "zh_CN"
        }
        return Locale(identifier: identifier)
    }

    private static var timeFormatter: DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = locale
        formatter.dateFormat = "HH:mm"
        return formatter
    }

    private static var monthDayFormatter: DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = locale
        formatter.setLocalizedDateFormatFromTemplate("MMMd")
        return formatter
    }

    private static var yearFormatter: DateFormatter {
        let formatter = DateFormatter()
        formatter.locale = locale
        formatter.dateFormat = "yyyy/M/d"
        return formatter
    }
}

final class WorkspaceSearchResultsView: NSTableView, NSTableViewDataSource, NSTableViewDelegate {
    enum State {
        case results([WorkspaceSearchResult])
        case searching
        case empty
    }

    private(set) var state: State = .empty
    var onActivate: ((WorkspaceSearchResult) -> Void)?

    func configure() {
        headerView = nil
        rowHeight = 66
        style = .sourceList
        backgroundColor = .clear
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("result"))
        column.title = ""
        addTableColumn(column)
        delegate = self
        dataSource = self
    }

    func setSearching() {
        state = .searching
        reloadData()
    }

    func setResults(_ results: [WorkspaceSearchResult]) {
        state = results.isEmpty ? .empty : .results(results)
        reloadData()
    }

    func numberOfRows(in tableView: NSTableView) -> Int {
        switch state {
        case .searching, .empty: return 1
        case .results(let results): return results.count
        }
    }

    func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool {
        if case .results = state { return true }
        return false
    }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        switch state {
        case .searching:
            return placeholder(L10n.t("搜索中…"))
        case .empty:
            return placeholder(L10n.t("无搜索结果"))
        case .results(let results):
            return resultCell(results[row])
        }
    }

    private func placeholder(_ text: String) -> NSView {
        let label = NSTextField(labelWithString: text)
        label.textColor = .secondaryLabelColor
        label.font = .systemFont(ofSize: 12)
        label.isSelectable = false
        label.translatesAutoresizingMaskIntoConstraints = false
        let container = NSView()
        container.addSubview(label)
        NSLayoutConstraint.activate([
            label.leadingAnchor.constraint(equalTo: container.leadingAnchor, constant: 12),
            label.centerYAnchor.constraint(equalTo: container.centerYAnchor),
        ])
        return container
    }

    private func resultCell(_ result: WorkspaceSearchResult) -> NSView {
        let id = NSUserInterfaceItemIdentifier("resultCell")
        let cell = (makeView(withIdentifier: id, owner: self) as? SearchResultCellView) ?? {
            let cell = SearchResultCellView()
            cell.identifier = id
            return cell
        }()
        cell.folderLabel.stringValue = result.folderName
        cell.timeLabel.stringValue = WorkspaceDocumentTimeFormatter.format(result.lastWriteTime)
        cell.textField?.stringValue = result.entry.name
        cell.snippetLabel.stringValue = result.snippet
        cell.snippetLabel.isHidden = result.snippet.isEmpty
        return cell
    }

    func tableViewSelectionDidChange(_ notification: Notification) {
        guard case .results(let results) = state,
              selectedRow >= 0, selectedRow < results.count else { return }
        onActivate?(results[selectedRow])
    }
}

/// 搜索结果行：文件夹 + 修改时间 / 文件名 / 内容片段，对齐 Windows SearchResultsView。
private final class SearchResultCellView: NSTableCellView {
    let folderIconView = NSImageView()
    let folderLabel = NSTextField(labelWithString: "")
    let timeLabel = NSTextField(labelWithString: "")
    let snippetLabel = NSTextField(labelWithString: "")

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        build()
    }

    required init?(coder: NSCoder) {
        fatalError("init(coder:) has not been implemented")
    }

    private func build() {
        let title = NSTextField(labelWithString: "")
        folderIconView.translatesAutoresizingMaskIntoConstraints = false
        folderIconView.image = NSImage(
            systemSymbolName: "folder.fill",
            accessibilityDescription: nil
        )
        folderIconView.contentTintColor = .tertiaryLabelColor
        folderIconView.widthAnchor.constraint(equalToConstant: 14).isActive = true
        folderIconView.heightAnchor.constraint(equalToConstant: 14).isActive = true
        for label in [folderLabel, timeLabel, title, snippetLabel] {
            label.translatesAutoresizingMaskIntoConstraints = false
            label.lineBreakMode = .byTruncatingTail
            label.maximumNumberOfLines = 1
            label.usesSingleLineMode = true
        }
        folderLabel.font = .systemFont(ofSize: 11)
        folderLabel.textColor = .tertiaryLabelColor
        timeLabel.font = .systemFont(ofSize: 11)
        timeLabel.textColor = .tertiaryLabelColor
        timeLabel.alignment = .right
        title.font = .systemFont(ofSize: 13, weight: .medium)
        snippetLabel.font = .systemFont(ofSize: 11)
        snippetLabel.textColor = .secondaryLabelColor

        let metaRow = NSStackView(views: [folderIconView, folderLabel, NSView(), timeLabel])
        metaRow.orientation = .horizontal
        metaRow.spacing = 6
        metaRow.translatesAutoresizingMaskIntoConstraints = false
        addSubview(metaRow)
        addSubview(title)
        addSubview(snippetLabel)
        textField = title
        NSLayoutConstraint.activate([
            metaRow.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 8),
            metaRow.trailingAnchor.constraint(equalTo: trailingAnchor, constant: -8),
            metaRow.topAnchor.constraint(equalTo: topAnchor, constant: 6),
            title.leadingAnchor.constraint(equalTo: metaRow.leadingAnchor),
            title.trailingAnchor.constraint(equalTo: metaRow.trailingAnchor),
            title.topAnchor.constraint(equalTo: metaRow.bottomAnchor, constant: 3),
            snippetLabel.leadingAnchor.constraint(equalTo: metaRow.leadingAnchor),
            snippetLabel.trailingAnchor.constraint(equalTo: metaRow.trailingAnchor),
            snippetLabel.topAnchor.constraint(equalTo: title.bottomAnchor, constant: 3),
            snippetLabel.bottomAnchor.constraint(lessThanOrEqualTo: bottomAnchor, constant: -6),
        ])
    }
}
