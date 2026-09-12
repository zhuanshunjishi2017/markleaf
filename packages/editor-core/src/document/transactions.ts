import { DocumentKernelError, encodeDocument, normalizeNewLines, resolveEncoding, type NativeCodec } from './encoding'

export type SaveFacts = { sameTarget: boolean; forceOverwrite: boolean; targetExists: boolean; acceptedVersion: boolean; contentChanged: boolean; readOnly: boolean }
export function validateSave(facts: SaveFacts): void {
  if (facts.readOnly) throw new DocumentKernelError('read_only', 'The target document is read-only')
  if (facts.sameTarget && !facts.forceOverwrite && (!facts.targetExists || !facts.acceptedVersion || facts.contentChanged)) {
    throw new DocumentKernelError('external_change', 'The document changed on disk; resolve the conflict before saving')
  }
}
export function prepareSave(text: string, encoding: string, newLine: string, codec: NativeCodec) {
  const normalized = normalizeNewLines(text, newLine)
  return { text: normalized, bytes: encodeDocument(normalized, encoding, codec), encoding: resolveEncoding(encoding) }
}
export function dirtyAfterSave(savedRevision: string, currentRevision: string): boolean { return savedRevision !== currentRevision }
export function encodingChange(current: string, target: string, hasFile: boolean, readOnly: boolean) {
  if (readOnly) return 'readOnly'
  if (resolveEncoding(current).id === resolveEncoding(target).id) return 'none'
  return hasFile ? 'prompt' : 'set'
}

export type RecoveryRecord = {
  documentId: string; documentPath: string | null; markdown: string; revision: string;
  timestamp: string; displayName: string | null; encoding?: string; newLine?: string
}
function validRecord(value: Record<string, unknown>): RecoveryRecord {
  const id = value.documentId
  if (typeof id !== 'string' || !/^[0-9a-f]{8}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{4}-?[0-9a-f]{12}$/i.test(id)) throw new DocumentKernelError('invalid_recovery', 'Invalid recovery document ID')
  if (typeof value.markdown !== 'string' || typeof value.timestamp !== 'string' || !Number.isFinite(Date.parse(value.timestamp))) throw new DocumentKernelError('invalid_recovery', 'Recovery content or timestamp is missing')
  const revision = String(value.revision)
  if (!/^\d+$/.test(revision) || BigInt(revision) > 9223372036854775807n) throw new DocumentKernelError('invalid_recovery', 'Invalid recovery revision')
  return { documentId: id, markdown: value.markdown, revision, timestamp: new Date(value.timestamp).toISOString(),
    documentPath: typeof value.documentPath === 'string' ? value.documentPath : null,
    displayName: typeof value.displayName === 'string' ? value.displayName : null,
    ...(typeof value.encoding === 'string' ? { encoding: value.encoding } : {}),
    ...(typeof value.newLine === 'string' ? { newLine: value.newLine } : {}),
  }
}
export function serializeRecovery(record: RecoveryRecord): string { return JSON.stringify({ schema: 1, ...validRecord(record) }) }
export function parseRecovery(content: string, legacyMarkdown?: string): RecoveryRecord {
  const value: Record<string, unknown> = JSON.parse(content.replace(/^\uFEFF/, ''))
  if (typeof legacyMarkdown === 'string') {
    // Explicit reader for files emitted before the single-file snapshot format.
    return validRecord({ documentId: value.documentId ?? value.DocumentId, documentPath: value.documentPath ?? value.DocumentPath,
      markdown: legacyMarkdown, revision: value.revision ?? value.Revision, timestamp: value.timestamp ?? value.Timestamp,
      displayName: value.displayName ?? value.DisplayName })
  }
  if (value.schema !== 1) throw new DocumentKernelError('unsupported_recovery', 'Unsupported recovery snapshot schema')
  return validRecord(value)
}
export function recoveryFileName(owner: number, documentId: string): string {
  if (!Number.isSafeInteger(owner) || owner < 0 || !/^[0-9a-f-]+$/i.test(documentId)) throw new DocumentKernelError('invalid_recovery', 'Invalid recovery file identity')
  return `doc-${owner}-${documentId.replace(/-/g, '').toLowerCase()}.recovery.json`
}
export function ownsRecoveryFile(name: string, documentId?: string, owner?: number): boolean {
  const match = /^doc-(\d+)-([0-9a-f-]+)(?:\.recovery\.json|\.md(?:\.meta)?)$/i.exec(name)
  return !!match && (owner == null || Number(match[1]) === owner) && (documentId == null || match[2]!.replace(/-/g, '').toLowerCase() === documentId.replace(/-/g, '').toLowerCase())
}

export function selectRecoveries(records: RecoveryRecord[]): RecoveryRecord[] {
  const selected = new Map<string, RecoveryRecord>()
  for (const record of records) {
    const key = record.documentId.replace(/-/g, '').toLowerCase()
    const previous = selected.get(key)
    if (!previous || Date.parse(previous.timestamp) < Date.parse(record.timestamp)) selected.set(key, record)
  }
  return [...selected.values()].sort((left, right) => Date.parse(right.timestamp) - Date.parse(left.timestamp))
}
