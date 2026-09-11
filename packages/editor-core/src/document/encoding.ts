export type NativeCodec = {
  decode(bytes: number[], codec: string): string | null
  encode(text: string, codec: string): number[] | null
}

export class DocumentKernelError extends Error {
  constructor(public readonly code: string, message: string) { super(message) }
}

export const encodings = [
  { id: 'utf-8', label: 'UTF-8', codec: 'utf-8', codePage: 65001, bom: [] },
  { id: 'utf-8-bom', label: 'UTF-8 with BOM', codec: 'utf-8', codePage: 65001, bom: [239, 187, 191] },
  { id: 'utf-16', label: 'UTF-16', codec: 'utf-16le', codePage: 1200, bom: [] },
  { id: 'utf-16-bom', label: 'UTF-16 with BOM', codec: 'utf-16le', codePage: 1200, bom: [255, 254] },
  { id: 'utf-16be', label: 'UTF-16 BE', codec: 'utf-16be', codePage: 1201, bom: [] },
  { id: 'utf-16be-bom', label: 'UTF-16 BE with BOM', codec: 'utf-16be', codePage: 1201, bom: [254, 255] },
  { id: 'gb18030', label: 'GB18030', codec: 'gb18030', codePage: 54936, bom: [] },
  { id: 'gbk', label: 'GBK', codec: 'gbk', codePage: 936, bom: [] },
  { id: 'gb2312', label: 'GB2312', codec: 'gb2312', codePage: 20936, bom: [] },
  { id: 'big5', label: 'Big5', codec: 'big5', codePage: 950, bom: [] },
  { id: 'shift_jis', label: 'Shift_JIS', codec: 'shift_jis', codePage: 932, bom: [] },
  { id: 'us-ascii', label: 'US-ASCII', codec: 'us-ascii', codePage: 20127, bom: [] },
]
export type Encoding = typeof encodings[number]
const starts = (bytes: number[], prefix: number[]) => prefix.every((value, index) => bytes[index] === value)
const equal = (left: number[], right: number[]) => left.length === right.length && left.every((value, index) => value === right[index])

export function resolveEncoding(value?: string): Encoding {
  const name = value?.toLowerCase()
  if (name === 'utf-8 without bom') return encodings[0]!
  if (name === 'utf-16 without bom') return encodings[2]!
  // Only settings resolve a missing/unrecognized value to their established default.
  return encodings.find(entry => entry.id === name || entry.label.toLowerCase() === name) ?? encodings[0]!
}

function requireEncoding(value: string): Encoding {
  const encoding = encodings.find(entry => entry.id === value || entry.label === value)
  if (!encoding) throw new DocumentKernelError('unsupported_encoding', `Unsupported document encoding: ${value}`)
  return encoding
}

function body(bytes: number[], encoding: Encoding) {
  if (encoding.codec === 'utf-8' && starts(bytes, [239, 187, 191])) return bytes.slice(3)
  if (encoding.codec === 'utf-16le' && starts(bytes, [255, 254])) return bytes.slice(2)
  if (encoding.codec === 'utf-16be' && starts(bytes, [254, 255])) return bytes.slice(2)
  return bytes
}

export function decodeDocument(bytes: number[], id: string, codec: NativeCodec): string {
  const encoding = requireEncoding(id)
  const text = codec.decode(body(bytes, encoding), encoding.codec)
  if (text === null) throw new DocumentKernelError('invalid_encoding', `The document cannot be decoded as ${encoding.label}`)
  return text
}

export function encodeDocument(text: string, id: string, codec: NativeCodec): number[] {
  const encoding = requireEncoding(id)
  const bytes = codec.encode(text, encoding.codec)
  if (bytes === null) throw new DocumentKernelError('unrepresentable_text', `The document cannot be represented as ${encoding.label}`)
  return encoding.bom.concat(bytes)
}

export function reloadWouldLoseData(bytes: number[], id: string, codec: NativeCodec): boolean {
  try {
    const encoding = requireEncoding(id)
    const text = decodeDocument(bytes, id, codec)
    return !equal(body(bytes, encoding), body(encodeDocument(text, id, codec), encoding))
  } catch (error) {
    if (error instanceof DocumentKernelError) return true
    throw error
  }
}

function plausibility(text: string, id: string) {
  let score = 0, cjk = 0, kana = 0
  for (const character of text) {
    const value = character.codePointAt(0)!
    if (value === 0 || value >= 1 && value <= 8 || value >= 11 && value <= 12 || value >= 14 && value <= 31) { score -= 24; continue }
    if (value >= 0x3400 && value <= 0x4dbf || value >= 0x4e00 && value <= 0x9fff) { cjk++; score += 3 }
    else if (value >= 0x3040 && value <= 0x30ff || value >= 0xff65 && value <= 0xff9f) { kana++; score += 2 }
    else score++
  }
  if (id === 'shift_jis') { if (kana) score += 18; if (cjk > kana) score -= cjk * 8 }
  else if (cjk) score += cjk * 8 + 6
  return score
}

export function detectEncoding(bytes: number[], codec: NativeCodec): Encoding {
  for (const id of ['utf-8-bom', 'utf-16-bom', 'utf-16be-bom']) {
    const encoding = requireEncoding(id)
    if (starts(bytes, encoding.bom)) { decodeDocument(bytes, id, codec); return encoding }
  }
  if (bytes.length >= 4 && bytes.length % 2 === 0) {
    let even = 0, odd = 0
    bytes.forEach((value, index) => { if (value === 0) { if (index % 2) odd++; else even++ } })
    const candidate = odd >= even ? 'utf-16' : 'utf-16be'
    if (Math.max(even, odd) >= Math.max(1, Math.floor(bytes.length / 8)) && !reloadWouldLoseData(bytes, candidate, codec)) return requireEncoding(candidate)
  }
  if (!reloadWouldLoseData(bytes, 'utf-8', codec)) return requireEncoding('utf-8')
  let best: { encoding: Encoding; score: number } | undefined
  // Stable tie order; platform codecs supply conversion, not detection policy.
  for (const id of ['gbk', 'gb2312', 'gb18030', 'big5', 'shift_jis']) {
    if (reloadWouldLoseData(bytes, id, codec)) continue
    const score = plausibility(decodeDocument(bytes, id, codec), id)
    if (!best || score > best.score) best = { encoding: requireEncoding(id), score }
  }
  if (!best) throw new DocumentKernelError('unknown_encoding', 'No supported encoding can decode the document without loss')
  return best.encoding
}

export type NewLine = 'LF' | 'CRLF' | 'CR' | 'Mixed'
export function detectNewLine(text: string): NewLine {
  const kinds = new Set(text.match(/\r\n|\r|\n/g) ?? [])
  if (kinds.size > 1) return 'Mixed'
  return kinds.has('\r\n') ? 'CRLF' : kinds.has('\r') ? 'CR' : 'LF'
}
export function normalizeNewLines(text: string, style: string): string {
  if (style.toLowerCase() === 'mixed') return text
  const value = style === '\r\n' || style.toUpperCase() === 'CRLF' ? '\r\n' : style === '\r' || style.toUpperCase() === 'CR' ? '\r' : '\n'
  return text.replace(/\r\n|\r|\n/g, value)
}

/** Text-document hosts supply their authoritative EOL and final-newline facts. */
export function preserveDocumentLineEndings(text: string, original: string, eol: string): string {
  let normalized = normalizeNewLines(text, 'LF')
  if (original.endsWith('\n') && !normalized.endsWith('\n')) normalized += '\n'
  return normalizeNewLines(normalized, eol)
}

export function readDocument(bytes: number[], codec: NativeCodec, encoding?: string) {
  const selected = encoding ? requireEncoding(encoding) : detectEncoding(bytes, codec)
  const text = decodeDocument(bytes, selected.id, codec)
  return { text, encoding: selected, newLine: detectNewLine(text) }
}

export const previewReadLimit = 2048
export function readPreview(bytes: number[], truncated: boolean, codec: NativeCodec): string {
  if (!truncated) return readDocument(bytes, codec).text
  // Only a bounded file prefix may end inside a multi-byte character. Complete files never trim invalid bytes.
  const bom = encodings.find(entry => entry.bom.length > 0 && starts(bytes, entry.bom))
  for (const id of [bom?.id ?? 'utf-8']) {
    for (let trim = 0; trim <= Math.min(3, bytes.length); trim++) {
      const prefix = bytes.slice(0, bytes.length - trim)
      if (!reloadWouldLoseData(prefix, id, codec)) return decodeDocument(prefix, id, codec)
    }
  }
  for (let trim = 0; trim <= Math.min(3, bytes.length); trim++) {
    try { return readDocument(bytes.slice(0, bytes.length - trim), codec).text }
    catch (error) { if (!(error instanceof DocumentKernelError)) throw error }
  }
  throw new DocumentKernelError('unknown_encoding', 'The document preview cannot be decoded without loss')
}
