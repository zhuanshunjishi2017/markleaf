import Foundation

struct OptionalFontFile: Equatable {
    let displayName: String
    let fileName: String
    let postScriptNames: [String]
}

struct OptionalFontPack: Equatable, Identifiable {
    let id: String
    let styleID: String
    let displayName: String
    let assetName: String
    let sha256: String
    let licenseName: String?
    let licenseURL: URL?
    let licenseFileName: String?
    let files: [OptionalFontFile]
    let unavailableReason: String?

    init(
        id: String,
        styleID: String,
        displayName: String,
        assetName: String,
        sha256: String,
        licenseName: String?,
        licenseURL: URL?,
        licenseFileName: String?,
        files: [OptionalFontFile],
        unavailableReason: String? = nil
    ) {
        self.id = id
        self.styleID = styleID
        self.displayName = displayName
        self.assetName = assetName
        self.sha256 = sha256
        self.licenseName = licenseName
        self.licenseURL = licenseURL
        self.licenseFileName = licenseFileName
        self.files = files
        self.unavailableReason = unavailableReason
    }

    var isInstallable: Bool {
        unavailableReason == nil && licenseName != nil && licenseURL != nil
            && licenseFileName != nil && !files.isEmpty
    }
}

struct ValidatedOptionalFontArchive: Equatable {
    let fontEntries: [String: String]
    let licenseEntry: String
}

enum OptionalFontCatalogError: Error, Equatable {
    case unavailablePack(String)
    case unsafeArchiveEntry(String)
    case unexpectedFontFile(String)
    case duplicateFontFile(String)
    case missingFontFiles([String])
    case missingLicenseFile(String)
    case duplicateLicenseFile(String)
}

enum OptionalFontCatalog {
    static let releaseTag = "1.7.2"
    static let releaseURL = URL(
        string: "https://github.com/zhuanshunjishi2017/markleaf/releases/tag/\(releaseTag)"
    )!
    private static let oflURL = URL(
        string: "https://openfontlicense.org/open-font-license-official-text/"
    )!

    static let packs: [OptionalFontPack] = [
        OptionalFontPack(
            id: "computer-modern",
            styleID: "latex",
            displayName: "Computer Modern",
            assetName: "MarkLeaf-fontpack-computer-modern.zip",
            sha256: "0d98d62459a131271d3bcf8b0a3e7831b49f4487fdd1eeddfef471b97408dc0a",
            licenseName: "SIL Open Font License 1.1",
            licenseURL: oflURL,
            licenseFileName: "SIL Open Font License.txt",
            files: [
                font("CMU Sans Serif", "cmunss.ttf", "CMUSansSerif"),
                font("CMU Sans Serif Oblique", "cmunsi.ttf", "CMUSansSerif-Oblique"),
                font("CMU Sans Serif Bold", "cmunsx.ttf", "CMUSansSerif-Bold"),
                font("CMU Sans Serif Bold Oblique", "cmunso.ttf", "CMUSansSerif-BoldOblique"),
                font("CMU Sans Serif Demi Condensed", "cmunssdc.ttf", "CMUSansSerif-DemiCondensed"),
                font("CMU Serif Roman", "cmunrm.ttf", "CMUSerif-Roman"),
                font("CMU Serif Italic", "cmunti.ttf", "CMUSerif-Italic"),
                font("CMU Serif Bold", "cmunbx.ttf", "CMUSerif-Bold"),
                font("CMU Serif Bold Italic", "cmunbi.ttf", "CMUSerif-BoldItalic"),
                font("CMU Serif Upright Italic", "cmunui.ttf", "CMUSerif-UprightItalic"),
                font("CMU Serif Roman Slanted", "cmunsl.ttf", "CMUSerif-RomanSlanted"),
                font("CMU Serif Bold Slanted", "cmunbl.ttf", "CMUSerif-BoldSlanted"),
                font("CMU Classical Serif Italic", "cmunci.ttf", "CMUClassicalSerif-Italic"),
                font("CMU Typewriter Light", "cmunbtl.ttf", "CMUTypewriter-Light"),
                font("CMU Typewriter Light Oblique", "cmunbto.ttf", "CMUTypewriter-LightOblique"),
                font("CMU Typewriter Regular", "cmuntt.ttf", "CMUTypewriter-Regular"),
                font("CMU Typewriter Italic", "cmunit.ttf", "CMUTypewriter-Italic"),
                font("CMU Typewriter Bold", "cmuntb.ttf", "CMUTypewriter-Bold"),
                font("CMU Typewriter Bold Italic", "cmuntx.ttf", "CMUTypewriter-BoldItalic"),
                font("CMU Typewriter Variable", "cmunvt.ttf", "CMUTypewriterVariable"),
                font("CMU Typewriter Variable Italic", "cmunvi.ttf", "CMUTypewriterVariable-Italic"),
                font("CMU Bright Roman", "cmunbmr.ttf", "CMUBright-Roman"),
                font("CMU Bright Oblique", "cmunbmo.ttf", "CMUBright-Oblique"),
                font("CMU Bright SemiBold", "cmunbsr.ttf", "CMUBright-SemiBold"),
                font("CMU Bright SemiBold Oblique", "cmunbso.ttf", "CMUBright-SemiBoldOblique"),
                font("CMU Concrete Roman", "cmunorm.ttf", "CMUConcrete-Roman"),
                font("CMU Concrete Italic", "cmunoti.ttf", "CMUConcrete-Italic"),
                font("CMU Concrete Bold", "cmunobx.ttf", "CMUConcrete-Bold"),
                font("CMU Concrete Bold Italic", "cmunobi.ttf", "CMUConcrete-BoldItalic"),
            ]
        ),
        OptionalFontPack(
            id: "lxgw-wenkai",
            styleID: "notebook",
            displayName: "霞鹜文楷",
            assetName: "MarkLeaf-fontpack-lxgw-wenkai.zip",
            sha256: "3b596205423d838ccd0965bfa2c7e8b6ebcb9d9284e1809e234ad1d5f441adef",
            licenseName: "SIL Open Font License 1.1",
            licenseURL: oflURL,
            licenseFileName: "OFL.txt",
            files: [
                font("LXGW WenKai Light", "LXGWWenKai-Light.ttf", "LXGWWenKai-Light"),
                font("LXGW WenKai Medium", "LXGWWenKai-Medium.ttf", "LXGWWenKai-Medium"),
                font("LXGW WenKai Regular", "LXGWWenKai-Regular.ttf", "LXGWWenKai-Regular"),
                font("LXGW WenKai Mono Light", "LXGWWenKaiMono-Light.ttf", "LXGWWenKaiMono-Light"),
                font("LXGW WenKai Mono Medium", "LXGWWenKaiMono-Medium.ttf", "LXGWWenKaiMono-Medium"),
                font("LXGW WenKai Mono Regular", "LXGWWenKaiMono-Regular.ttf", "LXGWWenKaiMono-Regular"),
            ]
        ),
        OptionalFontPack(
            id: "old-typeface",
            styleID: "retro-print",
            displayName: "汇文/朝华字体",
            assetName: "MarkLeaf-fontpack-old-typeface.zip",
            sha256: "9ba9685369fd22f199fc45987f30cc5a44da5b41c4facd002e66ef10e9d997ad",
            licenseName: nil,
            licenseURL: nil,
            licenseFileName: nil,
            files: [],
            unavailableReason: "字体包未附带完整许可证，MarkLeaf 暂不提供自动安装。"
        ),
    ]

    static func pack(id: String) -> OptionalFontPack? {
        packs.first(where: { $0.id == id })
    }

    static func assetURL(for pack: OptionalFontPack) -> URL {
        URL(
            string: "https://github.com/zhuanshunjishi2017/markleaf/releases/download/\(releaseTag)/\(pack.assetName)"
        )!
    }

    static func validatesSHA256(_ actual: String, for pack: OptionalFontPack) -> Bool {
        actual.lowercased() == pack.sha256.lowercased()
    }

    static func validatedArchiveEntries(
        _ archiveEntries: [String],
        for pack: OptionalFontPack
    ) throws -> ValidatedOptionalFontArchive {
        guard pack.isInstallable else {
            throw OptionalFontCatalogError.unavailablePack(pack.id)
        }
        guard let expectedLicense = pack.licenseFileName else {
            throw OptionalFontCatalogError.unavailablePack(pack.id)
        }

        let expected = Set(pack.files.map(\.fileName))
        var matched: [String: String] = [:]
        var licenseEntry: String?

        for rawEntry in archiveEntries {
            try validateArchivePath(rawEntry)
            if rawEntry.hasSuffix("/") { continue }
            let fileName = (rawEntry as NSString).lastPathComponent
            if fileName == expectedLicense {
                guard licenseEntry == nil else {
                    throw OptionalFontCatalogError.duplicateLicenseFile(expectedLicense)
                }
                licenseEntry = rawEntry
                continue
            }
            let ext = (fileName as NSString).pathExtension.lowercased()
            guard ["ttf", "otf", "ttc", "otc"].contains(ext) else { continue }
            guard expected.contains(fileName) else {
                throw OptionalFontCatalogError.unexpectedFontFile(fileName)
            }
            guard matched[fileName] == nil else {
                throw OptionalFontCatalogError.duplicateFontFile(fileName)
            }
            matched[fileName] = rawEntry
        }

        let missing = expected.subtracting(matched.keys).sorted()
        guard missing.isEmpty else {
            throw OptionalFontCatalogError.missingFontFiles(missing)
        }
        guard let licenseEntry else {
            throw OptionalFontCatalogError.missingLicenseFile(expectedLicense)
        }
        return ValidatedOptionalFontArchive(fontEntries: matched, licenseEntry: licenseEntry)
    }

    private static func font(
        _ displayName: String,
        _ fileName: String,
        _ postScriptName: String
    ) -> OptionalFontFile {
        OptionalFontFile(
            displayName: displayName,
            fileName: fileName,
            postScriptNames: [postScriptName]
        )
    }

    private static func validateArchivePath(_ entry: String) throws {
        let normalized = entry.replacingOccurrences(of: "\\", with: "/")
        let components = normalized.split(separator: "/", omittingEmptySubsequences: false)
        let hasUnsafeComponent = components.contains(where: { $0 == ".." || $0 == "." })
        guard !entry.isEmpty,
              !entry.contains("\\"),
              !entry.contains(":"),
              !entry.hasPrefix("/"),
              !entry.contains("\0"),
              !hasUnsafeComponent else {
            throw OptionalFontCatalogError.unsafeArchiveEntry(entry)
        }
    }
}
