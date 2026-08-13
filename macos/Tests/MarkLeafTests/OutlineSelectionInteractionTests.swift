import AppKit
import XCTest
@testable import MarkLeaf

@MainActor
final class OutlineSelectionInteractionTests: XCTestCase {
    @MainActor
    func testFirstMouseClickSelectsHeadingAndActivatesExactlyOnce() throws {
        let probe = OutlineProbeDataSource()
        var activated: [Int] = []
        let outline = configuredOutline(probe: probe) { activated.append($0.position) }
        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 260, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        OutlineTestRetention.objects.append(contentsOf: [window, outline, probe])
        outline.layoutSubtreeIfNeeded()

        let rowRect = outline.rect(ofRow: 0)
        let windowPoint = outline.convert(NSPoint(x: rowRect.midX, y: rowRect.midY), to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let mouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 1,
            clickCount: 1,
            pressure: 1
        ))
        let mouseUp = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseUp,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp + 0.01,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 2,
            clickCount: 1,
            pressure: 0
        ))

        NSApp.postEvent(mouseUp, atStart: true)
        outline.mouseDown(with: mouseDown)

        XCTAssertEqual(outline.selectedRow, 0)
        XCTAssertEqual(activated, [0])
        XCTAssertTrue(outline.rowView(atRow: 0, makeIfNecessary: true)?.isSelected == true)
    }

    func testFirstNativeSelectionSelectsHeadingAndActivatesExactlyOnce() {
        let probe = OutlineProbeDataSource()
        var activated: [Int] = []
        let outline = configuredOutline(probe: probe) { activated.append($0.position) }

        outline.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false)

        XCTAssertEqual(outline.selectedRow, 0)
        XCTAssertEqual(activated, [0])
    }

    func testProgrammaticSelectionDoesNotEchoActivation() {
        let probe = OutlineProbeDataSource()
        var activated: [Int] = []
        let outline = configuredOutline(probe: probe) { activated.append($0.position) }

        outline.synchronizeSelection(to: 20)

        XCTAssertEqual(outline.selectedRow, 1)
        XCTAssertTrue(activated.isEmpty)
    }

    func testReloadRestoresActiveHeadingAndMissingPositionClearsSelection() {
        let probe = OutlineProbeDataSource()
        let outline = configuredOutline(probe: probe) { _ in }

        outline.reloadData(activePosition: 20)
        XCTAssertEqual(outline.selectedRow, 1)

        outline.reloadData(activePosition: 999)
        XCTAssertEqual(outline.selectedRow, -1)
    }

    func testEditorSessionSeparatesOutlineContentAndSelectionCallbacks() {
        let session = EditorSession()
        var contentChanges = 0
        var selectionChanges = 0
        session.onOutlineChanged = { contentChanges += 1 }
        session.onOutlineSelectionChanged = { selectionChanges += 1 }

        session.handleEditorMessage([
            "type": "outlineChanged",
            "payload": ["headings": [["level": 1, "text": "First", "position": 0]]],
        ])
        XCTAssertEqual(contentChanges, 1)
        XCTAssertEqual(selectionChanges, 0)

        session.handleEditorMessage([
            "type": "outlineSelectionChanged",
            "payload": ["position": 0],
        ])
        XCTAssertEqual(contentChanges, 1)
        XCTAssertEqual(selectionChanges, 1)
        XCTAssertEqual(session.activeOutlinePosition, 0)

        session.handleEditorMessage([
            "type": "outlineSelectionChanged",
            "payload": ["position": NSNull()],
        ])
        XCTAssertEqual(contentChanges, 1)
        XCTAssertEqual(selectionChanges, 2)
        XCTAssertNil(session.activeOutlinePosition)
    }

    private func configuredOutline(
        probe: OutlineProbeDataSource,
        onHeadingActivated: @escaping (OutlineHeading) -> Void
    ) -> OutlineTreeView {
        let outline = OutlineTreeView(frame: NSRect(x: 0, y: 0, width: 260, height: 180))
        outline.configure(session: EditorSession(), onHeadingActivated: onHeadingActivated)
        outline.dataSource = probe
        outline.reloadData()
        outline.layoutSubtreeIfNeeded()
        return outline
    }

}

@MainActor
private enum OutlineTestRetention {
    static var objects: [AnyObject] = []
}

private final class OutlineProbeDataSource: NSObject, NSOutlineViewDataSource {
    let headings = [
        OutlineHeading(level: 1, text: "First", position: 0),
        OutlineHeading(level: 2, text: "Second", position: 20),
    ]

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        item == nil ? headings.count : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        headings[index]
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        false
    }
}
