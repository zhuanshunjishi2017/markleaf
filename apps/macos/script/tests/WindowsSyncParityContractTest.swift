import AppKit
import Foundation

struct ParityRequirement {
    let windowsName: String
    let macCommand: String
}

// Complete Windows 1.5.2 ShortcutCatalog inventory, mapped to macOS command
// identifiers where the command name intentionally differs between hosts.
let shortcutRequirements: [ParityRequirement] = [
    ParityRequirement(windowsName: "NewDocument", macCommand: "new"),
    ParityRequirement(windowsName: "NewPlainTextDocument", macCommand: "newPlainText"),
    ParityRequirement(windowsName: "OpenDocument", macCommand: "open"),
    ParityRequirement(windowsName: "SaveDocument", macCommand: "save"),
    ParityRequirement(windowsName: "SaveDocumentAs", macCommand: "saveAs"),
    ParityRequirement(windowsName: "Print", macCommand: "print"),
    ParityRequirement(windowsName: "Undo", macCommand: "undo"),
    ParityRequirement(windowsName: "Redo", macCommand: "redo"),
    ParityRequirement(windowsName: "Cut", macCommand: "cut"),
    ParityRequirement(windowsName: "Copy", macCommand: "copy"),
    ParityRequirement(windowsName: "Paste", macCommand: "paste"),
    ParityRequirement(windowsName: "PastePlainText", macCommand: "pastePlainText"),
    ParityRequirement(windowsName: "Find", macCommand: "find"),
    ParityRequirement(windowsName: "Replace", macCommand: "find"),
    ParityRequirement(windowsName: "SelectAll", macCommand: "selectAll"),
    ParityRequirement(windowsName: "SetHeading1", macCommand: "setHeading1"),
    ParityRequirement(windowsName: "SetHeading2", macCommand: "setHeading2"),
    ParityRequirement(windowsName: "SetHeading3", macCommand: "setHeading3"),
    ParityRequirement(windowsName: "SetHeading4", macCommand: "setHeading4"),
    ParityRequirement(windowsName: "SetHeading5", macCommand: "setHeading5"),
    ParityRequirement(windowsName: "SetHeading6", macCommand: "setHeading6"),
    ParityRequirement(windowsName: "ToggleBold", macCommand: "toggleBold"),
    ParityRequirement(windowsName: "ToggleItalic", macCommand: "toggleItalic"),
    ParityRequirement(windowsName: "ToggleUnderline", macCommand: "toggleUnderline"),
    ParityRequirement(windowsName: "FormatPainter", macCommand: "formatPainter"),
    ParityRequirement(windowsName: "SetParagraph", macCommand: "setParagraph"),
    ParityRequirement(windowsName: "ToggleQuote", macCommand: "toggleBlockquote"),
    ParityRequirement(windowsName: "InsertMathBlock", macCommand: "insertMathBlock"),
    ParityRequirement(windowsName: "ToggleCodeBlock", macCommand: "toggleCodeBlock"),
    ParityRequirement(windowsName: "NewWindow", macCommand: "newWindow"),
    ParityRequirement(windowsName: "OpenFolder", macCommand: "openFolder"),
    ParityRequirement(windowsName: "ToggleHighlight", macCommand: "toggleHighlight"),
    ParityRequirement(windowsName: "ToggleInlineCode", macCommand: "toggleCode"),
    ParityRequirement(windowsName: "InsertLink", macCommand: "insertLink"),
    ParityRequirement(windowsName: "ToggleStrike", macCommand: "toggleStrike"),
    ParityRequirement(windowsName: "ClearFormat", macCommand: "clearFormat"),
    ParityRequirement(windowsName: "InsertMathInline", macCommand: "insertMathInline"),
    ParityRequirement(windowsName: "ToggleEditorFocusMode", macCommand: "toggleEditorFocusMode"),
    ParityRequirement(windowsName: "ToggleEditorTypewriterMode", macCommand: "toggleTypewriterMode"),
    ParityRequirement(windowsName: "ToggleSourceMode", macCommand: "sourceMode"),
    ParityRequirement(windowsName: "InsertHorizontalRule", macCommand: "insertHorizontalRule"),
    ParityRequirement(windowsName: "ToggleSidebar", macCommand: "toggleSidebar"),
    ParityRequirement(windowsName: "ShowStatusBar", macCommand: "toggleStatusBar"),
    ParityRequirement(windowsName: "ZoomIn", macCommand: "zoomIn"),
    ParityRequirement(windowsName: "ZoomOut", macCommand: "zoomOut"),
    ParityRequirement(windowsName: "ZoomReset", macCommand: "resetZoom"),
    ParityRequirement(windowsName: "CloseFolder", macCommand: "closeFolder"),
    ParityRequirement(windowsName: "ToggleBulletList", macCommand: "toggleBulletList"),
    ParityRequirement(windowsName: "ToggleOrderedList", macCommand: "toggleOrderedList"),
    ParityRequirement(windowsName: "ToggleTaskList", macCommand: "toggleTaskList"),
    ParityRequirement(windowsName: "IncreaseListIndent", macCommand: "indentListItem"),
    ParityRequirement(windowsName: "DecreaseListIndent", macCommand: "outdentListItem"),
    ParityRequirement(windowsName: "InsertTable", macCommand: "insertTable"),
    ParityRequirement(windowsName: "InsertFootnote", macCommand: "insertFootnote"),
    ParityRequirement(windowsName: "PromoteHeading", macCommand: "promoteHeading"),
    ParityRequirement(windowsName: "DemoteHeading", macCommand: "demoteHeading"),
    ParityRequirement(windowsName: "ToggleFocusMode", macCommand: "toggleFocusMode"),
]

let nativeRequirements: [ParityRequirement] = [
    ParityRequirement(windowsName: "ToggleHighlight", macCommand: "toggleHighlight"),
    ParityRequirement(windowsName: "ToggleEditorFocusMode", macCommand: "toggleEditorFocusMode"),
    ParityRequirement(windowsName: "ToggleTypewriterMode", macCommand: "toggleTypewriterMode"),
    ParityRequirement(windowsName: "InsertAlertNote", macCommand: "insertAlertNote"),
    ParityRequirement(windowsName: "InsertAlertTip", macCommand: "insertAlertTip"),
    ParityRequirement(windowsName: "InsertAlertImportant", macCommand: "insertAlertImportant"),
    ParityRequirement(windowsName: "InsertAlertWarning", macCommand: "insertAlertWarning"),
    ParityRequirement(windowsName: "InsertAlertCaution", macCommand: "insertAlertCaution"),
    ParityRequirement(windowsName: "ShowFrontMatter", macCommand: "showFrontMatter"),
    ParityRequirement(windowsName: "SetMathNumber", macCommand: "setMathNumber"),
    ParityRequirement(windowsName: "CopyHtml", macCommand: "copyHtml"),
    ParityRequirement(windowsName: "RestartEditor", macCommand: "restartEditor"),
    ParityRequirement(windowsName: "LearnMarkdown", macCommand: "learnMarkdown"),
]

let specialKeyProbes = [
    "`": "toggleCode",
    "\\": "clearFormat",
    "/": "insertHorizontalRule",
    "[": "outdentListItem",
    "]": "indentListItem",
]
let specialKeys = Array(specialKeyProbes.keys).sorted()

guard CommandLine.arguments.count == 4 else {
    fputs("FAIL: expected NativeMenuBuilder, EditorCommandRouter, and context-menu paths\n", stderr)
    exit(2)
}

var failureSections: [(title: String, items: [String])] = []

let shortcutCommands = Set(ShortcutCatalog.entries.map(\.command))
let missingShortcuts = shortcutRequirements
    .filter { !shortcutCommands.contains($0.macCommand) }
    .sorted { $0.macCommand < $1.macCommand }
    .map { "\($0.macCommand) (Windows: \($0.windowsName))" }
if !missingShortcuts.isEmpty {
    failureSections.append((title: "Shortcut catalog", items: missingShortcuts))
}

let nativeSource = try CommandLine.arguments.dropFirst()
    .map { try String(contentsOfFile: $0, encoding: .utf8) }
    .joined(separator: "\n")
let missingNative = nativeRequirements
    .filter { !nativeSource.contains("\"\($0.macCommand)\"") }
    .sorted { $0.macCommand < $1.macCommand }
    .map { "\($0.macCommand) (Windows: \($0.windowsName))" }
if !missingNative.isEmpty {
    failureSections.append((title: "Native command surface", items: missingNative))
}

let validationFailures = specialKeys.filter { key in
    ShortcutSettings.validate(key: key, mask: [.command], for: specialKeyProbes[key] ?? "parityContractProbe") != .none
}
let validationDiagnostics = validationFailures.map { key in
    "\(key): \(ShortcutSettings.validate(key: key, mask: [.command], for: specialKeyProbes[key] ?? "parityContractProbe"))"
}
let invalidDefaultFailures = ShortcutCatalog.entries.compactMap { entry -> String? in
    guard !entry.defaultKey.isEmpty else { return nil }
    let result = ShortcutSettings.validate(
        key: entry.defaultKey,
        mask: entry.defaultMask,
        for: entry.command
    )
    return result == .none ? nil : "\(entry.command): \(result)"
}
let defaultBindings = ShortcutCatalog.entries.compactMap { entry -> String? in
    guard !entry.defaultKey.isEmpty else { return nil }
    return "\(entry.defaultKey)|\(entry.defaultMask.rawValue)"
}
let duplicateDefaultFailures = Dictionary(grouping: defaultBindings, by: \.self)
    .filter { $0.value.count > 1 }
    .keys.sorted()
if !validationFailures.isEmpty {
    failureSections.append((title: "Shortcut key validation", items: validationDiagnostics))
}
if !invalidDefaultFailures.isEmpty {
    failureSections.append((title: "Invalid shortcut defaults", items: invalidDefaultFailures))
}
if !duplicateDefaultFailures.isEmpty {
    failureSections.append((title: "Duplicate shortcut defaults", items: duplicateDefaultFailures))
}

let displayFailures = specialKeys.filter { key in
    ShortcutDisplay.string(key: key, mask: [.command, .shift]) != "⇧⌘\(key)"
}
let displayDiagnostics = displayFailures.map { key in
    "\(key): \(ShortcutDisplay.string(key: key, mask: [.command, .shift]))"
}
if !displayFailures.isEmpty {
    failureSections.append((title: "Shortcut key display", items: displayDiagnostics))
}

if failureSections.isEmpty {
    print("PASS")
    exit(0)
}

fputs("FAIL: Windows 1.5.2-1.6.0 macOS parity gaps\n", stderr)
for section in failureSections {
    fputs("\(section.title):\n", stderr)
    for item in section.items {
        fputs("- \(item)\n", stderr)
    }
}
exit(1)
