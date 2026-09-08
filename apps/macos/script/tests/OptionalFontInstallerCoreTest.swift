import Foundation

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

private final class FixtureArchive: OptionalFontArchiveProviding {
    let entries: [String]
    let contents: [String: Data]
    private(set) var extractedEntries: [String] = []

    init(entries: [String], contents: [String: Data]) {
        self.entries = entries
        self.contents = contents
    }

    func listEntries(in archiveURL: URL) throws -> [String] { entries }

    func extract(entry: String, from archiveURL: URL, to destinationURL: URL) throws {
        extractedEntries.append(entry)
        guard let data = contents[entry] else {
            throw NSError(domain: "FixtureArchive", code: 1)
        }
        try data.write(to: destinationURL, options: .atomic)
    }
}

private struct FixtureMetadata: OptionalFontMetadataProviding {
    let postScriptNames: [String: Set<String>]

    func postScriptNames(at fontURL: URL) throws -> Set<String> {
        postScriptNames[fontURL.lastPathComponent] ?? []
    }
}

private final class FixtureRegistrar: OptionalFontRegistering {
    var failOnFileName: String?
    var availableNames: Set<String> = []
    private(set) var registered: [URL] = []
    private(set) var unregistered: [URL] = []

    func registerFont(at url: URL) throws {
        if url.lastPathComponent == failOnFileName {
            throw NSError(domain: "FixtureRegistrar", code: 2)
        }
        registered.append(url)
    }

    func unregisterFont(at url: URL) throws {
        unregistered.append(url)
    }

    func isRegisteredByMarkLeaf(at url: URL) -> Bool { registered.contains(url) }

    func isFontAvailable(postScriptName: String) -> Bool { availableNames.contains(postScriptName) }
}

private func makePack(sha256: String = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") -> OptionalFontPack {
    OptionalFontPack(
        id: "fixture",
        styleID: "fixture-style",
        displayName: "Fixture",
        assetName: "fixture.zip",
        sha256: sha256,
        licenseName: "Fixture License",
        licenseURL: URL(string: "https://example.com/license")!,
        licenseFileName: "OFL.txt",
        files: [
            OptionalFontFile(
                displayName: "Alpha Regular",
                fileName: "Alpha-Regular.ttf",
                postScriptNames: ["Alpha-Regular"]
            ),
            OptionalFontFile(
                displayName: "Alpha Bold",
                fileName: "Alpha-Bold.otf",
                postScriptNames: ["Alpha-Bold"]
            ),
        ]
    )
}

private func makeRoot(_ name: String) -> URL {
    let root = FileManager.default.temporaryDirectory
        .appendingPathComponent("markleaf-optional-font-installer-\(name)-\(UUID().uuidString)", isDirectory: true)
    try! FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    return root
}

private func run(_ executable: String, _ arguments: [String], cwd: URL? = nil) throws {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: executable)
    process.arguments = arguments
    process.currentDirectoryURL = cwd
    try process.run()
    process.waitUntilExit()
    if process.terminationStatus != 0 {
        throw NSError(domain: "Process", code: Int(process.terminationStatus))
    }
}

private let entries = ["fonts/Alpha-Regular.ttf", "fonts/Alpha-Bold.otf", "fonts/OFL.txt"]
private let contents: [String: Data] = [
    "fonts/Alpha-Regular.ttf": Data("regular".utf8),
    "fonts/Alpha-Bold.otf": Data("bold".utf8),
    "fonts/OFL.txt": Data("license".utf8),
]
private let metadata = FixtureMetadata(postScriptNames: [
    "Alpha-Regular.ttf": ["Alpha-Regular"],
    "Alpha-Bold.otf": ["Alpha-Bold"],
])

let request = OptionalFontInstaller.request(for: OptionalFontCatalog.pack(id: "computer-modern")!)
expect(request.url?.absoluteString ==
       "https://github.com/zhuanshunjishi2017/markleaf/releases/download/1.7.2/MarkLeaf-fontpack-computer-modern.zip",
       "下载请求必须使用固定到已审计版本的资产 URL")
expect(request.cachePolicy == .reloadIgnoringLocalCacheData, "字体下载不得复用过期缓存")
expect(request.timeoutInterval == 60, "字体包下载请求应允许大文件传输但必须有超时")
expect(request.value(forHTTPHeaderField: "User-Agent") == "MarkLeaf/1.7.3 macOS",
       "字体下载请求应包含可识别的 User-Agent")

let fixturePack = makePack()
expect(OptionalFontPresentation.actions(for: fixturePack, status: .missing) ==
       OptionalFontActionState(canInstall: true, canUninstall: false, canOpenLicense: true),
       "缺失且许可完整的字体包应允许安装和查看许可")
expect(OptionalFontPresentation.actions(for: fixturePack, status: .installedByMarkLeaf) ==
       OptionalFontActionState(canInstall: false, canUninstall: true, canOpenLicense: true),
       "由 MarkLeaf 安装的字体包应允许卸载")
expect(OptionalFontPresentation.actions(for: fixturePack, status: .partial) ==
       OptionalFontActionState(canInstall: true, canUninstall: true, canOpenLicense: true),
       "不完整安装应允许清理并重新安装")
expect(OptionalFontPresentation.actions(for: fixturePack, status: .installedExternally) ==
       OptionalFontActionState(canInstall: false, canUninstall: false, canOpenLicense: true),
       "外部已安装字体不得由 MarkLeaf 覆盖或删除")
let unavailablePack = OptionalFontCatalog.pack(id: "old-typeface")!
expect(OptionalFontPresentation.actions(for: unavailablePack, status: .unavailable("missing license")) ==
       OptionalFontActionState(canInstall: false, canUninstall: false, canOpenLicense: false),
       "缺少许可信息的字体包只能展示原因，不能提供安装动作")

do {
    let root = makeRoot("external-status")
    defer { try? FileManager.default.removeItem(at: root) }
    let registrar = FixtureRegistrar()
    registrar.availableNames = ["Alpha-Regular", "Alpha-Bold"]
    let core = OptionalFontInstallerCore(
        storageRoot: root.appendingPathComponent("Fonts"),
        archive: FixtureArchive(entries: entries, contents: contents),
        metadata: metadata,
        registrar: registrar
    )
    expect(core.status(for: fixturePack) == .installedExternally,
           "全部内部名称已由其他来源提供时应识别为外部安装")
    registrar.availableNames = ["Alpha-Regular"]
    expect(core.status(for: fixturePack) == .partial,
           "仅部分内部名称可用时应提示不完整状态")
}

do {
    let root = makeRoot("success")
    defer { try? FileManager.default.removeItem(at: root) }
    let archiveURL = root.appendingPathComponent("download.zip")
    try Data("abc".utf8).write(to: archiveURL)
    let archive = FixtureArchive(entries: entries, contents: contents)
    let registrar = FixtureRegistrar()
    let core = OptionalFontInstallerCore(
        storageRoot: root.appendingPathComponent("Fonts"),
        archive: archive,
        metadata: metadata,
        registrar: registrar
    )

    let installed = try core.installDownloadedArchive(archiveURL, pack: makePack())
    expect(installed.map(\.lastPathComponent) == ["Alpha-Regular.ttf", "Alpha-Bold.otf"],
           "安装结果应保持目录声明顺序")
    expect(installed.allSatisfy { FileManager.default.fileExists(atPath: $0.path) },
           "成功安装后字体文件应位于持久目录")
    let license = root.appendingPathComponent("Fonts/1.7.2/fixture/LICENSE.txt")
    expect(FileManager.default.fileExists(atPath: license.path), "许可证必须随字体保存在安装目录")
    expect(registrar.registered == installed, "仅应注册最终持久路径中的字体")
    expect(archive.extractedEntries == entries, "只应提取白名单字体与许可证")
    expect(core.status(for: makePack()) == .installedByMarkLeaf,
           "持久文件和用户级注册均存在时应识别为 MarkLeaf 已安装")

    try core.uninstall(pack: makePack())
    expect(!FileManager.default.fileExists(atPath: license.deletingLastPathComponent().path),
           "卸载后应删除 MarkLeaf 管理的整包目录")
    expect(Set(registrar.unregistered) == Set(installed), "卸载前应注销该包的全部字体")
    expect(core.status(for: makePack()) == .missing, "卸载完成后状态应恢复为未安装")
} catch {
    fputs("FAIL: 合法字体包安装不应失败：\(error)\n", stderr)
    exit(1)
}

do {
    let root = makeRoot("digest")
    defer { try? FileManager.default.removeItem(at: root) }
    let archiveURL = root.appendingPathComponent("download.zip")
    try Data("tampered".utf8).write(to: archiveURL)
    let archive = FixtureArchive(entries: entries, contents: contents)
    let registrar = FixtureRegistrar()
    let core = OptionalFontInstallerCore(
        storageRoot: root.appendingPathComponent("Fonts"),
        archive: archive,
        metadata: metadata,
        registrar: registrar
    )
    _ = try core.installDownloadedArchive(archiveURL, pack: makePack())
    fputs("FAIL: 摘要错误的字体包必须被拒绝\n", stderr)
    exit(1)
} catch OptionalFontInstallerError.digestMismatch {
    // expected
} catch {
    fputs("FAIL: 摘要错误应返回 digestMismatch，实际为 \(error)\n", stderr)
    exit(1)
}

do {
    let root = makeRoot("metadata")
    defer { try? FileManager.default.removeItem(at: root) }
    let archiveURL = root.appendingPathComponent("download.zip")
    try Data("abc".utf8).write(to: archiveURL)
    let archive = FixtureArchive(entries: entries, contents: contents)
    let registrar = FixtureRegistrar()
    let wrongMetadata = FixtureMetadata(postScriptNames: [
        "Alpha-Regular.ttf": ["Unexpected-Regular"],
        "Alpha-Bold.otf": ["Alpha-Bold"],
    ])
    let core = OptionalFontInstallerCore(
        storageRoot: root.appendingPathComponent("Fonts"),
        archive: archive,
        metadata: wrongMetadata,
        registrar: registrar
    )
    _ = try core.installDownloadedArchive(archiveURL, pack: makePack())
    fputs("FAIL: 字体内部名称不匹配时必须拒绝整个包\n", stderr)
    exit(1)
} catch OptionalFontInstallerError.unexpectedPostScriptNames(let fileName) {
    expect(fileName == "Alpha-Regular.ttf", "应准确报告元数据不匹配的字体")
} catch {
    fputs("FAIL: 元数据错误应返回 unexpectedPostScriptNames，实际为 \(error)\n", stderr)
    exit(1)
}

let rollbackRoot = makeRoot("rollback")
do {
    let archiveURL = rollbackRoot.appendingPathComponent("download.zip")
    try Data("abc".utf8).write(to: archiveURL)
    let archive = FixtureArchive(entries: entries, contents: contents)
    let registrar = FixtureRegistrar()
    registrar.failOnFileName = "Alpha-Bold.otf"
    let core = OptionalFontInstallerCore(
        storageRoot: rollbackRoot.appendingPathComponent("Fonts"),
        archive: archive,
        metadata: metadata,
        registrar: registrar
    )
    _ = try core.installDownloadedArchive(archiveURL, pack: makePack())
    fputs("FAIL: 注册部分失败时整个事务必须失败\n", stderr)
    exit(1)
} catch OptionalFontInstallerError.registrationFailed {
    let finalRoot = rollbackRoot.appendingPathComponent("Fonts/1.7.2/fixture")
    expect(!FileManager.default.fileExists(atPath: finalRoot.path), "回滚后不得保留半安装目录")
} catch {
    fputs("FAIL: 注册失败应返回 registrationFailed，实际为 \(error)\n", stderr)
    exit(1)
}
try? FileManager.default.removeItem(at: rollbackRoot)

do {
    let root = makeRoot("system-zip")
    defer { try? FileManager.default.removeItem(at: root) }
    let source = root.appendingPathComponent("source", isDirectory: true)
    let nested = source.appendingPathComponent("nested", isDirectory: true)
    try FileManager.default.createDirectory(at: nested, withIntermediateDirectories: true)
    try Data("font-bytes".utf8).write(to: nested.appendingPathComponent("Fixture.ttf"))
    try Data("license".utf8).write(to: source.appendingPathComponent("OFL.txt"))
    let archiveURL = root.appendingPathComponent("fixture.zip")
    try run("/usr/bin/zip", ["-q", "-r", archiveURL.path, "."], cwd: source)

    let archive = SystemZipArchive()
    let listed = try archive.listEntries(in: archiveURL)
    expect(listed.contains("nested/Fixture.ttf"), "系统 ZIP 适配器应列出嵌套字体条目")
    expect(listed.contains("OFL.txt"), "系统 ZIP 适配器应列出许可证条目")
    let extracted = root.appendingPathComponent("Fixture.ttf")
    try archive.extract(entry: "nested/Fixture.ttf", from: archiveURL, to: extracted)
    let extractedData = try Data(contentsOf: extracted)
    expect(extractedData == Data("font-bytes".utf8),
           "系统 ZIP 适配器应只提取指定条目")
} catch {
    fputs("FAIL: 系统 ZIP 适配器测试失败：\(error)\n", stderr)
    exit(1)
}

print("OptionalFontInstallerCore tests passed")
