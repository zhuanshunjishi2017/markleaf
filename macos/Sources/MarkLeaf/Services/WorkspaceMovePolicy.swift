import Foundation

enum WorkspaceMoveError: Error, Equatable {
    case missingSource
    case invalidTarget
    case sameParent
    case outsideWorkspace
    case descendantTarget
    case destinationExists
}

enum WorkspaceMoveDisposition: Equatable {
    case noOp
    case move(URL)
}

enum WorkspaceMovePolicy {
    static func disposition(
        source: URL,
        targetDirectory: URL,
        workspaceRoot: URL,
        fileManager: FileManager = .default
    ) throws -> WorkspaceMoveDisposition {
        do {
            return .move(try destination(
                source: source,
                targetDirectory: targetDirectory,
                workspaceRoot: workspaceRoot,
                fileManager: fileManager
            ))
        } catch WorkspaceMoveError.sameParent {
            return .noOp
        }
    }

    static func destination(
        source: URL,
        targetDirectory: URL,
        workspaceRoot: URL,
        fileManager: FileManager = .default
    ) throws -> URL {
        let source = source.standardizedFileURL.resolvingSymlinksInPath()
        let targetDirectory = targetDirectory.standardizedFileURL.resolvingSymlinksInPath()
        let workspaceRoot = workspaceRoot.standardizedFileURL.resolvingSymlinksInPath()

        var sourceIsDirectory: ObjCBool = false
        guard fileManager.fileExists(atPath: source.path, isDirectory: &sourceIsDirectory) else {
            throw WorkspaceMoveError.missingSource
        }
        var targetIsDirectory: ObjCBool = false
        guard fileManager.fileExists(atPath: targetDirectory.path, isDirectory: &targetIsDirectory),
              targetIsDirectory.boolValue else {
            throw WorkspaceMoveError.invalidTarget
        }
        guard isInside(source, root: workspaceRoot), isInside(targetDirectory, root: workspaceRoot) else {
            throw WorkspaceMoveError.outsideWorkspace
        }
        guard source.deletingLastPathComponent() != targetDirectory else {
            throw WorkspaceMoveError.sameParent
        }
        if sourceIsDirectory.boolValue, isInside(targetDirectory, root: source) {
            throw WorkspaceMoveError.descendantTarget
        }

        let destination = targetDirectory.appendingPathComponent(source.lastPathComponent)
        guard !fileManager.fileExists(atPath: destination.path) else {
            throw WorkspaceMoveError.destinationExists
        }
        return destination
    }

    private static func isInside(_ candidate: URL, root: URL) -> Bool {
        candidate == root || candidate.path.hasPrefix(root.path + "/")
    }
}
