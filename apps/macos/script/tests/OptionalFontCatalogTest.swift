import Foundation

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    if !condition() {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

private let fixturePack = OptionalFontPack(
    id: "fixture",
    styleID: "fixture-style",
    displayName: "Fixture",
    assetName: "fixture.zip",
    sha256: String(repeating: "a", count: 64),
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

expect(OptionalFontCatalog.packs.count == 3, "应展示三个 Windows 1.7.3 对应字体包")
expect(OptionalFontCatalog.pack(id: "computer-modern")?.isInstallable == true,
       "包含 OFL 的 Computer Modern 字体包应可安装")
expect(OptionalFontCatalog.pack(id: "lxgw-wenkai")?.isInstallable == true,
       "包含 OFL 的霞鹜文楷字体包应可安装")
expect(OptionalFontCatalog.pack(id: "old-typeface")?.isInstallable == false,
       "缺少随包许可证的旧字形包不得静默安装")

let computerModern = OptionalFontCatalog.pack(id: "computer-modern")!
expect(OptionalFontCatalog.assetURL(for: computerModern).absoluteString ==
       "https://github.com/zhuanshunjishi2017/markleaf/releases/download/1.7.2/MarkLeaf-fontpack-computer-modern.zip",
       "字体资产必须固定到已审计的 Release，而不是 latest")
expect(OptionalFontCatalog.validatesSHA256(
    "0D98D62459A131271D3BCF8B0A3E7831B49F4487FDD1EEDDFEF471B97408DC0A",
    for: computerModern
), "摘要比较应忽略大小写")
expect(!OptionalFontCatalog.validatesSHA256(String(repeating: "0", count: 64), for: computerModern),
       "错误摘要必须被拒绝")

do {
    let entries = try OptionalFontCatalog.validatedArchiveEntries(
        ["fonts/Alpha-Regular.ttf", "fonts/Alpha-Bold.otf", "fonts/OFL.txt"],
        for: fixturePack
    )
    expect(entries.fontEntries == [
        "Alpha-Bold.otf": "fonts/Alpha-Bold.otf",
        "Alpha-Regular.ttf": "fonts/Alpha-Regular.ttf",
    ], "应只返回经过白名单匹配的字体文件")
    expect(entries.licenseEntry == "fonts/OFL.txt", "应保留随字体安装的许可证文件")
} catch {
    fputs("FAIL: 合法归档不应被拒绝：\(error)\n", stderr)
    exit(1)
}

do {
    _ = try OptionalFontCatalog.validatedArchiveEntries(
        ["../Alpha-Regular.ttf", "Alpha-Bold.otf"],
        for: fixturePack
    )
    fputs("FAIL: 路径穿越条目必须被拒绝\n", stderr)
    exit(1)
} catch OptionalFontCatalogError.unsafeArchiveEntry {
    // expected
} catch {
    fputs("FAIL: 路径穿越应返回 unsafeArchiveEntry，实际为 \(error)\n", stderr)
    exit(1)
}

do {
    _ = try OptionalFontCatalog.validatedArchiveEntries(
        ["Alpha-Regular.ttf", "Alpha-Bold.otf", "Unexpected.ttf"],
        for: fixturePack
    )
    fputs("FAIL: 未声明的额外字体必须被拒绝\n", stderr)
    exit(1)
} catch OptionalFontCatalogError.unexpectedFontFile("Unexpected.ttf") {
    // expected
} catch {
    fputs("FAIL: 额外字体应返回 unexpectedFontFile，实际为 \(error)\n", stderr)
    exit(1)
}

do {
    _ = try OptionalFontCatalog.validatedArchiveEntries(["Alpha-Regular.ttf", "OFL.txt"], for: fixturePack)
    fputs("FAIL: 缺少声明字体时必须拒绝整个包\n", stderr)
    exit(1)
} catch OptionalFontCatalogError.missingFontFiles(let files) {
    expect(files == ["Alpha-Bold.otf"], "应准确报告缺少的字体文件")
} catch {
    fputs("FAIL: 缺少字体应返回 missingFontFiles，实际为 \(error)\n", stderr)
    exit(1)
}

do {
    _ = try OptionalFontCatalog.validatedArchiveEntries(
        ["Alpha-Regular.ttf", "Alpha-Bold.otf"],
        for: fixturePack
    )
    fputs("FAIL: 缺少许可证文件时必须拒绝整个包\n", stderr)
    exit(1)
} catch OptionalFontCatalogError.missingLicenseFile("OFL.txt") {
    // expected
} catch {
    fputs("FAIL: 缺少许可证应返回 missingLicenseFile，实际为 \(error)\n", stderr)
    exit(1)
}

do {
    let disabled = OptionalFontCatalog.pack(id: "old-typeface")!
    _ = try OptionalFontCatalog.validatedArchiveEntries([], for: disabled)
    fputs("FAIL: 不可安装字体包必须在归档处理前被拒绝\n", stderr)
    exit(1)
} catch OptionalFontCatalogError.unavailablePack {
    // expected
} catch {
    fputs("FAIL: 不可安装字体包应返回 unavailablePack，实际为 \(error)\n", stderr)
    exit(1)
}

print("OptionalFontCatalog tests passed")
