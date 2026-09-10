import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

expect(CodeBlockLanguageCatalog.commonLanguages.prefix(6) == [
    "swift", "c", "cpp", "objective-c", "java", "kotlin",
], "common language list should start with the agreed platform-neutral choices")
expect(CodeBlockLanguageCatalog.commonLanguages.contains("python"), "python should be available")
expect(CodeBlockLanguageCatalog.commonLanguages.contains("typescript"), "typescript should be available")
expect(CodeBlockLanguageCatalog.commonLanguages.contains("mermaid"), "mermaid should be available")
expect(CodeBlockLanguageCatalog.normalized("  swift \n") == "swift", "selection should trim whitespace")
expect(CodeBlockLanguageCatalog.normalized("   ") == "", "blank selection should mean unspecified")
expect(CodeBlockLanguageCatalog.normalized("My-Custom-Language") == "My-Custom-Language", "custom values should be preserved")

print("CodeBlockLanguageCatalog tests passed")
