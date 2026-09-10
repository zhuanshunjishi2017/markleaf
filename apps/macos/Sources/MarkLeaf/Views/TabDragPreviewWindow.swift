import AppKit

/// 跨窗口拖拽标签时跟随光标的浮层：只负责显示标签快照，不接收鼠标事件。
/// 参照浏览器做法——窗口内仍用被抬起的标签本身，离开窗口后由这个浮层接管视觉。
final class TabDragPreviewWindow: NSWindow {
    init(image: NSImage) {
        let size = image.size.width > 0 && image.size.height > 0
            ? image.size
            : NSSize(width: 160, height: TabBarController.preferredHeight)
        super.init(
            contentRect: NSRect(origin: .zero, size: size),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        level = .floating
        ignoresMouseEvents = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        isReleasedWhenClosed = false
        let imageView = NSImageView(frame: NSRect(origin: .zero, size: size))
        imageView.image = image
        imageView.imageScaling = .scaleProportionallyUpOrDown
        imageView.alphaValue = 0.92
        contentView = imageView
    }
}
