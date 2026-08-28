import Foundation

/// 检查 GitHub Release 更新的核心逻辑。纯函数部分可在无网络环境下测试，
/// 网络请求由 `fetchLatestRelease` 单独提供。
struct UpdateCheckService {
    static let repositoryOwner = "zhuanshunjishi2017"
    static let repositoryName = "markleaf"
    static let apiLatestURL = URL(
        string: "https://api.github.com/repos/\(repositoryOwner)/\(repositoryName)/releases/latest"
    )!

    static func releasePageURL(tag: String) -> URL {
        URL(string: "https://github.com/\(repositoryOwner)/\(repositoryName)/releases/tag/\(tag)")!
    }

    struct Asset: Decodable, Equatable {
        let name: String
        let browserDownloadURL: String

        enum CodingKeys: String, CodingKey {
            case name
            case browserDownloadURL = "browser_download_url"
        }
    }

    struct Release: Decodable, Equatable {
        let tagName: String
        let name: String?
        let body: String?
        let assets: [Asset]

        enum CodingKeys: String, CodingKey {
            case tagName = "tag_name"
            case name
            case body
            case assets
        }
    }

    enum FetchError: Error, Equatable {
        case offline
        case timedOut
        case connectionFailed
        case httpStatus(Int)
        case invalidResponse
    }

    enum UpdateAvailability: Equatable {
        case updateAvailable
        case upToDate
        case missingBuildMetadata
    }

    struct FailurePresentation: Equatable {
        let title: String
        let message: String
    }

    /// 版本号比较：按 "." 分段数值比较，缺段视为 0，长度不同也能正确比较。
    static func compareVersions(_ lhs: String, _ rhs: String) -> ComparisonResult {
        let a = numericComponents(lhs)
        let b = numericComponents(rhs)
        let count = max(a.count, b.count)
        for i in 0..<count {
            let av = i < a.count ? a[i] : 0
            let bv = i < b.count ? b[i] : 0
            if av < bv { return .orderedAscending }
            if av > bv { return .orderedDescending }
        }
        return .orderedSame
    }

    /// released 是否严格更新于 current（版本号更高）。
    static func isNewerVersion(_ released: String, than current: String) -> Bool {
        compareVersions(released, current) == .orderedDescending
    }

    /// 构建号比较（数字字符串），用于同版本号的次级判断。
    static func isNewerBuild(_ releasedBuild: String, than currentBuild: String) -> Bool {
        (Int(releasedBuild) ?? 0) > (Int(currentBuild) ?? 0)
    }

    /// 从 release 资产里挑选 macOS 安装包：优先 macOS dmg，其次 macOS zip，
    /// 排除 dSYM、校验和、SHA 等辅助资产。
    static func macOSInstallerURL(from release: Release) -> URL? {
        let candidates = release.assets.filter { asset in
            let lower = asset.name.lowercased()
            return lower.contains("macos")
                && (lower.hasSuffix(".dmg") || lower.hasSuffix(".zip"))
                && !lower.contains("dsym")
                && !lower.contains(".sha256")
                && !lower.contains("sha256sums")
        }
        let pick = candidates.first { $0.name.lowercased().hasSuffix(".dmg") }
            ?? candidates.first
        return pick.flatMap { URL(string: $0.browserDownloadURL) }
    }

    /// 当前的“发现新版本”判定：released 版本更高，或版本相同但构建号更高。
    static func hasUpdate(release: Release, currentVersion: String, currentBuild: String) -> Bool {
        updateAvailability(release: release, currentVersion: currentVersion, currentBuild: currentBuild)
            == .updateAvailable
    }

    static func updateAvailability(
        release: Release,
        currentVersion: String,
        currentBuild: String
    ) -> UpdateAvailability {
        if isNewerVersion(release.tagName, than: currentVersion) { return .updateAvailable }
        guard isNewerVersion(currentVersion, than: release.tagName) == false,
              compareVersions(release.tagName, currentVersion) == .orderedSame else {
            return .upToDate
        }
        // 同版本号：仅当远端发布了更高构建号时才视为更新。
        guard let releasedBuild = release.assets.compactMap({ parseBuild(from: $0.name) }).max() else {
            return .missingBuildMetadata
        }
        return releasedBuild > (Int(currentBuild) ?? 0) ? .updateAvailable : .upToDate
    }

    /// 检查完成后只撤销仍然存在的“正在检查”状态，避免覆盖期间产生的新状态。
    static func statusAfterCheck(
        previousStatus: String,
        currentStatus: String,
        checkingStatus: String
    ) -> String {
        currentStatus == checkingStatus ? previousStatus : currentStatus
    }

    private static func parseBuild(from assetName: String) -> Int? {
        let prefix = "markleaf-build-"
        let lower = assetName.lowercased()
        guard lower.hasPrefix(prefix), lower.hasSuffix(".txt") else { return nil }
        let start = lower.index(lower.startIndex, offsetBy: prefix.count)
        let end = lower.index(lower.endIndex, offsetBy: -4)
        let digits = lower[start..<end]
        guard !digits.isEmpty, digits.allSatisfy({ $0 >= "0" && $0 <= "9" }) else { return nil }
        return Int(digits)
    }

    private static func numericComponents(_ version: String) -> [Int] {
        version.split(separator: ".").map { part in
            let digits = part.prefix(while: { $0.isNumber })
            return digits.isEmpty ? 0 : (Int(digits) ?? 0)
        }
    }

    static func makeNetworkConfiguration(protocolClasses: [AnyClass]? = nil) -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.urlCache = nil
        configuration.requestCachePolicy = .reloadIgnoringLocalCacheData
        configuration.timeoutIntervalForRequest = 15
        configuration.timeoutIntervalForResource = 20
        configuration.protocolClasses = protocolClasses
        return configuration
    }

    static func latestReleaseRequest() -> URLRequest {
        var request = URLRequest(url: apiLatestURL)
        request.cachePolicy = .reloadIgnoringLocalCacheData
        request.timeoutInterval = 15
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("no-cache", forHTTPHeaderField: "Cache-Control")
        return request
    }

    static func classifyNetworkError(_ error: URLError) -> FetchError {
        switch error.code {
        case .notConnectedToInternet, .dataNotAllowed:
            return .offline
        case .timedOut:
            return .timedOut
        default:
            return .connectionFailed
        }
    }

    static func failurePresentation(for error: FetchError) -> FailurePresentation {
        switch error {
        case .timedOut:
            return FailurePresentation(
                title: "检查更新超时",
                message: "连接 GitHub 超时，服务器可能繁忙或网络不稳定。"
            )
        case .invalidResponse:
            return FailurePresentation(
                title: "无法读取更新信息",
                message: "GitHub 返回的更新信息格式异常。"
            )
        case .offline, .connectionFailed, .httpStatus:
            return FailurePresentation(
                title: "无法检查更新",
                message: "无法连接 GitHub，请检查网络连接后重试。"
            )
        }
    }

    /// 发起网络请求拉取最新 release。返回经过主线程回调的 `Result<Release, FetchError>`。
    static func fetchLatestRelease(
        session: URLSession? = nil,
        completion: @escaping (Result<Release, FetchError>) -> Void
    ) {
        let request = latestReleaseRequest()
        let requestSession = session ?? URLSession(configuration: makeNetworkConfiguration())
        requestSession.dataTask(with: request) { data, response, error in
            let result: Result<Release, FetchError>
            if let urlError = error as? URLError {
                result = .failure(classifyNetworkError(urlError))
            } else if error != nil {
                result = .failure(.connectionFailed)
            } else if let http = response as? HTTPURLResponse, http.statusCode >= 400 {
                result = .failure(.httpStatus(http.statusCode))
            } else if let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode), let data {
                do {
                    result = .success(try JSONDecoder().decode(Release.self, from: data))
                } catch {
                    result = .failure(.invalidResponse)
                }
            } else {
                result = .failure(.invalidResponse)
            }
            DispatchQueue.main.async { completion(result) }
        }.resume()
    }
}
