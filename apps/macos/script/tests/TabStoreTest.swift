import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

func makeTab(_ path: String?) -> DocumentTab {
    DocumentTab(
        path: path,
        title: path.map { URL(fileURLWithPath: $0).lastPathComponent } ?? "未命名",
        encoding: "UTF-8",
        newLine: "LF"
    )
}

// 关闭后的相邻选择：优先右侧，其次左侧。
expect(TabSelectionPolicy.nextActiveIndex(closing: 1, count: 4) == 1, "closing middle tab should activate the right neighbor")
expect(TabSelectionPolicy.nextActiveIndex(closing: 3, count: 4) == 2, "closing the last tab should activate the left neighbor")
expect(TabSelectionPolicy.nextActiveIndex(closing: 0, count: 4) == 0, "closing the first tab should activate its right neighbor")
expect(TabSelectionPolicy.nextActiveIndex(closing: 0, count: 1) == nil, "closing the only tab should return nil (close window)")

let store = TabStore()
let a = makeTab("/tmp/A.md")
let b = makeTab("/tmp/B.md")
let c = makeTab("/tmp/C.md")
store.append(a)
store.append(b)
store.append(c)
expect(store.tabs.map(\.title) == ["A.md", "B.md", "C.md"], "append keeps insertion order")
expect(store.activeTabID == c.tabID, "appending activates the new tab by default")

// 身份去重查询。
expect(store.tab(withIdentity: FileIdentityPolicy.identity(forPath: "/tmp/B.md"))?.tabID == b.tabID,
       "tab(withIdentity:) should find the matching tab")
expect(store.tab(withIdentity: FileIdentityPolicy.identity(forPath: "/tmp/Z.md")) == nil,
       "unknown identity returns nil")

// 关闭中间标签激活右邻。
store.activate(a.tabID)
let next = store.close(b.tabID)
expect(next == c.tabID, "closing a non-active tab returns the right neighbor for reference")
expect(store.activeTabID == a.tabID, "closing a non-active tab does not change the active tab")

store.activate(a.tabID)
let afterClosingActive = store.close(a.tabID)
expect(afterClosingActive == c.tabID && store.activeTabID == c.tabID,
       "closing the active tab activates the right neighbor")

// `to` 是移动前坐标。此时状态为 [C, D]，移动 C 到坐标 2 后为 [D, C]。
store.append(makeTab("/tmp/D.md"))
store.move(from: 0, to: 2)
expect(store.tabs.map(\.title) == ["D.md", "C.md"], "move reorders tabs using pre-removal coordinates")

// 无路径文档序号。
let u1 = makeTab(nil)
u1.untitledSequence = store.nextUntitledSequence()
let u2 = makeTab(nil)
u2.untitledSequence = store.nextUntitledSequence()
expect(u1.untitledSequence == 1 && u2.untitledSequence == 2, "untitled sequence increases monotonically")
print("PASS")
