import AppKit
import CoreText
import CryptoKit
import Foundation

protocol OptionalFontArchiveProviding {
    func listEntries(in archiveURL: URL) throws -> [String]
    func extract(entry: String, from archiveURL: URL, to destinationURL: URL) throws
}

protocol OptionalFontMetadataProviding {
    func postScriptNames(at fontURL: URL) throws -> Set<String>
}

protocol OptionalFontRegistering {
    func registerFont(at url: URL) throws
    func unregisterFont(at url: URL) throws
    func isRegisteredByMarkLeaf(at url: URL) -> Bool
    func isFontAvailable(postScriptName: String) -> Bool
}

enum OptionalFontArchiveError: Error {
    case commandFailed
    case invalidEntryList
}

struct SystemZipArchive: OptionalFontArchiveProviding {
    func listEntries(in archiveURL: URL) throws -> [String] {
        let output = try run(arguments: ["-Z1", archiveURL.path])
        guard let text = String(data: output, encoding: .utf8) else {
            throw OptionalFontArchiveError.invalidEntryList
        }
        return text.split(separator: "\n", omittingEmptySubsequences: true).map(String.init)
    }

    func extract(entry: String, from archiveURL: URL, to destinationURL: URL) throws {
        let output = try run(arguments: ["-p", archiveURL.path, entry])
        try FileManager.default.createDirectory(
            at: destinationURL.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        try output.write(to: destinationURL, options: .atomic)
    }

    private func run(arguments: [String]) throws -> Data {
        let process = Process()
        let output = Pipe()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        process.arguments = arguments
        process.standardOutput = output
        process.standardError = FileHandle.nullDevice
        try process.run()
        let data = output.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw OptionalFontArchiveError.commandFailed
        }
        return data
    }
}

enum OptionalFontInstallerError: Error {
    case digestMismatch
    case alreadyInstalled
    case unexpectedPostScriptNames(String)
    case registrationFailed
    case downloadFailed
    case invalidHTTPResponse
    case httpStatus(Int)
    case invalidFontData(String)
}

enum OptionalFontPackStatus: Equatable {
    case unavailable(String)
    case installedByMarkLeaf
    case installedExternally
    case partial
    case missing
}

struct OptionalFontActionState: Equatable {
    let canInstall: Bool
    let canUninstall: Bool
    let canOpenLicense: Bool
}

enum OptionalFontPresentation {
    static func actions(
        for pack: OptionalFontPack,
        status: OptionalFontPackStatus
    ) -> OptionalFontActionState {
        let canOpenLicense = pack.licenseURL != nil
        switch status {
        case .missing, .partial:
            return OptionalFontActionState(
                canInstall: pack.isInstallable,
                canUninstall: status == .partial,
                canOpenLicense: canOpenLicense
            )
        case .installedByMarkLeaf:
            return OptionalFontActionState(canInstall: false, canUninstall: true, canOpenLicense: canOpenLicense)
        case .installedExternally:
            return OptionalFontActionState(canInstall: false, canUninstall: false, canOpenLicense: canOpenLicense)
        case .unavailable:
            return OptionalFontActionState(canInstall: false, canUninstall: false, canOpenLicense: false)
        }
    }
}

struct OptionalFontInstallerCore {
    let storageRoot: URL
    let archive: OptionalFontArchiveProviding
    let metadata: OptionalFontMetadataProviding
    let registrar: OptionalFontRegistering
    private let fileManager = FileManager.default

    func installDownloadedArchive(_ archiveURL: URL, pack: OptionalFontPack) throws -> [URL] {
        let digest = try Self.sha256(of: archiveURL)
        guard OptionalFontCatalog.validatesSHA256(digest, for: pack) else {
            throw OptionalFontInstallerError.digestMismatch
        }

        let archiveEntries = try archive.listEntries(in: archiveURL)
        let validated = try OptionalFontCatalog.validatedArchiveEntries(archiveEntries, for: pack)
        let stage = storageRoot.appendingPathComponent(".staging-\(UUID().uuidString)", isDirectory: true)
        try fileManager.createDirectory(at: stage, withIntermediateDirectories: true)
        defer { try? fileManager.removeItem(at: stage) }

        for font in pack.files {
            guard let entry = validated.fontEntries[font.fileName] else {
                throw OptionalFontCatalogError.missingFontFiles([font.fileName])
            }
            let target = stage.appendingPathComponent(font.fileName)
            try archive.extract(entry: entry, from: archiveURL, to: target)
            let actualNames = try metadata.postScriptNames(at: target)
            guard actualNames == Set(font.postScriptNames) else {
                throw OptionalFontInstallerError.unexpectedPostScriptNames(font.fileName)
            }
        }
        try archive.extract(
            entry: validated.licenseEntry,
            from: archiveURL,
            to: stage.appendingPathComponent("LICENSE.txt")
        )

        let versionRoot = storageRoot.appendingPathComponent(OptionalFontCatalog.releaseTag, isDirectory: true)
        let finalRoot = finalRoot(for: pack)
        guard !fileManager.fileExists(atPath: finalRoot.path) else {
            throw OptionalFontInstallerError.alreadyInstalled
        }
        try fileManager.createDirectory(at: versionRoot, withIntermediateDirectories: true)
        try fileManager.moveItem(at: stage, to: finalRoot)

        let installedURLs = pack.files.map { finalRoot.appendingPathComponent($0.fileName) }
        var registeredURLs: [URL] = []
        do {
            for url in installedURLs {
                try registrar.registerFont(at: url)
                registeredURLs.append(url)
            }
        } catch {
            for url in registeredURLs.reversed() {
                try? registrar.unregisterFont(at: url)
            }
            try? fileManager.removeItem(at: finalRoot)
            throw OptionalFontInstallerError.registrationFailed
        }
        return installedURLs
    }

    func status(for pack: OptionalFontPack) -> OptionalFontPackStatus {
        if let reason = pack.unavailableReason {
            return .unavailable(reason)
        }

        let fontURLs = pack.files.map { finalRoot(for: pack).appendingPathComponent($0.fileName) }
        let existingURLs = fontURLs.filter { fileManager.fileExists(atPath: $0.path) }
        let registeredURLs = existingURLs.filter { registrar.isRegisteredByMarkLeaf(at: $0) }
        if existingURLs.count == fontURLs.count && registeredURLs.count == fontURLs.count {
            return .installedByMarkLeaf
        }

        let expectedPostScriptNames = pack.files.flatMap(\.postScriptNames)
        if existingURLs.isEmpty,
           !expectedPostScriptNames.isEmpty,
           expectedPostScriptNames.allSatisfy({ registrar.isFontAvailable(postScriptName: $0) }) {
            return .installedExternally
        }
        if !existingURLs.isEmpty || !registeredURLs.isEmpty
            || expectedPostScriptNames.contains(where: { registrar.isFontAvailable(postScriptName: $0) }) {
            return .partial
        }
        return .missing
    }

    func uninstall(pack: OptionalFontPack) throws {
        let packRoot = finalRoot(for: pack)
        guard fileManager.fileExists(atPath: packRoot.path) else { return }
        let fontURLs = pack.files.map { packRoot.appendingPathComponent($0.fileName) }
        do {
            for url in fontURLs where registrar.isRegisteredByMarkLeaf(at: url) {
                try registrar.unregisterFont(at: url)
            }
        } catch {
            throw OptionalFontInstallerError.registrationFailed
        }
        try fileManager.removeItem(at: packRoot)
    }

    static func sha256(of url: URL) throws -> String {
        let handle = try FileHandle(forReadingFrom: url)
        defer { try? handle.close() }
        var hasher = SHA256()
        while let data = try handle.read(upToCount: 1024 * 1024), !data.isEmpty {
            hasher.update(data: data)
        }
        return hasher.finalize().map { String(format: "%02x", $0) }.joined()
    }

    private func finalRoot(for pack: OptionalFontPack) -> URL {
        storageRoot
            .appendingPathComponent(OptionalFontCatalog.releaseTag, isDirectory: true)
            .appendingPathComponent(pack.id, isDirectory: true)
    }
}

struct CoreTextFontMetadata: OptionalFontMetadataProviding {
    func postScriptNames(at fontURL: URL) throws -> Set<String> {
        let descriptors = CTFontManagerCreateFontDescriptorsFromURL(fontURL as CFURL) as? [CTFontDescriptor] ?? []
        let names = descriptors.compactMap {
            CTFontDescriptorCopyAttribute($0, kCTFontNameAttribute) as? String
        }
        guard !names.isEmpty else {
            throw OptionalFontInstallerError.invalidFontData(fontURL.lastPathComponent)
        }
        return Set(names)
    }
}

struct CoreTextFontRegistrar: OptionalFontRegistering {
    func registerFont(at url: URL) throws {
        var error: Unmanaged<CFError>?
        guard CTFontManagerRegisterFontsForURL(url as CFURL, .user, &error) else {
            if let error { throw error.takeRetainedValue() }
            throw OptionalFontInstallerError.registrationFailed
        }
    }

    func unregisterFont(at url: URL) throws {
        var error: Unmanaged<CFError>?
        guard CTFontManagerUnregisterFontsForURL(url as CFURL, .user, &error) else {
            if let error { throw error.takeRetainedValue() }
            throw OptionalFontInstallerError.registrationFailed
        }
    }

    func isRegisteredByMarkLeaf(at url: URL) -> Bool {
        CTFontManagerGetScopeForURL(url as CFURL) == .user
    }

    func isFontAvailable(postScriptName: String) -> Bool {
        NSFont(name: postScriptName, size: 12) != nil
    }
}

enum OptionalFontInstallProgress {
    case downloading(OptionalFontPack, Int, Int)
    case validating(OptionalFontPack, Int, Int)
    case installed(OptionalFontPack, Int, Int)
}

final class OptionalFontInstaller {
    private let core: OptionalFontInstallerCore
    private let session: URLSession
    private let workQueue = DispatchQueue(label: "com.markleaf.optional-font-installer", qos: .userInitiated)
    private var activeTask: URLSessionDownloadTask?

    init(storageRoot: URL? = nil, session: URLSession? = nil) {
        let resolvedRoot = storageRoot ?? Self.defaultStorageRoot()
        self.core = OptionalFontInstallerCore(
            storageRoot: resolvedRoot,
            archive: SystemZipArchive(),
            metadata: CoreTextFontMetadata(),
            registrar: CoreTextFontRegistrar()
        )
        self.session = session ?? URLSession(configuration: Self.sessionConfiguration())
    }

    static func request(for pack: OptionalFontPack) -> URLRequest {
        var request = URLRequest(url: OptionalFontCatalog.assetURL(for: pack))
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.timeoutInterval = 60
        request.setValue("MarkLeaf/1.7.3 macOS", forHTTPHeaderField: "User-Agent")
        request.setValue("application/zip", forHTTPHeaderField: "Accept")
        return request
    }

    func status(for pack: OptionalFontPack) -> OptionalFontPackStatus {
        core.status(for: pack)
    }

    func install(
        packs: [OptionalFontPack],
        progress: @escaping (OptionalFontInstallProgress) -> Void,
        completion: @escaping (Result<Void, Error>) -> Void
    ) {
        installNext(packs: packs, index: 0, progress: progress, completion: completion)
    }

    func uninstall(pack: OptionalFontPack, completion: @escaping (Result<Void, Error>) -> Void) {
        workQueue.async { [core] in
            let result = Result { try core.uninstall(pack: pack) }
            DispatchQueue.main.async { completion(result) }
        }
    }

    func cancel() {
        activeTask?.cancel()
        activeTask = nil
    }

    private func installNext(
        packs: [OptionalFontPack],
        index: Int,
        progress: @escaping (OptionalFontInstallProgress) -> Void,
        completion: @escaping (Result<Void, Error>) -> Void
    ) {
        guard index < packs.count else {
            completion(.success(()))
            return
        }
        let pack = packs[index]
        switch core.status(for: pack) {
        case .installedByMarkLeaf, .installedExternally:
            installNext(packs: packs, index: index + 1, progress: progress, completion: completion)
            return
        case .unavailable:
            completion(.failure(OptionalFontCatalogError.unavailablePack(pack.id)))
            return
        case .partial, .missing:
            break
        }

        progress(.downloading(pack, index + 1, packs.count))
        activeTask = session.downloadTask(with: Self.request(for: pack)) { [weak self] location, response, error in
            guard let self else { return }
            if let error {
                DispatchQueue.main.async { completion(.failure(error)) }
                return
            }
            guard let http = response as? HTTPURLResponse else {
                DispatchQueue.main.async { completion(.failure(OptionalFontInstallerError.invalidHTTPResponse)) }
                return
            }
            guard (200..<300).contains(http.statusCode) else {
                DispatchQueue.main.async { completion(.failure(OptionalFontInstallerError.httpStatus(http.statusCode))) }
                return
            }
            guard let location else {
                DispatchQueue.main.async { completion(.failure(OptionalFontInstallerError.downloadFailed)) }
                return
            }

            let retainedDownload = FileManager.default.temporaryDirectory
                .appendingPathComponent("markleaf-font-\(UUID().uuidString).zip")
            do {
                try FileManager.default.moveItem(at: location, to: retainedDownload)
            } catch {
                DispatchQueue.main.async { completion(.failure(error)) }
                return
            }

            DispatchQueue.main.async { progress(.validating(pack, index + 1, packs.count)) }
            self.workQueue.async { [core] in
                defer { try? FileManager.default.removeItem(at: retainedDownload) }
                do {
                    if case .partial = core.status(for: pack) {
                        try core.uninstall(pack: pack)
                    }
                    _ = try core.installDownloadedArchive(retainedDownload, pack: pack)
                    DispatchQueue.main.async {
                        progress(.installed(pack, index + 1, packs.count))
                        self.installNext(
                            packs: packs,
                            index: index + 1,
                            progress: progress,
                            completion: completion
                        )
                    }
                } catch {
                    DispatchQueue.main.async { completion(.failure(error)) }
                }
            }
        }
        activeTask?.resume()
    }

    private static func defaultStorageRoot() -> URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser
        return base.appendingPathComponent("MarkLeaf/Fonts", isDirectory: true)
    }

    private static func sessionConfiguration() -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.timeoutIntervalForRequest = 60
        configuration.timeoutIntervalForResource = 15 * 60
        return configuration
    }
}
