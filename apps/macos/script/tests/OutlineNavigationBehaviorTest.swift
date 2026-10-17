import AppKit
@testable import MarkLeaf

_ = NSApplication.shared
var failures = [String]()
let session = EditorSession()
session.handleEditorMessage(["type": "outlineChanged", "payload": ["headings": [
    ["level": 1, "text": "Parent", "position": 1],
    ["level": 2, "text": "Child", "position": 10]]]])
session.handleEditorMessage(["type": "outlineSelectionChanged", "payload": ["position": 10]])
let tree = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 300, height: 300))
tree.configure(session: session)
tree.reloadData(activePosition: 10)
if tree.numberOfRows != 2 { failures.append("fixture: must render both headings") }
tree.collapseItem(tree.item(atRow: 0))
tree.reloadData(activePosition: 10)
if tree.numberOfRows != 1 { failures.append("outline refresh must preserve collapsed branches") }
tree.perform(NSSelectorFromString("locateCurrentHeading"))
if tree.numberOfRows != 2 || tree.selectedRow != 1 { failures.append("locate must reveal the current heading's ancestors") }
session.handleEditorMessage(["type": "outlineChanged", "payload": ["headings": [
    ["level": 1, "text": "Parent", "position": 6],
    ["level": 2, "text": "Child", "position": 15]]]])
tree.reloadData(activePosition: 15)
if tree.numberOfRows != 2 { failures.append("inserting text before headings must retain expanded branches") }
tree.setFilter("no matching heading")
tree.perform(NSSelectorFromString("expandAllHeadings"))
if tree.numberOfRows != 2 { failures.append("expand all must clear search and expose the real hierarchy") }
tree.setFilter("Child")
tree.perform(NSSelectorFromString("collapseAllHeadings"))
if tree.numberOfRows != 1 || (tree.item(atRow: 0) as? OutlineNode)?.heading.text != "Parent" {
    failures.append("collapse all must clear search and collapse the real hierarchy")
}
for failure in failures { fputs("FAIL: \(failure)\n", stderr) }
if !failures.isEmpty { exit(1) }
print("PASS: outline refresh, explicit locate and search-aware hierarchy commands")
