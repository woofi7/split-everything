import type { StatementRow } from './statementParser'
import type { StatementWorkerRequest, StatementWorkerResponse } from '@/workers/statementWorker'
import { newId } from '@/domain/ids'

export interface ParseProgress {
  stage: string
  ratio: number
}

/**
 * Talks to the parsing worker.
 *
 * The worker exists so a multi-page PDF or an OCR pass cannot freeze the UI,
 * which matters most on a phone. This wrapper keeps that a detail: callers await
 * rows and get progress callbacks.
 */
export class StatementWorkerClient {
  private worker: Worker | null = null

  private ensureWorker(): Worker {
    this.worker ??= new Worker(new URL('../workers/statementWorker.ts', import.meta.url), {
      type: 'module',
    })
    return this.worker
  }

  parseCsv(text: string, onProgress?: (progress: ParseProgress) => void) {
    return this.run({ kind: 'csv', id: newId(), text }, onProgress)
  }

  parsePdf(
    buffer: ArrayBuffer,
    statementYear: number,
    onProgress?: (progress: ParseProgress) => void,
  ) {
    return this.run(
      { kind: 'pdf', id: newId(), buffer, statementYear },
      onProgress,
      [buffer],
    )
  }

  private run(
    request: StatementWorkerRequest,
    onProgress?: (progress: ParseProgress) => void,
    transfer: Transferable[] = [],
  ): Promise<{ rows: StatementRow[]; usedOcr: boolean }> {
    const worker = this.ensureWorker()

    return new Promise((resolve, reject) => {
      const onMessage = (event: MessageEvent<StatementWorkerResponse>) => {
        const response = event.data
        if (response.id !== request.id) return

        if (response.kind === 'progress') {
          onProgress?.({ stage: response.stage, ratio: response.ratio })
          return
        }

        worker.removeEventListener('message', onMessage)

        if (response.kind === 'error') reject(new Error(response.message))
        else resolve({ rows: response.rows, usedOcr: response.usedOcr })
      }

      worker.addEventListener('message', onMessage)
      worker.postMessage(request, transfer)
    })
  }

  /** Ends the worker, which also drops the parsed statement from its memory. */
  dispose(): void {
    this.worker?.terminate()
    this.worker = null
  }
}
