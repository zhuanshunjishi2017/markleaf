import type { ScriptClass } from './model'

const segmenter = new Intl.Segmenter(undefined, { granularity: 'grapheme' })

const cjkPattern =
  /[\u2e80-\u2eff\u3040-\u30ff\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uac00-\ud7af\u{20000}-\u{2fa1f}]/u

export function classifyCharacter(character: string): ScriptClass {
  if (/\s/u.test(character)) return 'space'
  if (cjkPattern.test(character)) return 'cjk'
  if (/[A-Za-z]/u.test(character)) return 'latin'
  if (/[0-9]/u.test(character)) return 'digit'
  if (/\p{P}/u.test(character)) return 'punctuation'
  if (/\p{S}/u.test(character)) return 'symbol'
  return 'unknown'
}

export function splitGraphemes(text: string): string[] {
  return Array.from(segmenter.segment(text), (segment) => segment.segment)
}
