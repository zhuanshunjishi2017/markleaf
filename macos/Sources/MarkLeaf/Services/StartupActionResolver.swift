import Foundation

struct StartupPlan: Equatable {
    enum Operation: Equatable {
        case newDocument
        case openExplicitFile(String)
        case openWorkspace(String)
        case openFile(String)
        case openWorkspaceAndFile(workspace: String, file: String)
    }

    enum Notice: Equatable {
        case missingWorkspace
        case missingFile
        case missingWorkspaceAndFile
    }

    let operation: Operation
    let notice: Notice?
}

enum StartupActionResolver {
    static func resolve(
        action: AppSettings.StartupAction,
        lastFolder: String?,
        lastFile: String?,
        explicitFile: String?,
        isDirectory: (String) -> Bool,
        isFile: (String) -> Bool
    ) -> StartupPlan {
        if let explicitFile {
            return .init(operation: .openExplicitFile(explicitFile), notice: nil)
        }

        switch action {
        case .newDocument:
            return .init(operation: .newDocument, notice: nil)
        case .openLastWorkspace:
            guard let lastFolder, isDirectory(lastFolder) else {
                return .init(operation: .newDocument, notice: .missingWorkspace)
            }
            return .init(operation: .openWorkspace(lastFolder), notice: nil)
        case .openLastWorkspaceAndFiles:
            let folder = lastFolder.flatMap { isDirectory($0) ? $0 : nil }
            let file = lastFile.flatMap { isFile($0) ? $0 : nil }

            switch (folder, file) {
            case let (.some(folder), .some(file)):
                return .init(operation: .openWorkspaceAndFile(workspace: folder, file: file), notice: nil)
            case let (.some(folder), .none):
                return .init(operation: .openWorkspace(folder), notice: .missingFile)
            case let (.none, .some(file)):
                return .init(operation: .openFile(file), notice: .missingWorkspace)
            case (.none, .none):
                return .init(operation: .newDocument, notice: .missingWorkspaceAndFile)
            }
        }
    }
}
