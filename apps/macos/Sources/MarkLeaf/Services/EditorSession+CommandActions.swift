extension EditorSession {
    func editorAction(_ command: String) -> EditorActionState? {
        let aliases = [
            "copyPlain": "copyPlainText", "copyAs": "copyMarkdown",
            "insertLink": "setLink", "insertImageFromUrl": "insertImage",
            "rotateImage": "rotateImageClockwise", "declareCodeLanguage": "setCodeBlockLanguage",
            "editTableCaption": "setTableCaption", "editImageCaption": "setImageCaption",
            "sourceMode": "toggleSourceMode", "toggleEditorFocusMode": "setEditorFocusMode",
            "toggleTypewriterMode": "setEditorTypewriterMode", "tableEditing": "addRowAfter",
            "resizeImage100": "resizeImage", "resizeImage75": "resizeImage",
            "resizeImage90": "resizeImage", "resizeImage50": "resizeImage",
        ]
        return editorActions[aliases[command] ?? command]
    }

    func editorCommandEnabled(_ command: String) -> Bool {
        guard editorAction(command)?.enabled == true else { return false }
        // Clipboard contents are supplied by the platform, not inferred from the document.
        return command != "paste" && command != "pastePlainText" || clipboardHasContent
    }
}
