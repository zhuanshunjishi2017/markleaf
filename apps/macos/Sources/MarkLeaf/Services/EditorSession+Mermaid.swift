import Foundation

extension EditorSession {
    func insertMermaid() {
        guard EditorMenuPolicy.allows(.insertMermaid, state: editorMenuState) else { return }
        execute("insertMermaid")
    }

    func editSelectedMermaid() {
        guard EditorMenuPolicy.allows(.editMermaid, state: editorMenuState) else { return }
        execute("editMermaid")
    }

    func rerenderSelectedMermaid() {
        guard EditorMenuPolicy.allows(.rerenderMermaid, state: editorMenuState) else { return }
        execute("rerenderMermaid")
    }

    func rerenderAllMermaid() {
        guard EditorMenuPolicy.allows(.rerenderAllMermaid, state: editorMenuState) else { return }
        execute("rerenderAllMermaid")
    }

    func deleteSelectedMermaid() {
        guard EditorMenuPolicy.allows(.deleteMermaid, state: editorMenuState) else { return }
        execute("deleteMermaid")
    }
}
