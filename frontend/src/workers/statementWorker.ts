/// <reference lib="webworker" />
import { parseStatementCsv, extractTransactionsFromText, type StatementRow } from '@/import/statementParser'

/**
 * Statement parsing off the UI thread.
 *
 * A multi-page PDF, and especially OCR of a scanned one, takes seconds of solid
 * CPU. On a phone through Capacitor that would freeze the app, so all of it runs
 * here and the main thread only receives rows.
 *
 * Nothing in this worker touches the network. The statement stays on the device by
 * construction: there is no code path out of here except postMessage.
 */

export type StatementWorkerRequest =
  | { kind: 'csv'; id: string; text: string }
  | { kind: 'pdf'; id: string; buffer: ArrayBuffer; statementYear: number }

export type StatementWorkerResponse =
  | { kind: 'rows'; id: string; rows: StatementRow[]; usedOcr: boolean }
  | { kind: 'progress'; id: string; stage: string; ratio: number }
  | { kind: 'error'; id: string; message: string }

const scope = self as unknown as DedicatedWorkerGlobalScope

scope.addEventListener('message', async (event: MessageEvent<StatementWorkerRequest>) => {
  const request = event.data

  try {
    if (request.kind === 'csv') {
      const parsed = await parseStatementCsv(request.text)
      post({ kind: 'rows', id: request.id, rows: parsed.rows, usedOcr: false })
      return
    }

    const { rows, usedOcr } = await parsePdf(request)
    post({ kind: 'rows', id: request.id, rows, usedOcr })
  } catch (error) {
    post({
      kind: 'error',
      id: request.id,
      message: error instanceof Error ? error.message : String(error),
    })
  }
})

async function parsePdf(
  request: Extract<StatementWorkerRequest, { kind: 'pdf' }>,
): Promise<{ rows: StatementRow[]; usedOcr: boolean }> {
  post({ kind: 'progress', id: request.id, stage: 'Reading the PDF', ratio: 0.05 })

  const pdfjs = await import('pdfjs-dist')
  // The worker script is bundled alongside, so PDF.js does not try to fetch one.
  const workerSrc = await import('pdfjs-dist/build/pdf.worker.mjs?url')
  pdfjs.GlobalWorkerOptions.workerSrc = workerSrc.default

  const document = await pdfjs.getDocument({ data: request.buffer }).promise

  let text = ''
  for (let pageNumber = 1; pageNumber <= document.numPages; pageNumber++) {
    const page = await document.getPage(pageNumber)
    const content = await page.getTextContent()

    // PDF.js returns positioned fragments, not lines. Grouping by vertical
    // position rebuilds the rows a statement was laid out as, which the
    // line-oriented extractor depends on.
    text += `${rebuildLines(content.items as Array<{ str: string; transform: number[] }>)}\n`

    post({
      kind: 'progress',
      id: request.id,
      stage: `Reading page ${pageNumber} of ${document.numPages}`,
      ratio: 0.05 + (pageNumber / document.numPages) * 0.55,
    })
  }

  let rows = extractTransactionsFromText(text, request.statementYear)
  if (rows.length > 0) return { rows, usedOcr: false }

  // No usable text layer: the statement is a scan, so fall back to OCR.
  post({ kind: 'progress', id: request.id, stage: 'No text found, reading the images', ratio: 0.6 })

  const ocrText = await runOcr(request, document)
  rows = extractTransactionsFromText(ocrText, request.statementYear)

  return { rows, usedOcr: true }
}

async function runOcr(
  request: Extract<StatementWorkerRequest, { kind: 'pdf' }>,
  // The library's own document type, imported for the type only: a hand-written
  // shape of the two methods used below drifts from it the moment it changes.
  document: import('pdfjs-dist').PDFDocumentProxy,
): Promise<string> {
  const { createWorker } = await import('tesseract.js')
  const ocr = await createWorker('eng')

  try {
    let text = ''

    for (let pageNumber = 1; pageNumber <= document.numPages; pageNumber++) {
      const page = await document.getPage(pageNumber)
      const viewport = page.getViewport({ scale: 2 })

      const canvas = new OffscreenCanvas(viewport.width, viewport.height)
      const context = canvas.getContext('2d')
      if (!context) throw new Error('This device cannot render the PDF for reading.')

      /*
       * Both the canvas and its context, which the library now asks for. It used
       * to take the context alone, and typing the document properly is what
       * surfaced that: an OffscreenCanvas is not the HTMLCanvasElement the types
       * name, but it is what a worker has and what the renderer actually uses.
       */
      await page.render({
        canvas: canvas as unknown as HTMLCanvasElement,
        canvasContext: context as unknown as CanvasRenderingContext2D,
        viewport,
      }).promise

      const blob = await canvas.convertToBlob({ type: 'image/png' })
      const result = await ocr.recognize(blob)
      text += `${result.data.text}\n`

      post({
        kind: 'progress',
        id: request.id,
        stage: `Reading images, page ${pageNumber} of ${document.numPages}`,
        ratio: 0.6 + (pageNumber / document.numPages) * 0.4,
      })
    }

    return text
  } finally {
    await ocr.terminate()
  }
}

/** Groups positioned text fragments back into lines by their y coordinate. */
function rebuildLines(items: Array<{ str: string; transform: number[] }>): string {
  const lines = new Map<number, string[]>()

  for (const item of items) {
    if (!item.str) continue
    // Rounded, because glyphs on one visual line differ by fractions of a point.
    const y = Math.round(item.transform[5])
    const existing = lines.get(y)
    if (existing) existing.push(item.str)
    else lines.set(y, [item.str])
  }

  return [...lines.entries()]
    .sort((left, right) => right[0] - left[0])
    .map(([, parts]) => parts.join(' ').replace(/\s+/g, ' ').trim())
    .filter(Boolean)
    .join('\n')
}

function post(response: StatementWorkerResponse): void {
  scope.postMessage(response)
}
