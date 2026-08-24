import AppKit

private var failures = 0

private func expect(_ condition: @autoclosure () -> Bool, _ message: String) {
    guard condition() else {
        failures += 1
        fputs("FAIL: \(message)\n", stderr)
        return
    }
}

let compact = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 420, height: 500)
)
expect(compact.width == PreferencesWindowLayout.minimumWindowWidth,
       "preference window should use a compact width floor")
expect(compact.height <= PreferencesWindowLayout.maximumWindowHeight,
       "preference window height should stay within the compact ceiling")

let wide = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 720, height: 700)
)
expect(wide.width == PreferencesWindowLayout.minimumWindowWidth,
       "simplified Chinese width must stay compact instead of feeding stretched tab width back into sizing")
expect(wide.height == PreferencesWindowLayout.maximumWindowHeight,
       "preference window should cap unusually tall tab content")

let column = PreferencesWindowLayout.centeredColumnFrame(
    containerWidth: compact.width,
    fittingWidth: 320
)
let expectedMargin = (compact.width - column.width) / 2
expect(abs(column.minX - expectedMargin) < 0.5,
       "settings content column should be centered in the window")
expect(column.width <= PreferencesWindowLayout.maximumContentColumnWidth,
       "settings content column should have a readable maximum width")

expect(PreferencesWindowLayout.bottomBarTopInset >= 10,
       "bottom action bar should keep enough top breathing room")
expect(PreferencesWindowLayout.bottomBarBottomInset == 12,
       "bottom action bar should use a balanced bottom inset")
expect(PreferencesWindowLayout.fieldLabelColumnWidth > 0,
       "field label column should reserve a readable width for right-aligned labels")
expect(PreferencesWindowLayout.fieldLabelColumnWidth >= 120,
       "field label column should stay wide enough for common Chinese labels")

let zh = PreferencesWindowLayout.metrics(for: "zh-Hans")
let zhHant = PreferencesWindowLayout.metrics(for: "zh-Hant")
let en = PreferencesWindowLayout.metrics(for: "en")
let ja = PreferencesWindowLayout.metrics(for: "ja")
expect(zh.formContentColumnWidth == 400,
       "simplified Chinese should preserve the current content column width")
expect(zh.fieldLabelColumnWidth == 120,
       "simplified Chinese should preserve the current label column width")
expect(en.fieldLabelColumnWidth > zh.fieldLabelColumnWidth,
       "English should reserve extra width for translated field labels")
expect(ja.fieldLabelColumnWidth > zh.fieldLabelColumnWidth,
       "Japanese should reserve extra width for translated field labels")
expect(en.formContentColumnWidth > zh.formContentColumnWidth,
       "English should use an independent wider content column")
expect(ja.formContentColumnWidth > zh.formContentColumnWidth,
       "Japanese should use an independent wider content column")
expect(en.minimumWindowWidth > zh.minimumWindowWidth,
       "English should use an independent wider preference window")
expect(ja.minimumWindowWidth > zh.minimumWindowWidth,
       "Japanese should use an independent wider preference window")
expect(zh.minimumWindowWidth == 500 && zh.maximumWindowWidth == 500,
       "simplified Chinese should keep the previously approved compact 500-point width")
expect(zhHant.minimumWindowWidth == 520 && zhHant.maximumWindowWidth == 520,
       "traditional Chinese should use its own stable 520-point width")
expect(en.minimumWindowWidth == 560 && en.maximumWindowWidth == 560,
       "English should use its own stable 560-point width")
expect(ja.minimumWindowWidth == 620 && ja.maximumWindowWidth == 620,
       "Japanese should use its own stable 620-point width")
expect(zhHant != zh,
       "traditional Chinese must not silently reuse simplified Chinese layout metrics")
expect(PreferencesWindowLayout.metrics(for: "unknown") == zh,
       "unknown languages should fall back to the simplified Chinese layout")
let englishColumn = PreferencesWindowLayout.centeredColumnFrame(
    containerWidth: en.maximumWindowWidth,
    fittingWidth: en.maximumContentColumnWidth + 100,
    metrics: en
)
expect(englishColumn.width == en.maximumContentColumnWidth,
       "language-specific centered columns should use that language's width cap")

let englishLongLabel = ("Default Encoding for New Files" as NSString).size(
    withAttributes: [.font: NSFont.systemFont(ofSize: 13)]
).width
let japaneseLongLabel = ("新規ファイルの既定のエンコーディング" as NSString).size(
    withAttributes: [.font: NSFont.systemFont(ofSize: 13)]
).width
expect(en.fieldLabelColumnWidth >= ceil(englishLongLabel),
       "English label column should fit the longest current field label")
expect(ja.fieldLabelColumnWidth >= ceil(japaneseLongLabel),
       "Japanese label column should fit the longest current field label")

let englishGeneralLabelWidth = PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [59],
    metrics: en,
    mode: .pageContent
)
let englishImagesLabelWidth = PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [53, 53],
    metrics: en,
    mode: .pageContent
)
let japaneseImagesLabelWidth = PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [52, 52],
    metrics: ja,
    mode: .pageContent
)
expect(englishGeneralLabelWidth == 59,
       "English General should size its centered field group from the actual short label")
expect(englishImagesLabelWidth == 53,
       "English Images should size its centered field group from the actual short labels")
expect(japaneseImagesLabelWidth == 52,
       "Japanese Images should size its centered field group from the actual short labels")
let japaneseImagesRowWidth = PreferencesWindowLayout.centeredFieldRowWidth(
    labelColumnWidth: japaneseImagesLabelWidth,
    maximumControlWidth: 218,
    availableWidth: ja.formContentColumnWidth - 56
)
expect(japaneseImagesRowWidth == 282,
       "Japanese Images field rows should contain only the label, spacing, and widest popup before centering")

let englishLongPageLabelWidth = PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [74, 188, 123],
    metrics: en,
    mode: .languageMaximum
)
let japaneseLongPageLabelWidth = PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [96, 226, 140],
    metrics: ja,
    mode: .languageMaximum
)
expect(englishLongPageLabelWidth == en.fieldLabelColumnWidth,
       "English File, Editor, and Appearance pages must retain their original centered label column")
expect(japaneseLongPageLabelWidth == ja.fieldLabelColumnWidth,
       "Japanese File, Editor, and Appearance pages must retain their original centered label column")
expect(PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [400],
    metrics: en,
    mode: .pageContent
) == en.fieldLabelColumnWidth,
       "unexpectedly long English labels must stay capped by the language layout width")
expect(PreferencesWindowLayout.resolvedFieldLabelColumnWidth(
    fittingWidths: [],
    metrics: zh,
    mode: .pageContent
) == 0,
       "pages without labeled fields should not add an invisible label column")

let enNarrowPage = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 320, height: 430), metrics: en
)
let enWidePage = PreferencesWindowLayout.windowContentSize(
    for: NSSize(width: 900, height: 580), metrics: en
)
expect(enNarrowPage.width == enWidePage.width,
       "switching English preference tabs may change height but must preserve width")

if failures > 0 {
    exit(1)
}
print("PASS")
