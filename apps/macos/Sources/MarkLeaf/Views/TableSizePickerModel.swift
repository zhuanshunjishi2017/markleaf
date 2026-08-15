import Foundation

struct TableSize: Equatable {
    let rows: Int
    let columns: Int
}

enum TableSizePickerModel {
    static let visibleLimit = 10
    static let maxCustomSize = 100
    static let defaultSize = TableSize(rows: 3, columns: 3)

    static func clamped(rows: Int, columns: Int) -> TableSize {
        TableSize(
            rows: min(max(rows, 1), maxCustomSize),
            columns: min(max(columns, 1), maxCustomSize)
        )
    }

    static func parse(_ text: String) -> TableSize? {
        let parts = text.split(separator: ",", omittingEmptySubsequences: false)
        guard parts.count == 2,
              let rows = Int(parts[0].trimmingCharacters(in: .whitespacesAndNewlines)),
              let columns = Int(parts[1].trimmingCharacters(in: .whitespacesAndNewlines)),
              rows > 0,
              columns > 0,
              rows <= maxCustomSize,
              columns <= maxCustomSize else {
            return nil
        }
        return TableSize(rows: rows, columns: columns)
    }
}
