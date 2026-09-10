import Foundation

enum ExternalFileOpenMode: String, Codable, CaseIterable {
    case newWindow
    case newTab
    case currentWindow
}
