enum AppVersion {
    static func displayString(version: String, build: String) -> String {
        "Version \(version) (Build \(build))"
    }

    static func displayString(infoDictionary: [String: Any]?) -> String {
        guard let version = infoDictionary?["CFBundleShortVersionString"] as? String,
              !version.isEmpty,
              let build = infoDictionary?["CFBundleVersion"] as? String,
              !build.isEmpty else {
            return "Version unavailable"
        }
        return displayString(version: version, build: build)
    }
}
