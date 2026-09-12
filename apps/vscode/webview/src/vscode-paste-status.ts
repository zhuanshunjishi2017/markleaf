import type { pasteClipboardContentWithResult } from '@markleaf/editor-core'
import { normalizeSharedEditorLanguage } from '@markleaf/editor-core'

// Match MainForm.SetPasteStatus and the Windows locale messages.
const messages: Record<string, Record<string, string>> = {
  'zh-Hans': {
    'pastedFormatted': '已粘贴格式化内容',
    'pastedPlainText': '已粘贴纯文本',
    'pastedMarkdown': '已粘贴 Markdown',
    'pastedMarkdownNormalized': '已粘贴 Markdown，并转换了不兼容的格式',
    'pastedPlainTextFallback': 'Markdown 格式不兼容，已作为纯文本粘贴',
    'pastedPlainTextFallbackReason': '已作为纯文本粘贴：{0}',
    'pasteFailed': '无法粘贴剪贴板内容'
  },
  'zh-Hant': {
    'pastedFormatted': '已貼上格式化內容',
    'pastedPlainText': '已貼上純文字',
    'pastedMarkdown': '已貼上 Markdown',
    'pastedMarkdownNormalized': '已貼上 Markdown，並轉換了不相容的格式',
    'pastedPlainTextFallback': 'Markdown 格式不相容，已作為純文字貼上',
    'pastedPlainTextFallbackReason': '已作為純文字貼上：{0}',
    'pasteFailed': '無法貼上剪貼簿內容'
  },
  'en': {
    'pastedFormatted': 'Formatted content pasted',
    'pastedPlainText': 'Plain text pasted',
    'pastedMarkdown': 'Markdown pasted',
    'pastedMarkdownNormalized': 'Markdown pasted with incompatible formatting converted',
    'pastedPlainTextFallback': 'Incompatible Markdown pasted as plain text',
    'pastedPlainTextFallbackReason': 'Pasted as plain text: {0}',
    'pasteFailed': 'Could not paste clipboard content'
  },
  'ja': {
    'pastedFormatted': '書式付きで貼り付け',
    'pastedPlainText': 'プレーンテキストで貼り付け',
    'pastedMarkdown': 'Markdown を貼り付けました',
    'pastedMarkdownNormalized': '互換性のない書式を変換して Markdown を貼り付けました',
    'pastedPlainTextFallback': '互換性のない Markdown をプレーンテキストとして貼り付けました',
    'pastedPlainTextFallbackReason': 'プレーンテキストとして貼り付けました：{0}',
    'pasteFailed': 'クリップボードの内容を貼り付けられませんでした'
  }
}

export function vscodePasteStatus(result: ReturnType<typeof pasteClipboardContentWithResult>, language: string): string {
  const labels = messages[normalizeSharedEditorLanguage(language)]!
  if (!result.success) return labels.pasteFailed!
  switch (result.outcome) {
    case 'markdown': return labels.pastedMarkdown!
    case 'normalized': return labels.pastedMarkdownNormalized!
    case 'formatted': return labels.pastedFormatted!
    case 'plainText': return result.error?.trim()
      ? labels.pastedPlainTextFallbackReason!.replace('{0}', () => result.error!)
      : labels.pastedPlainTextFallback!
    default: return labels.pastedPlainText!
  }
}
