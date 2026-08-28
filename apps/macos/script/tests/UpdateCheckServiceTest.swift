import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

// —— 版本比较 ——
expect(UpdateCheckService.compareVersions("1.4.0", "1.3.2") == .orderedDescending,
       "1.4.0 应大于 1.3.2")
expect(UpdateCheckService.compareVersions("1.4.0", "1.4.0") == .orderedSame,
       "相同版本应相等")
expect(UpdateCheckService.compareVersions("1.4.0", "1.5.0") == .orderedAscending,
       "1.4.0 应小于 1.5.0")
expect(UpdateCheckService.compareVersions("1.4.0", "1.4.1") == .orderedAscending,
       "补丁号应参与比较")
expect(UpdateCheckService.compareVersions("1.10.0", "1.9.0") == .orderedDescending,
       "应按数值而非字典序比较")
expect(UpdateCheckService.compareVersions("1.4", "1.4.0") == .orderedSame,
       "缺段视为 0")
expect(UpdateCheckService.isNewerVersion("1.4.0", than: "1.3.2"),
       "1.4.0 更新于 1.3.2")
expect(!UpdateCheckService.isNewerVersion("1.3.2", than: "1.4.0"),
       "1.3.2 不应更新于 1.4.0")

// —— macOS 资产选择 ——
let dmgAsset = UpdateCheckService.Asset(name: "MarkLeaf-1.4.0-macos-arm64.dmg", browserDownloadURL: "https://example.com/a.dmg")
let zipAsset = UpdateCheckService.Asset(name: "MarkLeaf-1.4.0-macos-arm64.zip", browserDownloadURL: "https://example.com/a.zip")
let dsym = UpdateCheckService.Asset(name: "MarkLeaf-1.4.0-macos-arm64.dSYM.zip", browserDownloadURL: "https://example.com/a.dsym.zip")
let sha = UpdateCheckService.Asset(name: "SHA256SUMS.txt", browserDownloadURL: "https://example.com/sha")
let release = UpdateCheckService.Release(
    tagName: "1.4.0",
    name: "1.4.0",
    body: "release notes",
    assets: [dsym, sha, zipAsset, dmgAsset]
)
let picked = UpdateCheckService.macOSInstallerURL(from: release)
expect(picked?.absoluteString == "https://example.com/a.dmg",
       "应优先选择 macOS dmg")

let zipOnly = UpdateCheckService.Release(
    tagName: "1.4.0",
    name: nil,
    body: nil,
    assets: [dsym, sha, zipAsset]
)
expect(UpdateCheckService.macOSInstallerURL(from: zipOnly)?.absoluteString == "https://example.com/a.zip",
       "无 dmg 时应选 macOS zip")

// —— 更新判定（版本号 + 可选构建号） ——
let releaseNewer = UpdateCheckService.Release(tagName: "1.4.0", name: nil, body: nil, assets: [dmgAsset])
expect(UpdateCheckService.hasUpdate(release: releaseNewer, currentVersion: "1.3.2", currentBuild: "100"),
       "更高版本号应视为有更新")
let releaseSame = UpdateCheckService.Release(tagName: "1.3.2", name: nil, body: nil, assets: [dmgAsset])
expect(!UpdateCheckService.hasUpdate(release: releaseSame, currentVersion: "1.3.2", currentBuild: "100"),
       "相同版本且未发布构建号时应视为最新")

// —— 检查结束后的状态栏恢复 ——
expect(UpdateCheckService.statusAfterCheck(
    previousStatus: "已保存",
    currentStatus: "正在检查更新…",
    checkingStatus: "正在检查更新…"
) == "已保存", "检查结束后应恢复检查前的状态")
expect(UpdateCheckService.statusAfterCheck(
    previousStatus: "已保存",
    currentStatus: "已修改",
    checkingStatus: "正在检查更新…"
) == "已修改", "检查期间出现的新状态不应被旧状态覆盖")

// —— JSON 解码 ——
let json = """
{
  "tag_name": "1.4.0",
  "name": "1.4.0",
  "body": "release notes",
  "assets": [
    {"name": "MarkLeaf-1.4.0-macos-arm64.dmg", "browser_download_url": "https://example.com/a.dmg"}
  ]
}
""".data(using: .utf8)!
let decoded = try! JSONDecoder().decode(UpdateCheckService.Release.self, from: json)
expect(decoded.tagName == "1.4.0", "应解析 tag_name")
expect(decoded.assets.first?.browserDownloadURL == "https://example.com/a.dmg", "应解析资产下载地址")

print("PASS")
