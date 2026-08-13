import AppKit
import XCTest
@testable import MarkLeaf

final class WorkspaceTreeMouseInteractionTests: XCTestCase {
    @MainActor
    func testWorkspaceTreeKeepsNativeSourceListGeometryWithGraySelection() throws {
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        let entry = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)

        let rowView = try XCTUnwrap(outline.outlineView(outline, rowViewForItem: entry))
        rowView.isEmphasized = true

        XCTAssertTrue(rowView is FinderWorkspaceRowView)
        XCTAssertEqual(outline.selectionHighlightStyle, .sourceList)
        XCTAssertFalse(rowView.isEmphasized)
    }

    @MainActor
    func testDoubleClickOnActualDirectoryNameForwardsClickToDisclosureFrame() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = DisclosureRoutingWorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        outline.layoutSubtreeIfNeeded()
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        cell.layoutSubtreeIfNeeded()
        let textField = try XCTUnwrap(cell.textField)
        textField.layoutSubtreeIfNeeded()
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        XCTAssertFalse(outline.frameOfOutlineCell(atRow: 0).contains(point))
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let mouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 1,
            clickCount: 2,
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
            clickCount: 2,
            pressure: 0
        ))

        NSApp.postEvent(mouseUp, atStart: true)
        outline.mouseDown(with: mouseDown)

        XCTAssertNotNil(outline.forwardedDisclosureButton)
    }

    @MainActor
    func testDoubleClickOnActualDirectoryNameExpandsRow() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        outline.layoutSubtreeIfNeeded()
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        let textField = try XCTUnwrap(cell.textField)
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let mouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 1,
            clickCount: 2,
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
            clickCount: 2,
            pressure: 0
        ))

        NSApp.postEvent(mouseUp, atStart: true)
        outline.mouseDown(with: mouseDown)

        XCTAssertTrue(outline.isItemExpanded(directoryItem))
    }

    @MainActor
    func testMarkdownFileCanBecomeSelectedAfterOpening() {
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        let file = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)

        XCTAssertTrue(outline.outlineView(outline, shouldSelectItem: file))
    }

    @MainActor
    func testSelectingMarkdownFileDoesNotActivateBeforeMouseUp() {
        let outline = ActivationProbeWorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        let file = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)

        XCTAssertTrue(outline.outlineView(outline, shouldSelectItem: file))

        XCTAssertTrue(outline.activatedEntries.isEmpty)
    }

    @MainActor
    func testRealAppKitClickOnMarkdownFileActivatesExactlyOnce() throws {
        let dataSource = WorkspaceFileProbeDataSource()
        let outline = ActivationProbeWorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource
        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])
        outline.reloadData()
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
        NSApp.sendEvent(mouseDown)

        XCTAssertEqual(outline.activatedEntries.map(\.path), ["/probe/file.md"])
    }

    @MainActor
    func testFirstClickOnDirectoryNameSelectsWithoutExpandingRow() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        XCTAssertEqual(outline.selectedRow, -1)
        XCTAssertFalse(outline.isItemExpanded(directoryItem))

        outline.layoutSubtreeIfNeeded()
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        let textField = try XCTUnwrap(cell.textField)
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        let windowPoint = outline.convert(point, to: nil)
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
        XCTAssertFalse(outline.isItemExpanded(directoryItem))

        let secondMouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp + 0.1,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 3,
            clickCount: 1,
            pressure: 1
        ))
        let secondMouseUp = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseUp,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp + 0.11,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 4,
            clickCount: 1,
            pressure: 0
        ))

        NSApp.postEvent(secondMouseUp, atStart: true)
        outline.mouseDown(with: secondMouseDown)

        XCTAssertTrue(outline.isItemExpanded(directoryItem))
    }

    @MainActor
    func testMouseDownOnCollapsedDirectoryNameKeepsRowCollapsed() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource
        outline.delegate = nil

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        XCTAssertFalse(outline.isItemExpanded(directoryItem))

        let rowRect = outline.rect(ofRow: 0)
        let point = NSPoint(x: rowRect.maxX - 24, y: rowRect.midY)
        let windowPoint = outline.convert(point, to: nil)
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

        XCTAssertFalse(outline.isItemExpanded(directoryItem))
    }

    @MainActor
    func testDoubleClickOnExpandedDirectoryNameCollapsesRow() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        outline.expandItem(directoryItem)
        XCTAssertTrue(outline.isItemExpanded(directoryItem))

        outline.layoutSubtreeIfNeeded()
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        cell.layoutSubtreeIfNeeded()
        let textField = try XCTUnwrap(cell.textField)
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        XCTAssertFalse(outline.frameOfOutlineCell(atRow: 0).contains(point))
        XCTAssertEqual(outline.row(at: point), 0)
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let secondMouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp + 0.1,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 3,
            clickCount: 2,
            pressure: 1
        ))
        let secondMouseUp = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseUp,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp + 0.11,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 4,
            clickCount: 2,
            pressure: 0
        ))
        let eventPoint = outline.convert(secondMouseDown.locationInWindow, from: nil)
        XCTAssertEqual(outline.row(at: eventPoint), 0)
        XCTAssertFalse(outline.frameOfOutlineCell(atRow: 0).contains(eventPoint))

        NSApp.postEvent(secondMouseUp, atStart: true)
        outline.mouseDown(with: secondMouseDown)

        XCTAssertFalse(outline.isItemExpanded(directoryItem))
    }

    @MainActor
    func testDoubleClickOnDirectoryNameForwardsClickToDisclosureFrame() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = DisclosureRoutingWorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        outline.layoutSubtreeIfNeeded()
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        let textField = try XCTUnwrap(cell.textField)
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let mouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 1,
            clickCount: 2,
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
            clickCount: 2,
            pressure: 0
        ))

        NSApp.postEvent(mouseUp, atStart: true)
        outline.mouseDown(with: mouseDown)

        XCTAssertNotNil(outline.forwardedDisclosureButton)
    }

    @MainActor
    func testDoubleClickOnCollapsedDirectoryNameExpandsRow() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        XCTAssertEqual(outline.numberOfRows, 1)
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        _ = try XCTUnwrap(directoryItem as? WorkspaceEntry)
        XCTAssertFalse(outline.isItemExpanded(directoryItem))

        outline.layoutSubtreeIfNeeded()
        let cell = try XCTUnwrap(outline.view(atColumn: 0, row: 0, makeIfNecessary: true) as? NSTableCellView)
        let textField = try XCTUnwrap(cell.textField)
        let point = outline.convert(
            NSPoint(x: textField.bounds.midX, y: textField.bounds.midY),
            from: textField
        )
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let mouseDown = try XCTUnwrap(NSEvent.mouseEvent(
            with: .leftMouseDown,
            location: windowPoint,
            modifierFlags: [],
            timestamp: timestamp,
            windowNumber: window.windowNumber,
            context: nil,
            eventNumber: 1,
            clickCount: 2,
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
            clickCount: 2,
            pressure: 0
        ))

        NSApp.postEvent(mouseUp, atStart: true)
        outline.mouseDown(with: mouseDown)

        XCTAssertTrue(outline.isItemExpanded(directoryItem))
    }

    @MainActor
    func testTypedWorkspaceEntryPreservesOutlineItemIdentity() throws {
        let dataSource = WorkspaceTreeProbeDataSource()
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource
        outline.delegate = nil

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        let originalItem = try XCTUnwrap(outline.item(atRow: 0))
        let typedEntry = try XCTUnwrap(originalItem as? WorkspaceEntry)

        outline.expandItem(typedEntry)

        XCTAssertTrue(outline.isItemExpanded(originalItem))
    }

    @MainActor
    func testReloadDirectoryChildrenMakesNewChildVisible() throws {
        let dataSource = WorkspaceTreeProbeDataSource(includeChild: false)
        let outline = WorkspaceTreeView(frame: NSRect(x: 0, y: 0, width: 320, height: 180))
        outline.configure(session: EditorSession())
        outline.dataSource = dataSource
        outline.delegate = nil

        let window = NSWindow(
            contentRect: NSRect(x: 100, y: 100, width: 320, height: 180),
            styleMask: [.titled],
            backing: .buffered,
            defer: false
        )
        window.contentView = outline
        window.makeKeyAndOrderFront(nil)
        WorkspaceTreeTestRetention.objects.append(contentsOf: [window, outline, dataSource])

        outline.reloadData()
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        let directory = try XCTUnwrap(directoryItem as? WorkspaceEntry)
        outline.expandItem(directory)
        XCTAssertEqual(outline.numberOfRows, 1)

        dataSource.includeChild = true
        outline.reloadDirectoryChildren(directory)

        XCTAssertEqual(outline.numberOfRows, 2)
    }
}

private final class DisclosureRoutingWorkspaceTreeView: WorkspaceTreeView {
    var forwardedDisclosureButton: NSButton?

    override func performNativeDisclosureClick(_ disclosureButton: NSButton) {
        forwardedDisclosureButton = disclosureButton
    }
}

private final class ActivationProbeWorkspaceTreeView: WorkspaceTreeView {
    var activatedEntries: [WorkspaceEntry] = []

    override func activateWorkspaceEntry(_ entry: WorkspaceEntry) {
        activatedEntries.append(entry)
    }
}

@MainActor
private enum WorkspaceTreeTestRetention {
    static var objects: [AnyObject] = []
}

private final class WorkspaceTreeProbeDataSource: NSObject, NSOutlineViewDataSource {
    private let directory = WorkspaceEntry(name: "folder", path: "/probe/folder", isDirectory: true)
    private let child = WorkspaceEntry(name: "file.md", path: "/probe/folder/file.md", isDirectory: false)
    var includeChild: Bool

    init(includeChild: Bool = true) {
        self.includeChild = includeChild
    }

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        guard let entry = item as? WorkspaceEntry else { return 1 }
        return entry.isDirectory && includeChild ? 1 : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        item == nil ? directory : child
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        (item as? WorkspaceEntry)?.isDirectory == true
    }
}

private final class WorkspaceFileProbeDataSource: NSObject, NSOutlineViewDataSource {
    private let file = WorkspaceEntry(name: "file.md", path: "/probe/file.md", isDirectory: false)

    func outlineView(_ outlineView: NSOutlineView, numberOfChildrenOfItem item: Any?) -> Int {
        item == nil ? 1 : 0
    }

    func outlineView(_ outlineView: NSOutlineView, child index: Int, ofItem item: Any?) -> Any {
        file
    }

    func outlineView(_ outlineView: NSOutlineView, isItemExpandable item: Any) -> Bool {
        false
    }
}
