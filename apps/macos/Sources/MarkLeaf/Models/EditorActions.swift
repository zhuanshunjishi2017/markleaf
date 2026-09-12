import Foundation

struct EditorActionState: Equatable {
    let enabled: Bool
    let checked: Bool

    static func decode(_ payload: Any?) -> [String: Self] {
        guard let values = payload as? [String: [String: Any]] else { return [:] }
        return values.compactMapValues { value in
            guard let enabled = value["enabled"] as? Bool,
                  let checked = value["checked"] as? Bool else { return nil }
            return Self(enabled: enabled, checked: checked)
        }
    }
}
