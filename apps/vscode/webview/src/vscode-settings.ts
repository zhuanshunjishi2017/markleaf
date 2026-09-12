// Host-independent defaults shared by configuration transport and the Webview.
export const typographyStyles = ['sans', 'serif', 'print', 'print-double', 'latex', 'retro-print', 'minimal', 'magazine', 'notebook'] as const
export const colorThemes = ['vscode', 'default-light', 'apple-blue', 'apple-dark', 'dark', 'deep-sea', 'espresso', 'forest', 'high-contrast-dark', 'high-contrast-light', 'ink', 'lavender', 'memo', 'morandi-cyan', 'morandi-dark', 'morandi', 'pure-black', 'rose', 'saltlemon', 'yellowed-page'] as const
export const defaultSettings = {
  defaultMode: 'edit' as 'edit' | 'read', fontSize: 16, maxWidth: 820, lineHeight: 1.75,
  typography: 'minimal' as typeof typographyStyles[number], colorTheme: 'vscode' as typeof colorThemes[number],
  fontFamily: '', sourceFontFamily: '', sourceFontSize: 14, cjkLanguage: 'zh-Hans',
  cjkAutoSpacing: true, ignoreMaxWidth: false, showCodeHighlight: true, showBlockHandle: true,
  focusMode: false, typewriterMode: false, showOutline: false, showStatusBar: true,
  zoom: 100, ctrlWheelZoom: true, autoHideScrollbars: true,
  autoConvertUnsafeEmphasis: true, exitBlockOnEmptyEnter: false, useShiftEnterHardBreak: true,
  codeFence: 'backtick' as 'backtick' | 'tilde', emphasisMarker: 'asterisk' as 'asterisk' | 'underscore',
  bulletMarker: 'dash' as 'dash' | 'asterisk' | 'plus', escapeLiteralSymbols: false,
  escapeMarkdownLiteralSymbols: true,
  imageDirectory: 'assets', fileImageHandling: 'copy' as 'copy' | 'reference',
  useRelativeImagePaths: true, prefixImagePathsWithDot: true, customCss: '',
}
export type MarkLeafSettings = typeof defaultSettings
