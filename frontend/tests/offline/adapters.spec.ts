import { describe, expect, it, vi } from 'vitest'
import { HttpSyncApi } from '@/api/syncApi'
import { StatementWorkerClient } from '@/import/statementWorkerClient'

describe('sync transport', () => {
  const api = {
    post: vi.fn(async (path: string) => ({ path })),
  }

  it('pushes to the sync endpoint', async () => {
    const transport = new HttpSyncApi(api as never)

    await transport.push({ deviceId: 'device-a', operations: [] })

    expect(api.post).toHaveBeenCalledWith('/sync/push', {
      deviceId: 'device-a',
      operations: [],
    })
  })

  it('pulls from the sync endpoint', async () => {
    const transport = new HttpSyncApi(api as never)

    await transport.pull({ deviceId: 'device-a', groupCursors: { g: 3 }, maxEntries: 100 })

    expect(api.post).toHaveBeenCalledWith('/sync/pull', {
      deviceId: 'device-a',
      groupCursors: { g: 3 },
      maxEntries: 100,
    })
  })

  it('acknowledges cursors and returns nothing', async () => {
    const transport = new HttpSyncApi(api as never)

    expect(await transport.acknowledge({ g: 4 })).toBeUndefined()
    expect(api.post).toHaveBeenCalledWith('/sync/ack', { g: 4 })
  })
})

/**
 * A stand-in for the real Worker. The worker itself runs pdfjs and tesseract in a
 * worker scope jsdom does not provide; what this covers is the message protocol
 * between the two, which is where a mismatch would actually break the wizard.
 */
class FakeWorker implements Partial<Worker> {
  static instances: FakeWorker[] = []

  private listeners: Array<(event: MessageEvent) => void> = []
  posted: unknown[] = []
  terminated = false

  constructor() {
    FakeWorker.instances.push(this)
  }

  addEventListener(_type: string, listener: (event: MessageEvent) => void): void {
    this.listeners.push(listener)
  }

  removeEventListener(_type: string, listener: (event: MessageEvent) => void): void {
    this.listeners = this.listeners.filter((candidate) => candidate !== listener)
  }

  postMessage(message: unknown): void {
    this.posted.push(message)
  }

  terminate(): void {
    this.terminated = true
  }

  emit(data: unknown): void {
    for (const listener of [...this.listeners]) {
      listener({ data } as MessageEvent)
    }
  }

  get listenerCount(): number {
    return this.listeners.length
  }
}

describe('statement worker client', () => {
  function withFakeWorker(): { client: StatementWorkerClient; worker: () => FakeWorker } {
    FakeWorker.instances = []
    vi.stubGlobal('Worker', FakeWorker as unknown as typeof Worker)

    const client = new StatementWorkerClient()
    return { client, worker: () => FakeWorker.instances[0] }
  }

  it('sends the CSV text to the worker and resolves with its rows', async () => {
    const { client, worker } = withFakeWorker()

    const pending = client.parseCsv('Date,Description,Amount')
    const request = worker().posted[0] as { kind: string; id: string }
    expect(request.kind).toBe('csv')

    worker().emit({ kind: 'rows', id: request.id, rows: [{ rowNumber: 1 }], usedOcr: false })

    expect((await pending).rows).toHaveLength(1)
  })

  it('sends the PDF buffer and the statement year', async () => {
    const { client, worker } = withFakeWorker()
    const buffer = new ArrayBuffer(8)

    const pending = client.parsePdf(buffer, 2026)
    const request = worker().posted[0] as { kind: string; id: string; statementYear: number }

    expect(request.kind).toBe('pdf')
    expect(request.statementYear).toBe(2026)

    worker().emit({ kind: 'rows', id: request.id, rows: [], usedOcr: true })
    expect((await pending).usedOcr).toBe(true)
  })

  it('reports progress while parsing', async () => {
    const { client, worker } = withFakeWorker()
    const progress: Array<{ stage: string; ratio: number }> = []

    const pending = client.parseCsv('text', (update) => progress.push(update))
    const request = worker().posted[0] as { id: string }

    worker().emit({ kind: 'progress', id: request.id, stage: 'Reading page 1', ratio: 0.5 })
    worker().emit({ kind: 'rows', id: request.id, rows: [], usedOcr: false })
    await pending

    expect(progress).toEqual([{ stage: 'Reading page 1', ratio: 0.5 }])
  })

  it('rejects when the worker reports a failure', async () => {
    const { client, worker } = withFakeWorker()

    const pending = client.parseCsv('text')
    const request = worker().posted[0] as { id: string }
    worker().emit({ kind: 'error', id: request.id, message: 'That file is empty.' })

    await expect(pending).rejects.toThrow('That file is empty.')
  })

  it('ignores a message meant for another request', async () => {
    const { client, worker } = withFakeWorker()

    const pending = client.parseCsv('text')
    const request = worker().posted[0] as { id: string }

    // Two parses can be in flight; a response must only settle its own promise.
    worker().emit({ kind: 'rows', id: 'someone-else', rows: [{ rowNumber: 9 }], usedOcr: false })
    worker().emit({ kind: 'rows', id: request.id, rows: [], usedOcr: false })

    expect((await pending).rows).toHaveLength(0)
  })

  it('stops listening once a request settles', async () => {
    const { client, worker } = withFakeWorker()

    const pending = client.parseCsv('text')
    const request = worker().posted[0] as { id: string }
    worker().emit({ kind: 'rows', id: request.id, rows: [], usedOcr: false })
    await pending

    expect(worker().listenerCount).toBe(0)
  })

  it('reuses one worker across parses', async () => {
    const { client, worker } = withFakeWorker()

    const first = client.parseCsv('a')
    worker().emit({ kind: 'rows', id: (worker().posted[0] as { id: string }).id, rows: [], usedOcr: false })
    await first

    const second = client.parseCsv('b')
    worker().emit({ kind: 'rows', id: (worker().posted[1] as { id: string }).id, rows: [], usedOcr: false })
    await second

    expect(FakeWorker.instances).toHaveLength(1)
  })

  it('terminates the worker on dispose, dropping the parsed statement', () => {
    const { client, worker } = withFakeWorker()
    void client.parseCsv('text')

    client.dispose()

    // The statement lives in the worker's memory; ending it is part of the data
    // hygiene the spec asks for.
    expect(worker().terminated).toBe(true)
  })

  it('starts a fresh worker after being disposed', async () => {
    const { client } = withFakeWorker()
    void client.parseCsv('a')
    client.dispose()

    void client.parseCsv('b')

    expect(FakeWorker.instances).toHaveLength(2)
  })
})
