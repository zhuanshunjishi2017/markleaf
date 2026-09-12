import type { DocumentSnapshot, WebviewMessage } from './vscode-protocol'

type EditMessage = Extract<WebviewMessage, { type: 'edit' }>

/** One edit in flight, with the latest local snapshot queued behind it.
 * Acknowledgements never parse or replace the visible editor document. */
export class TextDocumentSync {
  private version = -1
  private sequence = 0
  private acknowledged = ''
  private visible = ''
  private inFlight?: EditMessage
  private composing = false
  private deferredDocument?: DocumentSnapshot
  private flushRequests = new Set<number>()
  conflict: string | undefined

  constructor(private readonly callbacks: {
    post(message: WebviewMessage): void
    // Return the initial serialization to distinguish parsing from user edits.
    render(document: DocumentSnapshot): string
    status(): void
  }) {}

  get pending(): boolean {
    return this.inFlight !== undefined || this.visible !== this.acknowledged || this.composing
  }

  get markdown(): string { return this.visible }

  receiveDocument(document: DocumentSnapshot): void {
    if (document.version < this.version) return
    if (this.composing) {
      this.deferredDocument = document
      return
    }
    if (this.pending || this.conflict) {
      this.conflict = '文件已在其他视图中修改。未同步的编辑保留在此处，请先将它打开为草稿，再合并到源码。'
      this.callbacks.status()
      this.completeFlushes()
      return
    }
    this.reset(document)
  }

  reset(document: DocumentSnapshot): void {
    // Do not associate the old view with a new host version if parsing fails.
    const rendered = this.callbacks.render(document)
    this.inFlight = undefined
    this.deferredDocument = undefined
    this.conflict = undefined
    this.version = document.version
    this.visible = this.acknowledged = rendered
    this.callbacks.status()
    this.completeFlushes()
  }

  change(markdown: string): void {
    this.visible = markdown
    this.sendNext()
  }

  setComposing(composing: boolean): void {
    this.composing = composing
    if (!composing && this.deferredDocument) {
      const document = this.deferredDocument
      this.deferredDocument = undefined
      this.receiveDocument(document)
    }
    this.sendNext()
  }

  accept(sequence: number, version: number): void {
    if (this.inFlight?.sequence !== sequence) return
    this.version = Math.max(this.version, version)
    this.acknowledged = this.inFlight.markdown
    this.inFlight = undefined
    this.sendNext()
  }

  reject(sequence: number, error: string): void {
    if (this.inFlight?.sequence !== sequence) return
    this.inFlight = undefined
    this.conflict = error
    this.callbacks.status()
    this.completeFlushes()
  }

  flush(requestId: number): void {
    this.flushRequests.add(requestId)
    this.sendNext()
  }

  private sendNext(): void {
    if (this.version >= 0 && !this.composing && !this.conflict && !this.inFlight
      && this.visible !== this.acknowledged) {
      this.inFlight = {
        type: 'edit', sequence: ++this.sequence,
        baseVersion: this.version, markdown: this.visible,
      }
      this.callbacks.post(this.inFlight)
    }
    this.callbacks.status()
    this.completeFlushes()
  }

  private completeFlushes(): void {
    if (this.pending && !this.conflict) return
    for (const requestId of this.flushRequests) {
      this.callbacks.post({ type: 'flushed', requestId, success: !this.conflict })
    }
    this.flushRequests.clear()
  }
}
