import * as encoding from './encoding'
import * as projection from './projection'
import * as transactions from './transactions'

declare global { var __markleafDocumentCodec: (request: string) => string }
const codec: encoding.NativeCodec = {
  decode: (bytes, name) => JSON.parse(globalThis.__markleafDocumentCodec(JSON.stringify({ operation: 'decode', codec: name, bytes }))),
  encode: (text, name) => JSON.parse(globalThis.__markleafDocumentCodec(JSON.stringify({ operation: 'encode', codec: name, text }))),
}

// A single JSON boundary for synchronous native runtimes. Native code supplies only OS codec primitives.
export function invoke(request: string): string {
  try {
    const { method, payload: p = {} } = JSON.parse(request)
    let value: unknown
    switch (method) {
      case 'encodings': value = encoding.encodings; break
      case 'resolveEncoding': value = encoding.resolveEncoding(p.value); break
      case 'detectEncoding': value = encoding.detectEncoding(p.bytes, codec); break
      case 'decode': value = encoding.decodeDocument(p.bytes, p.encoding, codec); break
      case 'encode': value = encoding.encodeDocument(p.text, p.encoding, codec); break
      case 'reloadWouldLoseData': value = encoding.reloadWouldLoseData(p.bytes, p.encoding, codec); break
      case 'read': value = encoding.readDocument(p.bytes, codec, p.encoding); break
      case 'previewReadLimit': value = encoding.previewReadLimit; break
      case 'preview': value = projection.documentPlainText(encoding.readPreview(p.bytes, p.truncated, codec), p.isMarkdown); break
      case 'newLine': value = encoding.detectNewLine(p.text); break
      case 'normalizeNewLines': value = encoding.normalizeNewLines(p.text, p.style); break
      case 'preserveDocumentLineEndings': value = encoding.preserveDocumentLineEndings(p.text, p.original, p.eol); break
      case 'plainText': value = projection.documentPlainText(p.text, p.isMarkdown); break
      case 'snippet': value = projection.documentSnippet(p.text, p.query); break
      case 'match': value = projection.matchDocument(p.name, p.text, p.isMarkdown, p.query); break
      case 'validateSave': transactions.validateSave(p); value = true; break
      case 'prepareSave': value = transactions.prepareSave(p.text, p.encoding, p.newLine, codec); break
      case 'dirtyAfterSave': value = transactions.dirtyAfterSave(p.savedRevision, p.currentRevision); break
      case 'encodingChange': value = transactions.encodingChange(p.current, p.target, p.hasFile, p.readOnly); break
      case 'serializeRecovery': value = transactions.serializeRecovery(p); break
      case 'parseRecovery': value = transactions.parseRecovery(p.content, p.legacyMarkdown); break
      case 'recoveryFileName': value = transactions.recoveryFileName(p.owner, p.documentId); break
      case 'ownsRecoveryFile': value = transactions.ownsRecoveryFile(p.name, p.documentId, p.owner); break
      case 'selectRecoveries': value = transactions.selectRecoveries(p.records); break
      default: throw new encoding.DocumentKernelError('unknown_method', `Unknown document kernel method: ${method}`)
    }
    return JSON.stringify({ ok: true, value })
  } catch (error) {
    return JSON.stringify({ ok: false, error: { code: error instanceof encoding.DocumentKernelError ? error.code : 'document_error', message: error instanceof Error ? error.message : String(error) } })
  }
}
