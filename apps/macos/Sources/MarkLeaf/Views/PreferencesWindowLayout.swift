import AppKit

/// 偏好设置窗口的尺寸与内容列布局策略。
/// 将窗口的紧凑边界与页面内容的居中规则集中管理，便于回归测试。
enum PreferencesWindowLayout {
    enum FieldLabelColumnMode {
        case languageMaximum
        case pageContent
    }

    struct Metrics: Equatable {
        let minimumWindowWidth: CGFloat
        let maximumWindowWidth: CGFloat
        let minimumWindowHeight: CGFloat
        let maximumWindowHeight: CGFloat
        let maximumContentColumnWidth: CGFloat
        let formContentColumnWidth: CGFloat
        let contentHorizontalOffset: CGFloat
        let contentColumnMinimumMargin: CGFloat
        let hintLeadingInset: CGFloat
        let fieldLabelColumnWidth: CGFloat
        let bottomBarTopInset: CGFloat
        let bottomBarBottomInset: CGFloat
    }

    /// 各语言使用稳定的独立宽度。标签页切换只改变高度，不参与宽度计算。
    static let simplifiedChinese = Metrics(
        minimumWindowWidth: 500,
        maximumWindowWidth: 500,
        minimumWindowHeight: 420,
        maximumWindowHeight: 620,
        maximumContentColumnWidth: 452,
        formContentColumnWidth: 400,
        contentHorizontalOffset: 0,
        contentColumnMinimumMargin: 24,
        hintLeadingInset: 0,
        fieldLabelColumnWidth: 120,
        bottomBarTopInset: 12,
        bottomBarBottomInset: 12
    )

    static let traditionalChinese = Metrics(
        minimumWindowWidth: 520,
        maximumWindowWidth: 520,
        minimumWindowHeight: 420,
        maximumWindowHeight: 620,
        maximumContentColumnWidth: 472,
        formContentColumnWidth: 420,
        contentHorizontalOffset: 0,
        contentColumnMinimumMargin: 24,
        hintLeadingInset: 0,
        fieldLabelColumnWidth: 140,
        bottomBarTopInset: 12,
        bottomBarBottomInset: 12
    )

    static let english = Metrics(
        minimumWindowWidth: 560,
        maximumWindowWidth: 560,
        minimumWindowHeight: 420,
        maximumWindowHeight: 620,
        maximumContentColumnWidth: 512,
        formContentColumnWidth: 500,
        contentHorizontalOffset: 0,
        contentColumnMinimumMargin: 24,
        hintLeadingInset: 0,
        fieldLabelColumnWidth: 196,
        bottomBarTopInset: 12,
        bottomBarBottomInset: 12
    )

    static let japanese = Metrics(
        minimumWindowWidth: 620,
        maximumWindowWidth: 620,
        minimumWindowHeight: 420,
        maximumWindowHeight: 620,
        maximumContentColumnWidth: 572,
        formContentColumnWidth: 560,
        contentHorizontalOffset: 0,
        contentColumnMinimumMargin: 24,
        hintLeadingInset: 0,
        fieldLabelColumnWidth: 235,
        bottomBarTopInset: 12,
        bottomBarBottomInset: 12
    )

    static func metrics(for language: String) -> Metrics {
        switch language {
        case "zh-Hant": return traditionalChinese
        case "en": return english
        case "ja": return japanese
        default: return simplifiedChinese
        }
    }

    // 中文默认值兼容现有测试与调用方；窗口控制器使用 metrics(for:) 的语言配置。
    static let minimumWindowWidth = simplifiedChinese.minimumWindowWidth
    static let maximumWindowWidth = simplifiedChinese.maximumWindowWidth
    static let minimumWindowHeight = simplifiedChinese.minimumWindowHeight
    static let maximumWindowHeight = simplifiedChinese.maximumWindowHeight
    static let maximumContentColumnWidth = simplifiedChinese.maximumContentColumnWidth
    static let formContentColumnWidth = simplifiedChinese.formContentColumnWidth
    static let contentHorizontalOffset = simplifiedChinese.contentHorizontalOffset
    static let contentColumnMinimumMargin = simplifiedChinese.contentColumnMinimumMargin
    static let hintLeadingInset = simplifiedChinese.hintLeadingInset
    static let fieldLabelColumnWidth = simplifiedChinese.fieldLabelColumnWidth
    static let bottomBarTopInset = simplifiedChinese.bottomBarTopInset
    static let bottomBarBottomInset = simplifiedChinese.bottomBarBottomInset

    static let fieldRowSpacing: CGFloat = 12

    static func resolvedFieldLabelColumnWidth(
        fittingWidths: [CGFloat],
        metrics: Metrics,
        mode: FieldLabelColumnMode
    ) -> CGFloat {
        guard mode == .pageContent else {
            return metrics.fieldLabelColumnWidth
        }
        let widestLabel = ceil(fittingWidths.max() ?? 0)
        return min(metrics.fieldLabelColumnWidth, widestLabel)
    }

    static func centeredFieldRowWidth(
        labelColumnWidth: CGFloat,
        maximumControlWidth: CGFloat,
        availableWidth: CGFloat
    ) -> CGFloat {
        min(
            max(0, availableWidth),
            max(0, labelColumnWidth) + fieldRowSpacing + max(0, maximumControlWidth)
        )
    }

    static func windowContentSize(for fittingSize: NSSize) -> NSSize {
        windowContentSize(for: fittingSize, metrics: simplifiedChinese)
    }

    static func windowContentSize(for fittingSize: NSSize, metrics: Metrics) -> NSSize {
        let width = metrics.minimumWindowWidth
        let height = min(
            metrics.maximumWindowHeight,
            max(metrics.minimumWindowHeight, ceil(fittingSize.height) + 24)
        )
        return NSSize(width: width, height: height)
    }

    static func centeredColumnFrame(containerWidth: CGFloat, fittingWidth: CGFloat) -> NSRect {
        centeredColumnFrame(
            containerWidth: containerWidth,
            fittingWidth: fittingWidth,
            metrics: simplifiedChinese
        )
    }

    static func centeredColumnFrame(
        containerWidth: CGFloat,
        fittingWidth: CGFloat,
        metrics: Metrics
    ) -> NSRect {
        let availableWidth = max(0, containerWidth - 2 * metrics.contentColumnMinimumMargin)
        let width = min(max(0, fittingWidth), min(metrics.maximumContentColumnWidth, availableWidth))
        return NSRect(
            x: (containerWidth - width) / 2,
            y: 0,
            width: width,
            height: 0
        )
    }
}
