import { readFileSync } from 'node:fs'
import { describe, expect, it } from 'vitest'
import { vscodePasteStatus } from '../src/vscode-paste-status'

describe('Windows clipboard status parity', () => {
  it.each([
    ['zh-Hans', 'zh-CN'], ['zh-Hant', 'zh-TW'], ['en', 'en-US'], ['ja', 'ja-JP'],
  ])('keeps %s paste outcomes and fallback reasons aligned with Windows', (language, locale) => {
    const windows = JSON.parse(readFileSync(`../../apps/windows/MarkLeaf/Resources/Locales/${locale}.json`, 'utf8').replace(/^\uFEFF/, ''))
    for (const [outcome, key] of [
      ['markdown', 'pastedMarkdown'], ['normalized', 'pastedMarkdownNormalized'],
      ['formatted', 'pastedFormatted'], ['plainText', 'pastedPlainTextFallback'],
    ] as const) {
      expect(vscodePasteStatus({ success: true, outcome }, language)).toBe(windows[`status.${key}`])
    }
    expect(vscodePasteStatus({ success: true, outcome: 'plainText', error: 'Invalid marks: $&' }, language))
      .toBe(windows['status.pastedPlainTextFallbackReason'].replace('{0}', () => 'Invalid marks: $&'))
    expect(vscodePasteStatus({ success: false, outcome: 'failed' }, language)).toBe(windows['status.pasteFailed'])
    expect(vscodePasteStatus({ success: false, outcome: 'markdown' }, language)).toBe(windows['status.pasteFailed'])
  })
})
