import AppKit
import ObjectiveC.runtime

/// A compact overlay knob shared by document-adjacent AppKit scroll views.
/// It deliberately avoids the legacy full-height scroller track.
final class CompactOverlayScroller: NSScroller {
    static let overlayWidth: CGFloat = 6

    override class var isCompatibleWithOverlayScrollers: Bool { true }

    override class func scrollerWidth(
        for controlSize: NSControl.ControlSize,
        scrollerStyle: NSScroller.Style
    ) -> CGFloat {
        scrollerStyle == .overlay
            ? Self.overlayWidth
            : super.scrollerWidth(for: controlSize, scrollerStyle: scrollerStyle)
    }

    override func drawKnobSlot(in slotRect: NSRect, highlight flag: Bool) {}

    override func drawKnob() {
        let knobRect = rect(for: .knob)
        guard knobRect.width > 0, knobRect.height > 0, knobProportion < 1 else { return }
        let thumbRect = NSRect(
            x: knobRect.maxX - 5,
            y: knobRect.minY + 1,
            width: 5,
            height: max(12, knobRect.height - 2)
        )
        NSColor.secondaryLabelColor.withAlphaComponent(0.45).setFill()
        NSBezierPath(roundedRect: thumbRect, xRadius: 2.5, yRadius: 2.5).fill()
    }
}

enum CompactOverlayScrollView {
    static func configure(_ scrollView: NSScrollView) {
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        scrollView.scrollerStyle = .overlay
        scrollView.verticalScroller = CompactOverlayScroller()
        // 自定义 NSScroller 不参与系统的 overlay 空闲淡出；autohidesScrollers
        // 也只在内容不足一屏时隐藏。接一个空闲淡出控制器，让“滚动时可见、
        // 空闲后隐藏”跟随宿主的「自动隐藏滚动条」偏好（对齐编辑器行为）。
        let fader = ScrollKnobFadeController(scrollView: scrollView)
        objc_setAssociatedObject(scrollView, &FadeControllerKey, fader, .OBJC_ASSOCIATION_RETAIN_NONATOMIC)
        fader.autoHide = SettingsService.shared.settings.autoHideScrollbars
    }

    private static var FadeControllerKey: UInt8 = 0

    /// 偏好变更时由宿主调用；返回控制器便于解绑（一般随 scrollView 释放）。
    @discardableResult
    static func setAutoHide(_ enabled: Bool, for scrollView: NSScrollView) -> Bool {
        guard let fader = objc_getAssociatedObject(scrollView, &FadeControllerKey) as? ScrollKnobFadeController else {
            return false
        }
        fader.autoHide = enabled
        return true
    }
}

/// 滚动时显示滑块、空闲后淡出的控制器（AppKit 原生 overlay 淡出不适用于
/// 自定义 NSScroller 子类）。
// NSTrackingArea 的 owner 必须经 ObjC 消息派发响应 mouseEntered/Exited，
// 因此继承 NSObject 并显式暴露这两个选择器；纯 Swift 类会让 AppKit 走
// 消息转发并以 doesNotRecognizeSelector 崩溃（闪退）。
final class ScrollKnobFadeController: NSObject {
    private weak var scrollView: NSScrollView?
    private var observers: [Any] = []
    private var hideItem: DispatchWorkItem?
    private var mouseInside = false
    private let idleInterval: TimeInterval = 0.9

    var autoHide = true {
        didSet {
            guard autoHide != oldValue else { return }
            if autoHide {
                scheduleHide()
            } else {
                setHidden(false, animated: false)
            }
        }
    }

    init(scrollView: NSScrollView) {
        super.init()
        self.scrollView = scrollView
        let clip = scrollView.contentView
        clip.postsBoundsChangedNotifications = true
        observers.append(NotificationCenter.default.addObserver(
            forName: NSView.boundsDidChangeNotification, object: clip, queue: .main
        ) { [weak self] _ in
            self?.noteScrollActivity()
        })
        observers.append(NotificationCenter.default.addObserver(
            forName: NSView.frameDidChangeNotification, object: clip, queue: .main
        ) { [weak self] _ in
            self?.noteScrollActivity()
        })
        let tracking = NSTrackingArea(
            rect: scrollView.bounds,
            options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect],
            owner: self, userInfo: nil)
        scrollView.addTrackingArea(tracking)
        setHidden(autoHide, animated: false)
    }

    // MARK: NSTrackingArea owner 回调：鼠标在滚动区域内时不淡出。

    @objc private func mouseEntered(with event: NSEvent) {
        mouseInside = true
        if autoHide { setHidden(false, animated: true) }
    }

    @objc private func mouseExited(with event: NSEvent) {
        mouseInside = false
        if autoHide { scheduleHide() }
    }

    private func noteScrollActivity() {
        guard autoHide else { return }
        // 内容不足一屏时无需浮现（autohidesScrollers 已处理可见性）。
        if let clip = scrollView?.contentView,
           clip.documentView?.frame.height ?? 0 <= clip.bounds.height { return }
        setHidden(false, animated: false)
        scheduleHide()
    }

    private func scheduleHide() {
        hideItem?.cancel()
        let item = DispatchWorkItem { [weak self] in
            guard let self, self.autoHide, !self.mouseInside else { return }
            self.setHidden(true, animated: true)
        }
        hideItem = item
        DispatchQueue.main.asyncAfter(deadline: .now() + idleInterval, execute: item)
    }

    private func setHidden(_ hidden: Bool, animated: Bool) {
        guard let scroller = scrollView?.verticalScroller else { return }
        let alpha: CGFloat = hidden ? 0 : 1
        if animated {
            NSAnimationContext.runAnimationGroup { context in
                context.duration = 0.25
                scroller.animator().alphaValue = alpha
            }
        } else {
            scroller.alphaValue = alpha
        }
    }
}
