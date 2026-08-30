import AppKit

/// 活动标签编辑器宿主：同时挂载所有已创建编辑器视图，
/// 切换标签只做显隐（可选 ≤100ms 淡化），不销毁或重建 WKWebView。
final class EditorHostView: NSView {
    private var viewsByTab: [DocumentTabID: EditorWebContainerView] = [:]
    private(set) var visibleTabID: DocumentTabID?

    func attach(tabID: DocumentTabID, view: EditorWebContainerView) {
        guard viewsByTab[tabID] == nil else { return }
        viewsByTab[tabID] = view
        view.translatesAutoresizingMaskIntoConstraints = false
        view.isHidden = true
        addSubview(view)
        NSLayoutConstraint.activate([
            view.topAnchor.constraint(equalTo: topAnchor),
            view.leadingAnchor.constraint(equalTo: leadingAnchor),
            view.trailingAnchor.constraint(equalTo: trailingAnchor),
            view.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }

    func attachedView(for tabID: DocumentTabID) -> EditorWebContainerView? {
        viewsByTab[tabID]
    }

    /// 显示目标标签的编辑器。若导致 WebKit 闪烁/焦点跳动，调用方传入
    /// `animated: false` 即刻切换（设计稿 §5 的降级路径）。
    func show(tabID: DocumentTabID, animated: Bool, reduceMotion: Bool) {
        guard let target = viewsByTab[tabID] else { return }
        let outgoing = visibleTabID.flatMap { viewsByTab[$0] }
        visibleTabID = tabID

        let duration = animated && !reduceMotion
            ? TabAnimationPolicy.duration(for: .editorFade, reduceMotion: false)
            : 0
        guard duration > 0 else {
            outgoing?.isHidden = true
            target.isHidden = false
            return
        }
        target.alphaValue = 0
        target.isHidden = false
        NSAnimationContext.runAnimationGroup({ context in
            context.duration = duration
            target.animator().alphaValue = 1
        }, completionHandler: { [weak outgoing] in
            outgoing?.isHidden = true
        })
    }

    /// 关闭标签时移除其编辑器视图（唯一允许销毁的时机，另见退出与内存压力）。
    func detach(tabID: DocumentTabID) {
        guard let view = viewsByTab.removeValue(forKey: tabID) else { return }
        if visibleTabID == tabID { visibleTabID = nil }
        view.removeFromSuperview()
    }
}
