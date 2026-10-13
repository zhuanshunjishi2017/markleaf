import AppKit

enum L10n { static func t(_ value: String) -> String { value } }

func buttons(in view: NSView) -> [NSButton] {
    view.subviews.flatMap { child in
        if let button = child as? NSButton { return [button] }
        return buttons(in: child)
    }
}

let commands = ["toggleBold", "toggleItalic", "toggleUnderline", "toggleStrike", "toggleHighlight", "clearFormat"]
let actions = Dictionary(uniqueKeysWithValues: commands.map { ($0, EditorActionState(enabled: true, checked: false)) })
let toolbar = NativeFormatToolbarView(actions: actions, canStartFormatPainter: true, formatPainterArmed: false)
let controls = buttons(in: toolbar)
func assertLayout(_ toolbar: NativeFormatToolbarView, withHeadings: Bool = false) {
    toolbar.layoutSubtreeIfNeeded()
    let controls = buttons(in: toolbar)
    precondition(controls.count == (withHeadings ? 9 : 7), "heading actions must participate in the shared layout")
    let separators = toolbar.subviews.compactMap { $0 as? NSStackView }
        .flatMap { $0.arrangedSubviews.filter { !($0 is NSButton) } }
    precondition(separators.count == (withHeadings ? 2 : 1), "each functional group retains its divider")
    precondition(separators.allSatisfy { ($0 as? NSBox)?.boxType == .separator },
        "group dividers must use AppKit separators")
    precondition(toolbar.subviews.count == 1,
        "the horizontal divider belongs to NSMenu, outside the padded button row")
    var previous: NSRect?
    for (index, button) in controls.enumerated() {
        let frame = toolbar.convert(button.bounds, from: button)
        for separator in separators {
            let dividerFrame = toolbar.convert(separator.bounds, from: separator)
            precondition(frame.minY == dividerFrame.minY && frame.maxY == dividerFrame.maxY,
                "compact button edges must align with the existing vertical group divider")
        }
        precondition(button.bounds.size == NSSize(width: 18, height: 18),
            "\(button.identifier!.rawValue): symbol alignment metadata must not enlarge the actual button frame")
        precondition(frame.minY == 4 && frame.maxY == 22, "\(button.identifier!.rawValue) frame=\(frame) toolbar=\(toolbar.bounds)")
        precondition(frame.minX >= 16 && frame.maxX <= toolbar.bounds.width - 16, "the button row must respect the native menu separator's side insets")
        if let previous {
            let startsGroup = index == 5 || (withHeadings && index == 7)
            precondition(frame.minX - previous.maxX == (startsGroup ? 17 : 2),
                "only explicit group separators may increase the shared button spacing")
        }
        previous = frame
        let imageRect = button.cell!.imageRect(forBounds: button.bounds)
        precondition(imageRect == NSRect(x: 2.5, y: 2.5, width: 13, height: 13),
            "all symbols must use one centered icon canvas, independent of their font baseline")
        precondition(button.toolTip != nil && button.accessibilityLabel() == button.toolTip,
            "normalizing icon artwork must retain tooltips and accessibility labels")
    }
}
assertLayout(toolbar)
precondition(controls.count == 7, "ordinary text has seven quick actions, without heading controls")
for button in controls {
    precondition(String(describing: type(of: button)) == "NativeFormatToolbarButton", "AppKit factory must retain the hover-capable subclass")
    precondition(button.refusesFirstResponder && button.focusRingType == .none, "opening a menu must not focus the first button")
    precondition(button.isEnabled, "enabled editor actions stay available")
}
let hoverEvent = NSEvent.enterExitEvent(with: .mouseEntered, location: .zero,
    modifierFlags: [], timestamp: 0, windowNumber: 0, context: nil,
    eventNumber: 0, trackingNumber: 0, userData: nil)!
let bold = controls[0]
// Exercise the real drawing path: the normal bezel must cover the same tall
// footprint as the menu hover highlight, rather than only a short native bezel.
func renderButton(_ button: NSButton, scale: CGFloat = 1) -> NSBitmapImageRep {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil,
        pixelsWide: Int(button.bounds.width * scale), pixelsHigh: Int(button.bounds.height * scale),
        bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
        colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    let bitmapContext = NSGraphicsContext(bitmapImageRep: bitmap)!
    NSGraphicsContext.current = bitmapContext
    NSColor.clear.setFill()
    NSRect(x: 0, y: 0, width: CGFloat(bitmap.pixelsWide), height: CGFloat(bitmap.pixelsHigh)).fill(using: .copy)
    let context = bitmapContext.cgContext
    if button.isFlipped {
        context.translateBy(x: 0, y: CGFloat(bitmap.pixelsHigh))
        context.scaleBy(x: scale, y: -scale)
    } else {
        context.scaleBy(x: scale, y: scale)
    }
    NSGraphicsContext.current = NSGraphicsContext(cgContext: context, flipped: button.isFlipped)
    button.effectiveAppearance.performAsCurrentDrawingAppearance { button.draw(button.bounds) }
    NSGraphicsContext.restoreGraphicsState()
    return bitmap
}
func backgroundAlpha(_ button: NSButton, x: Int, y: Int) -> CGFloat {
    renderButton(button).colorAt(x: x, y: y)!.alphaComponent
}
precondition(backgroundAlpha(bold, x: 10, y: 2) > 0.01,
    "normal background must extend to the same top edge as the tall hover highlight")
precondition(backgroundAlpha(bold, x: 10, y: 17) > 0.01,
    "normal background must extend to the same bottom edge as the tall hover highlight")
let normalFrame = bold.frame
func alignmentInsets(_ button: NSButton) -> [CGFloat] {
    let insets = button.alignmentRectInsets
    return [insets.top, insets.left, insets.bottom, insets.right]
}
let normalAlignmentInsets = alignmentInsets(bold)
bold.state = .on
var previews: [String?] = []
toolbar.onPreview = { previews.append($0) }
bold.mouseEntered(with: hoverEvent)
precondition(bold.contentTintColor == .selectedMenuItemTextColor && !bold.isBordered,
    "hover must use the native selected-menu foreground, not a keyboard focus ring")
precondition(bold.state == .on && previews.last! == "toggleBold", "hover preserves the checked state and previews the text format")
precondition(bold.frame == normalFrame && alignmentInsets(bold) == normalAlignmentInsets,
    "hover must not change the button layout or alignment geometry")
precondition(backgroundAlpha(bold, x: 10, y: 2) > 0.9 && backgroundAlpha(bold, x: 10, y: 17) > 0.9,
    "hover highlight retains its full-height footprint")
let italic = controls[1]
italic.mouseEntered(with: hoverEvent)
bold.mouseExited(with: hoverEvent)
precondition(bold.contentTintColor == .labelColor && !bold.isBordered && bold.state == .on,
    "leaving restores normal native artwork without toggling the format")
precondition(previews.last! == "toggleItalic", "a late exit from the old button must not cancel the newly hovered button preview")
italic.mouseExited(with: hoverEvent)
precondition(previews.last! == nil, "leaving cancels only the temporary text preview")
bold.isEnabled = false
bold.mouseEntered(with: hoverEvent)
_ = backgroundAlpha(bold, x: 10, y: 2)
precondition(bold.contentTintColor == .tertiaryLabelColor && !bold.isBordered, "disabled actions must not appear hovered")
italic.mouseEntered(with: hoverEvent)
italic.viewDidMoveToWindow()
precondition(italic.contentTintColor == .labelColor && !italic.isBordered && previews.last! == nil,
    "detaching the menu row restores the button and ends the text preview")
var headingActions = actions
headingActions["promoteHeading"] = EditorActionState(enabled: true, checked: false)
headingActions["demoteHeading"] = EditorActionState(enabled: false, checked: false)
let headingToolbar = NativeFormatToolbarView(actions: headingActions, canStartFormatPainter: false, formatPainterArmed: false)
let minimumWidth = headingToolbar.frame.width
assertLayout(headingToolbar, withHeadings: true)
headingToolbar.frame.size.width += 160
assertLayout(headingToolbar, withHeadings: true)
headingToolbar.frame.size.width = minimumWidth
assertLayout(headingToolbar, withHeadings: true)
var committed: [String] = []
headingToolbar.onCommand = { committed.append($0) }
for name in [NSAppearance.Name.aqua, .darkAqua] {
    headingToolbar.appearance = NSAppearance(named: name)
    for button in buttons(in: headingToolbar) {
        let frame = button.frame
        for scale: CGFloat in [1, 2] {
            for state: NSControl.StateValue in [.off, .on] {
                button.state = state
                button.mouseEntered(with: hoverEvent)
                let hovered = renderButton(button, scale: scale)
                if button.isEnabled {
                    var foregroundCoverage: CGFloat = 0
                    let background = hovered.colorAt(x: Int(9 * scale), y: Int(2 * scale))!
                    for y in Int(3 * scale)..<Int(15 * scale) {
                        for x in Int(3 * scale)..<Int(15 * scale) {
                            let color = hovered.colorAt(x: x, y: y)!
                            // Integrate antialiased foreground coverage instead of
                            // counting opaque pixels, which small vector glyphs may
                            // not contain at 1x. Compare against this appearance's
                            // actual accent fill rather than assuming blue RGB values.
                            let contrast = max(color.redComponent - background.redComponent,
                                color.greenComponent - background.greenComponent,
                                color.blueComponent - background.blueComponent, 0)
                            foregroundCoverage += contrast * color.alphaComponent
                        }
                    }
                    let minimumCoverage = scale * scale
                    if foregroundCoverage <= minimumCoverage, let renderDirectory = ProcessInfo.processInfo.environment["MARKLEAF_TOOLBAR_RENDER_DIR"] {
                        let url = URL(fileURLWithPath: renderDirectory).appendingPathComponent("failed-hover-\(button.identifier!.rawValue)-\(name.rawValue).png")
                        try hovered.representation(using: .png, properties: [:])!.write(to: url)
                    }
                    precondition(foregroundCoverage > minimumCoverage, "\(button.identifier!.rawValue), \(name.rawValue), \(scale)x, \(state.rawValue): compact symbols must retain visible antialiased white hover artwork")
                }
                assertLayout(headingToolbar, withHeadings: true)
                button.mouseExited(with: hoverEvent)
                _ = renderButton(button, scale: scale)
                assertLayout(headingToolbar, withHeadings: true)
                precondition(button.frame == frame && !button.isBordered, "appearance, state and display scale must not change layout")
            }
        }
        button.highlight(true)
        assertLayout(headingToolbar, withHeadings: true)
        button.highlight(false)
        if button.isEnabled {
            button.performClick(nil)
            precondition(committed.last == button.identifier?.rawValue, "the normalized native button must still dispatch its original command")
        }
    }
}

// Optional rendering evidence uses the actual production button drawing, not a mock.
if let renderDirectory = ProcessInfo.processInfo.environment["MARKLEAF_TOOLBAR_RENDER_DIR"] {
    for name in [NSAppearance.Name.aqua, .darkAqua] {
        headingToolbar.appearance = NSAppearance(named: name)
        for (index, button) in buttons(in: headingToolbar).enumerated() {
            button.state = .off
            let bitmap = renderButton(button, scale: 2)
            let url = URL(fileURLWithPath: renderDirectory)
                .appendingPathComponent("\(name.rawValue)-\(index)-\(button.identifier!.rawValue).png")
            try bitmap.representation(using: .png, properties: [:])!.write(to: url)
        }
    }
}
print("PASS: uniform native button/icon geometry, spacing, heading groups, light/dark 1x/2x rendering, hover preview and commands")
