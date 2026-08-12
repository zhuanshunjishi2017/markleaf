import AppKit
import XCTest
@testable import MarkLeaf

final class WorkspaceTreeMouseInteractionTests: XCTestCase {
    @MainActor
    func testMouseDownOnExpandedDirectoryNameKeepsRowExpanded() throws {
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
        outline.expandItem(directoryItem)
        XCTAssertTrue(outline.isItemExpanded(directoryItem))

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
        outline.expandItem(directoryItem)
        XCTAssertTrue(outline.isItemExpanded(directoryItem))

        let rowRect = outline.rect(ofRow: 0)
        let point = NSPoint(x: rowRect.maxX - 24, y: rowRect.midY)
        let windowPoint = outline.convert(point, to: nil)
        let timestamp = ProcessInfo.processInfo.systemUptime
        let firstMouseDown = try XCTUnwrap(NSEvent.mouseEvent(
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
        let firstMouseUp = try XCTUnwrap(NSEvent.mouseEvent(
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

        NSApp.postEvent(firstMouseUp, atStart: true)
        outline.mouseDown(with: firstMouseDown)
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

        let forwardedEvent = try XCTUnwrap(outline.forwardedDisclosureMouseDown)
        let forwardedPoint = outline.convert(forwardedEvent.locationInWindow, from: nil)
        XCTAssertTrue(outline.frameOfOutlineCell(atRow: 0).contains(forwardedPoint))
    }

    @MainActor
    func testDoubleClickOnCollapsedDirectoryNameExpandsRow() throws {
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
        XCTAssertEqual(outline.numberOfRows, 1)
        let directoryItem = try XCTUnwrap(outline.item(atRow: 0))
        _ = try XCTUnwrap(directoryItem as? WorkspaceEntry)
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
    var forwardedDisclosureMouseDown: NSEvent?

    override func performNativeDisclosureMouseDown(_ event: NSEvent) {
        forwardedDisclosureMouseDown = event
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
