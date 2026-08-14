import Foundation

enum IncomingFileRouter {
    static func normalized(_ url: URL) -> URL {
        url.standardizedFileURL.resolvingSymlinksInPath()
    }

    static func route(
        urls: [URL],
        mode: ExternalFileOpenMode,
        activeEditor: Bool,
        openDocuments: [URL],
        activateExisting: (URL) -> Void,
        replaceActive: (URL) -> Void,
        createWindow: (URL) -> Void
    ) {
        let open = Set(openDocuments.filter(\.isFileURL).map(normalized))
        var seenInEvent = Set<URL>()

        for (eventIndex, rawURL) in urls.enumerated() where rawURL.isFileURL {
            let url = normalized(rawURL)
            guard seenInEvent.insert(url).inserted else { continue }
            switch IncomingFileRoutingPolicy.action(
                mode: mode,
                eventIndex: eventIndex,
                hasActiveEditor: activeEditor,
                hasOpenDuplicate: open.contains(url)
            ) {
            case .activateExisting: activateExisting(url)
            case .replaceActive: replaceActive(url)
            case .createWindow: createWindow(url)
            }
        }
    }
}
