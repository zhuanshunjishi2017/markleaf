import Darwin
import Foundation

struct DocumentFileVersion: Equatable {
    let device: UInt64
    let inode: UInt64
    let modificationSeconds: Int64
    let modificationNanoseconds: Int64
    let size: Int64

    static func read(from url: URL) throws -> DocumentFileVersion {
        let descriptor = open(url.path, O_RDONLY)
        guard descriptor >= 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        defer { close(descriptor) }
        var attributes = stat()
        guard fstat(descriptor, &attributes) == 0 else {
            throw POSIXError(POSIXErrorCode(rawValue: errno) ?? .EIO)
        }
        return DocumentFileVersion(
            device: UInt64(attributes.st_dev),
            inode: UInt64(attributes.st_ino),
            modificationSeconds: Int64(attributes.st_mtimespec.tv_sec),
            modificationNanoseconds: Int64(attributes.st_mtimespec.tv_nsec),
            size: Int64(attributes.st_size)
        )
    }
}

struct ExternalDocumentWatchGeneration {
    private(set) var current = 0

    mutating func beginWatch() -> Int {
        current += 1
        return current
    }

    mutating func invalidate() {
        current += 1
    }

    func isCurrent(_ generation: Int) -> Bool {
        generation == current
    }
}

enum ExternalDocumentChangeDecision: Equatable {
    case ignore
    case rebindAndRecheck
    case presentExternalChange
    case missing
}

final class ExternalDocumentChangeTracker {
    private var acceptedVersion: DocumentFileVersion?
    private var needsRecheck = false
    private var isSelfWriteInProgress = false

    func acceptCurrentVersion(at url: URL) throws {
        acceptedVersion = try DocumentFileVersion.read(from: url)
        needsRecheck = false
    }

    func beginSelfWrite() {
        needsRecheck = false
        isSelfWriteInProgress = true
    }

    func finishSelfWrite(at url: URL) throws {
        defer { isSelfWriteInProgress = false }
        try acceptCurrentVersion(at: url)
    }

    func cancelSelfWrite() {
        needsRecheck = false
        isSelfWriteInProgress = false
    }

    func decision(forEventAt url: URL) throws -> ExternalDocumentChangeDecision {
        guard !isSelfWriteInProgress else { return .ignore }
        guard FileManager.default.fileExists(atPath: url.path) else {
            return .missing
        }
        let currentVersion = try DocumentFileVersion.read(from: url)
        guard acceptedVersion != currentVersion else {
            return .ignore
        }
        guard needsRecheck else {
            needsRecheck = true
            return .rebindAndRecheck
        }
        return .presentExternalChange
    }
}
