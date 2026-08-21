import Foundation

func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        fputs("FAIL: \(message)\n", stderr)
        exit(1)
    }
}

let snippet = WorkspaceSearchPolicy.snippet(
    content: "before\nneedle appears here\nafter",
    query: "needle",
    nameMatches: false
)
expect(snippet == "before\nneedle appears here\nafter", "content matches should include nearby context")

let root = "/Users/test/Documents/挑战杯"
let elderGuard = root + "/ElderGuard"
let docs = elderGuard + "/docs"
let plans = docs + "/superpowers/plans"
let target = plans + "/2026-01-26-scam-event-chain.md"

let initialReveal = WorkspaceSearchPolicy.nextRevealStep(
    root: root,
    target: target,
    rootEntries: [WorkspaceRevealEntry(path: elderGuard, isDirectory: true)],
    childrenByDirectory: [:]
)
expect(
    initialReveal == .loadDirectory(path: elderGuard, expandedDirectories: [elderGuard]),
    "a cold nested reveal should request the first unloaded directory instead of timing out"
)

let completedReveal = WorkspaceSearchPolicy.nextRevealStep(
    root: root,
    target: target,
    rootEntries: [WorkspaceRevealEntry(path: elderGuard, isDirectory: true)],
    childrenByDirectory: [
        elderGuard: [WorkspaceRevealEntry(path: docs, isDirectory: true)],
        docs: [WorkspaceRevealEntry(path: docs + "/superpowers", isDirectory: true)],
        docs + "/superpowers": [WorkspaceRevealEntry(path: plans, isDirectory: true)],
        plans: [WorkspaceRevealEntry(path: target, isDirectory: false)],
    ]
)
expect(
    completedReveal == .selectFile(
        path: target,
        expandedDirectories: [elderGuard, docs, docs + "/superpowers", plans]
    ),
    "a fully loaded nested reveal should expand every ancestor and select the target"
)

let firstExpansion = WorkspaceSearchPolicy.nextExpansionStep(
    directories: [elderGuard, docs],
    visibleDirectories: [elderGuard],
    expandedDirectories: []
)
expect(
    firstExpansion == .expandDirectory(elderGuard),
    "the reveal should expand only the next ancestor that AppKit has made visible"
)

let waitForNestedRow = WorkspaceSearchPolicy.nextExpansionStep(
    directories: [elderGuard, docs],
    visibleDirectories: [elderGuard],
    expandedDirectories: [elderGuard]
)
expect(
    waitForNestedRow == .waitingForVisibleDirectory,
    "the reveal should stay pending while AppKit inserts the next nested row"
)

let nestedExpansion = WorkspaceSearchPolicy.nextExpansionStep(
    directories: [elderGuard, docs],
    visibleDirectories: [elderGuard, docs],
    expandedDirectories: [elderGuard]
)
expect(
    nestedExpansion == .expandDirectory(docs),
    "the reveal should resume with the nested directory once its row is visible"
)
print("PASS")
