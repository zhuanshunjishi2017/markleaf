import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(EditorCommandRouter.scope(for: "openFolder") == .workspace, "openFolder is workspace scope")
expect(EditorCommandRouter.scope(for: "closeFolder") == .workspace, "closeFolder is workspace scope")
expect(EditorCommandRouter.scope(for: "toggleSidebar") == .workspace, "sidebar toggle is workspace scope")
expect(EditorCommandRouter.scope(for: "treeView") == .workspace, "workspace view modes are workspace scope")
expect(EditorCommandRouter.scope(for: "newWindow") == .windowLevel, "newWindow is window scope")
expect(EditorCommandRouter.scope(for: "toggleFocusMode") == .windowLevel, "focus mode is window scope")
expect(EditorCommandRouter.scope(for: "save") == .document, "save routes to the active document")
expect(EditorCommandRouter.scope(for: "toggleBold") == .document, "formatting routes to the active document")
expect(EditorCommandRouter.scope(for: "someUnknownCommand") == .document, "unknown commands default to document scope")
print("PASS")
