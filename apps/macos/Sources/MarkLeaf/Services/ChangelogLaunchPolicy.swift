import Foundation

enum ChangelogLaunchPolicy {
    static func shouldOpen(
        currentVersion: String,
        lastShownVersion: String?,
        settingsFileExisted: Bool
    ) -> Bool {
        guard let current = normalizedVersion(currentVersion) else { return false }
        guard let lastShown = lastShownVersion.map(normalizedVersion) else {
            return settingsFileExisted
        }
        guard let lastShown else { return false }
        return compare(current, lastShown) > 0
    }

    private static func normalizedVersion(_ text: String) -> [Int]? {
        let numericPrefix = text.prefix { $0.isNumber || $0 == "." }
        let parts = numericPrefix
            .split(separator: ".", omittingEmptySubsequences: false)
            .compactMap { Int($0) }
        guard !parts.isEmpty else { return nil }
        return parts
    }

    private static func compare(_ left: [Int], _ right: [Int]) -> Int {
        let count = max(left.count, right.count)
        for index in 0..<count {
            let leftValue = index < left.count ? left[index] : 0
            let rightValue = index < right.count ? right[index] : 0
            if leftValue != rightValue {
                return leftValue < rightValue ? -1 : 1
            }
        }
        return 0
    }
}
