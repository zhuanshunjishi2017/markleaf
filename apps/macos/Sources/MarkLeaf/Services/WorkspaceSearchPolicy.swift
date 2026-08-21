import Foundation

struct WorkspaceRevealEntry: Equatable {
    let path: String
    let isDirectory: Bool
}

enum WorkspaceRevealStep: Equatable {
    case invalid
    case waitingForRoot
    case loadDirectory(path: String, expandedDirectories: [String])
    case selectFile(path: String, expandedDirectories: [String])
}

enum WorkspaceRevealExpansionStep: Equatable {
    case waitingForVisibleDirectory
    case expandDirectory(String)
    case ready
}

enum WorkspaceSearchPolicy {
    static func snippet(content: String, query: String, nameMatches: Bool) -> String {
        let normalized = query.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let lines = content.split(whereSeparator: { $0 == "\n" }).map(String.init)
        guard !normalized.isEmpty else { return "" }
        if nameMatches { return lines.first ?? "" }
        guard let index = lines.firstIndex(where: { $0.lowercased().contains(normalized) }) else { return "" }
        let start = max(0, index - 1)
        let end = min(lines.count, index + 2)
        return lines[start..<end].joined(separator: "\n")
    }

    /// Computes the next event-driven step for revealing a search result in a lazy tree.
    /// A missing child cache means "load this directory", not "retry after a fixed delay".
    static func nextRevealStep(
        root: String,
        target: String,
        rootEntries: [WorkspaceRevealEntry],
        childrenByDirectory: [String: [WorkspaceRevealEntry]]
    ) -> WorkspaceRevealStep {
        let rootPath = normalizedPath(root)
        let targetPath = normalizedPath(target)
        guard targetPath.hasPrefix(rootPath + "/") else { return .invalid }

        let normalizedRootEntries = rootEntries.map(normalizedEntry)
        let normalizedChildren = Dictionary(uniqueKeysWithValues: childrenByDirectory.map { key, value in
            (normalizedPath(key), value.map(normalizedEntry))
        })
        guard !normalizedRootEntries.isEmpty else { return .waitingForRoot }

        var directoryPaths: [String] = []
        var directory = URL(fileURLWithPath: targetPath).deletingLastPathComponent().path
        while directory != rootPath && directory.hasPrefix(rootPath + "/") {
            directoryPaths.insert(directory, at: 0)
            directory = URL(fileURLWithPath: directory).deletingLastPathComponent().path
        }

        var candidates = normalizedRootEntries
        var expandedDirectories: [String] = []
        for directoryPath in directoryPaths {
            guard candidates.contains(where: { $0.isDirectory && $0.path == directoryPath }) else {
                return .invalid
            }
            expandedDirectories.append(directoryPath)
            guard let children = normalizedChildren[directoryPath] else {
                return .loadDirectory(
                    path: directoryPath,
                    expandedDirectories: expandedDirectories
                )
            }
            candidates = children
        }

        guard candidates.contains(where: { !$0.isDirectory && $0.path == targetPath }) else {
            return .invalid
        }
        return .selectFile(path: targetPath, expandedDirectories: expandedDirectories)
    }

    /// AppKit inserts nested outline rows on a later run-loop turn. Expand one visible
    /// ancestor at a time so selection never races ahead of row creation.
    static func nextExpansionStep(
        directories: [String],
        visibleDirectories: Set<String>,
        expandedDirectories: Set<String>
    ) -> WorkspaceRevealExpansionStep {
        let visible = Set(visibleDirectories.map(normalizedPath))
        let expanded = Set(expandedDirectories.map(normalizedPath))
        for path in directories.map(normalizedPath) {
            guard visible.contains(path) else { return .waitingForVisibleDirectory }
            if !expanded.contains(path) {
                return .expandDirectory(path)
            }
        }
        return .ready
    }

    private static func normalizedEntry(_ entry: WorkspaceRevealEntry) -> WorkspaceRevealEntry {
        WorkspaceRevealEntry(path: normalizedPath(entry.path), isDirectory: entry.isDirectory)
    }

    private static func normalizedPath(_ path: String) -> String {
        URL(fileURLWithPath: path).standardizedFileURL.path
    }
}
