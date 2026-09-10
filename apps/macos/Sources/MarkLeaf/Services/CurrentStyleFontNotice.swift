import Foundation

enum CurrentStyleFontNotice {
    static func missingPacks(
        styleID: String?, packs: [OptionalFontPack], statuses: [OptionalFontPackStatus]
    ) -> [OptionalFontPack] {
        guard let styleID else { return [] }
        return packs.enumerated().compactMap { index, pack in
            guard pack.styleID == styleID, statuses.indices.contains(index) else { return nil }
            switch statuses[index] {
            case .missing, .partial: return pack
            // Unavailable packs have no verified font inventory; absence is unknown.
            case .installedByMarkLeaf, .installedExternally, .unavailable: return nil
            }
        }
    }
}
