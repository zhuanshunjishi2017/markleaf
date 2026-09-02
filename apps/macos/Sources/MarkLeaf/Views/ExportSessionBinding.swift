import Foundation

struct ExportBinding: Equatable {
    let tabID: DocumentTabID
    let contentRevision: Int64
}

/// Keeps a detached tab's editor alive while its export window remains open.
final class ExportSessionLease {
    let tabID: DocumentTabID
    let session: EditorSession
    let container: EditorWebContainerView?

    init(tabID: DocumentTabID, session: EditorSession, container: EditorWebContainerView?) {
        self.tabID = tabID
        self.session = session
        self.container = container
    }
}
