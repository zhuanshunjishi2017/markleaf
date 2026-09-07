import Foundation

struct SessionTabRecord: Codable, Equatable {
    var tabID: String
    var path: String?
    var title: String
    var untitledSequence: Int?
    var isDirty: Bool
    var revision: Int64
    var encoding: String
    var newLine: String
    var fingerprintModificationSeconds: Int64?
    var fingerprintSize: Int64?
    var cursorPosition: Int?
    var selectionAnchor: Int?
    var selectionHead: Int?
    var visualSelectionFrom: Int? = nil
    var visualSelectionTo: Int? = nil
    var sourceSelectionFrom: Int? = nil
    var sourceSelectionTo: Int? = nil
    var scrollTop: Double?
    var snapshotFileName: String?
}

struct SessionWindowRecord: Codable, Equatable {
    var windowID: String
    var frameX: Double?
    var frameY: Double?
    var frameWidth: Double?
    var frameHeight: Double?
    var workspacePath: String?
    var sidebarVisible: Bool?
    var sidebarTab: String?
    var sidebarWidth: Int?
    var outlineDetached: Bool?
    var outlineWidth: Int?
    var statusBarVisible: Bool?
    var tabOrder: [String]
    var activeTabID: String?
    var tabs: [SessionTabRecord]
}

struct SessionManifest: Codable, Equatable {
    var schemaVersion: Int
    var generation: Int64
    var savedAt: Date
    var windows: [SessionWindowRecord]
}

enum SessionManifestCodec {
    static let currentSchemaVersion = 1

    static func encode(_ manifest: SessionManifest) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(date.timeIntervalSinceReferenceDate)
        }
        return try encoder.encode(manifest)
    }

    static func decode(_ data: Data, diagnostics: inout [String]) -> SessionManifest? {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let value = try decoder.singleValueContainer().decode(Double.self)
            return Date(timeIntervalSinceReferenceDate: value)
        }
        guard let raw = try? decoder.decode(RawManifest.self, from: data) else {
            diagnostics.append("manifest unparseable")
            return nil
        }
        guard raw.schemaVersion == currentSchemaVersion else {
            diagnostics.append("unsupported schemaVersion \(raw.schemaVersion)")
            return nil
        }
        var windows: [SessionWindowRecord] = []
        for (index, rawWindow) in raw.windows.enumerated() {
            guard let window = rawWindow.record, !window.windowID.isEmpty else {
                diagnostics.append("window[\(index)] invalid, skipped")
                continue
            }
            windows.append(window)
        }
        return SessionManifest(schemaVersion: raw.schemaVersion, generation: raw.generation, savedAt: raw.savedAt, windows: windows)
    }
}

private struct RawManifest: Codable {
    struct RawWindow: Codable {
        var record: SessionWindowRecord?

        init(from decoder: Decoder) throws {
            record = try? SessionWindowRecord(from: decoder)
        }

        func encode(to encoder: Encoder) throws {
            try record?.encode(to: encoder)
        }
    }

    var schemaVersion: Int
    var generation: Int64
    var savedAt: Date
    var windows: [RawWindow]
}
