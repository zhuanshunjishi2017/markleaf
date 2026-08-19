import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let boldPrompt = UnsafeEmphasisPolicy.prompt(for: "bold")
expect(boldPrompt.kind == .bold, "bold requests should map to the bold prompt")
expect(boldPrompt.titleKey == "Markdown 粗体标记可能不安全", "bold prompt should expose a localized title key")

let italicPrompt = UnsafeEmphasisPolicy.prompt(for: "italic")
expect(italicPrompt.kind == .italic, "italic requests should map to the italic prompt")
expect(italicPrompt.messageKey == "这段斜体标记可能与相邻字符连在一起。请选择保留 Markdown 字面量，或转换为 HTML 标签。",
       "italic prompt should explain the unsafe delimiter")

expect(UnsafeEmphasisPolicy.savedAction(value: "literal", suppressPrompt: true) == .literal,
       "a saved literal preference should bypass the dialog")
expect(UnsafeEmphasisPolicy.savedAction(value: "html", suppressPrompt: true) == .html,
       "a saved HTML preference should bypass the dialog")
expect(UnsafeEmphasisPolicy.savedAction(value: "invalid", suppressPrompt: true) == nil,
       "an invalid preference should not bypass the dialog")
expect(UnsafeEmphasisPolicy.savedAction(value: "literal", suppressPrompt: false) == nil,
       "the action should not bypass the dialog unless suppression is enabled")

let response = UnsafeEmphasisPolicy.response(requestID: "bold:4:8:value", action: .html)
expect(response.requestID == "bold:4:8:value", "responses should preserve the frontend request id")
expect(response.action.rawValue == "html", "responses should contain only an allowed action")

print("PASS")
