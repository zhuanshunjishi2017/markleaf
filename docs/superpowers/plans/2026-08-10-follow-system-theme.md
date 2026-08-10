# 主题「与操作系统同步」实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在偏好设置「外观」新增「与操作系统同步」开关（默认开启）：系统浅色→colors-white-only、深色→colors-dark，开启时颜色主题选择置灰；关闭时恢复手动主题。

**Architecture:** `AppSettings.followSystemTheme` 持久化；`StyleManager.defaultThemeID(forDark:)` 解析默认主题；`EditorSession` 在加载、开关切换与系统外观变化时重新解析并换肤；偏好设置与外观菜单在跟随模式下禁用主题选择。沿用现有 `setTheme`/`applyStyles`/`applySystemAppearance` 换肤链路。

**Tech Stack:** Swift/AppKit、SwiftPM、XCTest。

## Global Constraints

- 仓库根：`/Users/nabian/Documents/Codex/2026-08-07/wo-e/work/markleaf`；分支 `macos-port`，当前分支开发（用户已确认）。
- 基线：HEAD `f69c715`（设计规格提交）。
- `followSystemTheme` 默认 `true`；缺失字段解码为 true。
- 默认主题：浅色 `colors-white-only`，深色 `colors-dark`。
- 跟随模式下自动主题不写入 `settings.colorTheme`；`NSApp.appearance = nil`。
- macOS 三语：zh-Hans 键 / zh-Hant / en。
- 测试因 Documents 文件提供器 FinderInfo 签名问题，统一用 /tmp scratch：
  `DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch`
- 每个任务独立提交；提交信息沿用仓库风格。

---

### Task 1: 设置字段与默认主题解析（TDD）

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/AppSettings.swift`
- Modify: `macos/Sources/MarkLeaf/Services/StyleManager.swift`
- Test: `macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift`（新建）
- Test: `macos/Tests/MarkLeafTests/StyleManagerThemeDefaultsTests.swift`（新建）

**Interfaces:**
- Consumes: 无。
- Produces: `AppSettings.followSystemTheme: Bool`；`StyleManager.defaultLightThemeID` / `defaultDarkThemeID`（static let）；`StyleManager.defaultThemeID(forDark: Bool) -> String?`。

- [ ] **Step 1: 写失败测试（设置字段）**

新建 `macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift`：

```swift
import XCTest
@testable import MarkLeaf

final class AppSettingsFollowSystemTests: XCTestCase {
    func testDefaultsToFollowSystemWhenFieldMissing() throws {
        let data = Data("{\"schemaVersion\":3}".utf8)
        let settings = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(settings.followSystemTheme)
    }

    func testRoundTripFalse() throws {
        var settings = AppSettings()
        settings.followSystemTheme = false
        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertFalse(decoded.followSystemTheme)
    }

    func testRoundTripTrue() throws {
        var settings = AppSettings()
        settings.followSystemTheme = true
        let data = try JSONEncoder().encode(settings)
        let decoded = try JSONDecoder().decode(AppSettings.self, from: data)
        XCTAssertTrue(decoded.followSystemTheme)
    }
}
```

- [ ] **Step 2: 写失败测试（默认主题解析）**

新建 `macos/Tests/MarkLeafTests/StyleManagerThemeDefaultsTests.swift`：

```swift
import XCTest
@testable import MarkLeaf

final class StyleManagerThemeDefaultsTests: XCTestCase {
    private func makeManager(themeIDs: [String]) throws -> StyleManager {
        let dir = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        addTeardownBlock { try? FileManager.default.removeItem(at: dir) }
        for id in themeIDs {
            let css = "/* @name: \(id) */\n:root { --bg-primary: #fff; }\n"
            try Data(css.utf8).write(to: dir.appendingPathComponent("\(id).css"))
        }
        return StyleManager(directories: [dir])
    }

    func testReturnsDefaultLightAndDark() throws {
        let manager = try makeManager(themeIDs: ["colors-white-only", "colors-dark", "colors-rose"])
        XCTAssertEqual(manager.defaultThemeID(forDark: false), "colors-white-only")
        XCTAssertEqual(manager.defaultThemeID(forDark: true), "colors-dark")
    }

    func testFallsBackToDefaultThemeId() throws {
        let manager = try makeManager(themeIDs: ["colors-rose"])
        XCTAssertEqual(manager.defaultThemeID(forDark: false), manager.defaultThemeId)
        XCTAssertEqual(manager.defaultThemeID(forDark: true), manager.defaultThemeId)
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

```bash
cd /Users/nabian/Documents/Codex/2026-08-07/wo-e/work/markleaf
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch --filter "AppSettingsFollowSystemTests|StyleManagerThemeDefaultsTests"
```

预期：编译失败（`followSystemTheme` / `defaultThemeID` 不存在）。

- [ ] **Step 4: 实现设置字段**

`AppSettings.swift`：在 `CodingKeys` 枚举加 `case followSystemTheme`；在「外观」区加属性 `var followSystemTheme = true`；在 `init(from decoder:)` 加：

```swift
followSystemTheme = try container.decodeIfPresent(Bool.self, forKey: .followSystemTheme) ?? true
```

（若 CodingKeys 是合成隐式的，改为显式列出时保持其它键不变。）

- [ ] **Step 5: 实现默认主题解析**

`StyleManager.swift`（类内）：

```swift
    /// 跟随系统外观时的默认浅色/深色主题（对齐 Windows 1.1.2 的 dark/white-only 默认）。
    static let defaultLightThemeID = "colors-white-only"
    static let defaultDarkThemeID = "colors-dark"

    /// 返回指定外观的默认主题 id；内置默认主题缺失时回退到解析出的默认主题。
    func defaultThemeID(forDark dark: Bool) -> String? {
        let preferred = dark ? Self.defaultDarkThemeID : Self.defaultLightThemeID
        if colorThemes.contains(where: { $0.id == preferred }) { return preferred }
        return defaultThemeId
    }
```

- [ ] **Step 6: 运行测试确认通过**

```bash
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch --filter "AppSettingsFollowSystemTests|StyleManagerThemeDefaultsTests"
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch
```

预期：新测试 5/5；全量 24/24。

- [ ] **Step 7: 提交**

```bash
git add macos/Sources/MarkLeaf/Services/AppSettings.swift macos/Sources/MarkLeaf/Services/StyleManager.swift macos/Tests/MarkLeafTests/AppSettingsFollowSystemTests.swift macos/Tests/MarkLeafTests/StyleManagerThemeDefaultsTests.swift
git commit -m "feat(macos): follow-system theme setting and default theme resolution"
```

---

### Task 2: EditorSession 跟随系统集成

**Files:**
- Modify: `macos/Sources/MarkLeaf/Services/EditorSession.swift`

**Interfaces:**
- Consumes: `AppSettings.followSystemTheme`、`StyleManager.defaultThemeID(forDark:)`。
- Produces:
  - `EditorSession.isFollowSystemTheme: Bool`（internal computed）
  - `EditorSession.applyFollowSystemTheme()`（internal）
  - 系统外观变化监听（`AppleInterfaceThemeChangedNotification`）

- [ ] **Step 1: 新增只读属性与系统外观监听**

`EditorSession.swift` 的「初始化」区（`attachStyleManager` 附近）：

```swift
    /// 是否开启「与操作系统同步」（读取当前设置）。
    var isFollowSystemTheme: Bool {
        SettingsService.shared.settings.followSystemTheme
    }

    private var systemThemeObserver: NSObjectProtocol?

    private func startFollowingSystemAppearance() {
        guard systemThemeObserver == nil else { return }
        systemThemeObserver = DistributedNotificationCenter.default().addObserver(
            forName: Notification.Name("AppleInterfaceThemeChangedNotification"),
            object: nil, queue: .main) { [weak self] _ in
            self?.applyFollowSystemTheme()
        }
    }
```

在 `openInitialDocument(path:)` 开头调用 `startFollowingSystemAppearance()`（幂等，每窗口会话注册一次系统外观监听）。

- [ ] **Step 2: applyStyles 支持跟随系统**

`applyStyles()` 中替换「应用已保存的颜色主题」分支：

```swift
        let saved = SettingsService.shared.settings
        if !saved.followSystemTheme, let theme = colorThemes.first(where: { $0.id == saved.colorTheme }) {
            payload["colorThemeCss"] = theme.css
            currentThemeId = theme.id
            applySystemAppearance(for: theme)
        } else {
            let dark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
            let id = manager.defaultThemeID(forDark: dark) ?? manager.defaultThemeId
            currentThemeId = id
            if let theme = colorThemes.first(where: { $0.id == id }) {
                payload["colorThemeCss"] = theme.css
                applySystemAppearance(for: theme)
            }
        }
```

- [ ] **Step 3: setTheme 守卫与 applySystemAppearance 跟随**

`setTheme(_:)` 开头：

```swift
        guard !isFollowSystemTheme else { return }
```

`applySystemAppearance(for:)` 改为：

```swift
    private func applySystemAppearance(for theme: ColorThemeInfo) {
        DispatchQueue.main.async { [weak self] in
            guard let self else { return }
            let dark = theme.isDark
            if self.isFollowSystemTheme {
                NSApp.appearance = nil
            } else {
                NSApp.appearance = dark ? NSAppearance(named: .darkAqua) : nil
            }
            self.applyScrollbarAppearance(dark: dark)
        }
    }
```

- [ ] **Step 4: 新增 applyFollowSystemTheme**

`EditorSession.swift`（`setTheme` 附近）：

```swift
    /// 跟随系统外观：重新解析默认主题并换肤（开关切换或系统外观变化时调用）。
    func applyFollowSystemTheme() {
        guard isFollowSystemTheme else { return }
        let dark = NSApp.effectiveAppearance.bestMatch(from: [.aqua, .darkAqua]) == .darkAqua
        guard let manager = StyleManager(directories: styleDirectories),
              let id = manager.defaultThemeID(forDark: dark), id != currentThemeId else { return }
        currentThemeId = id
        guard let theme = colorThemes.first(where: { $0.id == id }) else { return }
        applySystemAppearance(for: theme)
        var payload = manager.applyStylesPayload()
        payload["colorThemeCss"] = theme.css
        payload["activeStyle"] = currentStyleId
        send("applyStyles", payload: payload)
    }
```

- [ ] **Step 5: 验证**

```bash
cd /Users/nabian/Documents/Codex/2026-08-07/wo-e/work/markleaf
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift build --package-path macos --scratch-path /tmp/markleaf-t4-scratch
```

预期：全量测试通过（24/24），构建成功。

- [ ] **Step 6: 提交**

```bash
git add macos/Sources/MarkLeaf/Services/EditorSession.swift
git commit -m "feat(macos): follow system appearance for theme selection"
```

---

### Task 3: 偏好设置开关、菜单禁用与本地化

**Files:**
- Modify: `macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift`
- Modify: `macos/Sources/MarkLeaf/App/AppWindowManager.swift`
- Modify: `macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift`
- Modify: `macos/Sources/MarkLeaf/Services/L10n.swift`

**Interfaces:**
- Consumes: `EditorSession.applyFollowSystemTheme()`、`EditorSession.setTheme(_:)`、`AppSettings.followSystemTheme`。
- Produces: 偏好设置复选框「与操作系统同步」；主题下拉在跟随模式下禁用；外观菜单主题项禁用；`AppWindowManager.applyThemeModeToAll()`。

- [ ] **Step 1: 偏好设置复选框**

`PreferencesWindowController.swift`：
- 新增属性（放在 `themePopup` 附近，参照现有 checkbox 属性写法）：`private let followSystemCheck = NSButton(checkboxWithTitle: L10n.t("与操作系统同步"), target: nil, action: nil)`
- `appearancePage()` 中在「颜色主题」行之前插入：`.field("", followSystemCheck),`
- 在初始化主题下拉选中位置（`themePopup.selectItem` 附近）设置：`followSystemCheck.state = settings.followSystemTheme ? .on : .off` 与 `themePopup.isEnabled = !settings.followSystemTheme`
- `controlChanged()` 的 settings 更新块中加：`settings.followSystemTheme = followSystemCheck.state == .on`
- `controlChanged()` 末尾（`applyPreferencesToAll` 附近）追加：

```swift
            themePopup.isEnabled = followSystemCheck.state != .on
            AppWindowManager.shared.applyThemeModeToAll()
```

- [ ] **Step 2: AppWindowManager 主题模式刷新**

`AppWindowManager.swift` 追加：

```swift
    /// 跟随系统开关变化后刷新所有会话的主题（开→跟随系统；关→恢复手动主题）。
    func applyThemeModeToAll() {
        let follow = SettingsService.shared.settings.followSystemTheme
        for controller in windowControllers {
            if follow {
                controller.session.applyFollowSystemTheme()
            } else {
                controller.session.setTheme(SettingsService.shared.settings.colorTheme)
            }
        }
    }
```

- [ ] **Step 3: 外观菜单禁用主题项**

`NativeMenuBuilder.swift` 的 `validateMenuItem` 开头（switch 之前）追加：

```swift
        // 跟随系统外观时禁用主题选择（对应偏好设置置灰）
        if menuItem.action == #selector(chooseTheme(_:)) {
            return !SettingsService.shared.settings.followSystemTheme
        }
```

- [ ] **Step 4: L10n 三语**

`L10n.swift`：
- `zhHantTable`：`"与操作系统同步": "與作業系統同步",`
- `englishTable`：`"与操作系统同步": "Sync with OS",`

- [ ] **Step 5: 验证**

```bash
cd /Users/nabian/Documents/Codex/2026-08-07/wo-e/work/markleaf
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift test --package-path macos --scratch-path /tmp/markleaf-t4-scratch
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer CLANG_MODULE_CACHE_PATH=/tmp/markleaf-t4-cache/clang SWIFTPM_MODULECACHE_OVERRIDE=/tmp/markleaf-t4-cache/swiftpm swift build --package-path macos --scratch-path /tmp/markleaf-t4-scratch
DEVELOPER_DIR=/Applications/Xcode-beta.app/Contents/Developer macos/script/build_and_run.sh --verify
pkill -x MarkLeaf || true
```

预期：全量测试通过；构建成功；`--verify` 启动成功且验证后无残留进程。

- [ ] **Step 6: 提交**

```bash
git add macos/Sources/MarkLeaf/Views/PreferencesWindowController.swift macos/Sources/MarkLeaf/App/AppWindowManager.swift macos/Sources/MarkLeaf/Support/NativeMenuBuilder.swift macos/Sources/MarkLeaf/Services/L10n.swift
git commit -m "feat(macos): add sync-with-OS theme switch in preferences"
```
