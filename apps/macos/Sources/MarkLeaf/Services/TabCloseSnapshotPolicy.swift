import Foundation

/// Resolves the Markdown buffer used when a tab is closed before its WebView
/// has completed the initial ready handshake.
enum TabCloseSnapshotPolicy {
    static func fallbackMarkdown(isReady: Bool, pendingMarkdown: String?) -> String? {
        guard !isReady else { return nil }
        return pendingMarkdown ?? ""
    }
}
