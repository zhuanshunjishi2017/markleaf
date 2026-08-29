import Foundation

enum WelcomeResource {
    private static let supported = Set(["zh-Hans", "zh-Hant", "en", "ja"])

    static func candidateFileNames(displayLanguage: String) -> [String] {
        let language = supported.contains(displayLanguage) ? displayLanguage : "zh-Hans"
        let requested = "welcome.\(language).md"
        let fallback = "welcome.zh-Hans.md"
        return requested == fallback ? [fallback] : [requested, fallback]
    }

    static func bundledURL(in bundle: Bundle, displayLanguage: String) -> URL? {
        for name in candidateFileNames(displayLanguage: displayLanguage) {
            let stem = String(name.dropLast(3))
            if let url = bundle.url(forResource: stem, withExtension: "md", subdirectory: "Welcome") {
                return url
            }
        }
        return nil
    }

    static func cachedURL(cacheDirectory: URL) -> URL {
        cacheDirectory.appendingPathComponent("welcome.md")
    }
}
