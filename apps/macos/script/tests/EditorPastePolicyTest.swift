import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else { fputs("FAIL: \(message)\n", stderr); exit(1) }
}

expect(EditorPastePolicy.contentKind(
    hasFinderFiles: true,
    hasBitmapImage: true,
    plainText: "text",
    html: "<b>text</b>"
) == .finderFiles, "Finder files should have first paste priority")
expect(EditorPastePolicy.contentKind(
    hasFinderFiles: false,
    hasBitmapImage: true,
    plainText: "text",
    html: "<b>text</b>"
) == .bitmapImage, "bitmap images should precede text and HTML")
expect(EditorPastePolicy.contentKind(
    hasFinderFiles: false,
    hasBitmapImage: false,
    plainText: "text",
    html: "<b>text</b>"
) == .textOrHTML, "text and HTML should be routed after image content")
expect(EditorPastePolicy.contentKind(
    hasFinderFiles: false,
    hasBitmapImage: false,
    plainText: nil,
    html: nil
) == .none, "an empty clipboard should have no paste route")

let richVisual = EditorPastePolicy.command(
    isSourceMode: false,
    plainText: "OpenAI",
    html: "<p><a href=\"https://openai.com\">OpenAI</a></p>"
)
expect(richVisual == EditorPasteCommand(
    command: "pasteClipboard",
    text: "OpenAI",
    html: "<p><a href=\"https://openai.com\">OpenAI</a></p>"
), "visual paste should send text and HTML to the shared decision path")

let source = EditorPastePolicy.command(
    isSourceMode: true,
    plainText: "# literal",
    html: "<h1>literal</h1>"
)
expect(source == EditorPasteCommand(command: "pasteText", text: "# literal", html: nil),
       "source mode should paste literal text and ignore HTML")

for html: String? in [nil, ""] {
    expect(EditorPastePolicy.command(isSourceMode: false, plainText: "# heading", html: html)
           == EditorPasteCommand(command: "pasteMarkdown", text: "# heading", html: nil),
           "visual text-only paste must parse Markdown like Windows")
}

let htmlOnly = EditorPastePolicy.command(
    isSourceMode: false,
    plainText: nil,
    html: "<p><strong>rich</strong></p>"
)
expect(htmlOnly == EditorPasteCommand(
    command: "pasteClipboard",
    text: nil,
    html: "<p><strong>rich</strong></p>"
), "HTML-only paste should retain the rich HTML fallback")

expect(EditorPastePolicy.command(isSourceMode: true, plainText: nil, html: "<b>rich</b>") == nil,
       "source mode should not turn HTML-only clipboard content into source text")
expect(EditorPastePolicy.command(isSourceMode: false, plainText: nil, html: nil) == nil,
       "empty clipboard content should not issue a command")

for command in ["pasteMarkdown", "pasteClipboard"] {
    expect(EditorPastePolicy.modifyingCommands.contains(command),
           "\(command) should be blocked for read-only documents")
}

let payload = EditorPastePolicy.payload(
    command: "pasteClipboard",
    text: "plain",
    html: "<p>plain</p>"
)
expect(payload["command"] as? String == "pasteClipboard", "payload should retain command")
expect(payload["text"] as? String == "plain", "payload should carry plain text")
expect(payload["html"] as? String == "<p>plain</p>", "payload should carry optional HTML")

let textOnlyPayload = EditorPastePolicy.payload(command: "pasteText", text: "literal", html: nil)
expect(textOnlyPayload["html"] == nil, "payload should omit absent HTML")
print("PASS")
