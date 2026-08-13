import Foundation

struct ProbeSettings { var displayLanguage = "zh-Hans" }

final class SettingsService {
    static let shared = SettingsService()
    var settings = ProbeSettings()
}

@main
enum L10nStartupProbe {
    static func main() {
        precondition(L10n.translate("保存", language: "ja") == "保存")
    }
}
