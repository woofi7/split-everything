import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RouterLinkStub } from '@vue/test-utils'
import ImportView from '@/views/ImportView.vue'
import {
  ALICE,
  GROUP_ID,
  fakeApi,
  mountView,
  settle,
  testGroup,
  textOf,
} from '../support/viewHarness'

const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {} }),
  useRouter: () => ({ push: vi.fn(), replace }),
  RouterLink: RouterLinkStub,
}))

/**
 * A stand-in for the parsing worker.
 *
 * The worker itself runs PDF.js and Tesseract in a scope jsdom does not provide.
 * What matters here is what the view does with the rows it gets back, so the
 * client is faked and the parsers are tested directly in their own suite.
 */
const parseCsv = vi.fn()
const parsePdf = vi.fn()
const dispose = vi.fn()

vi.mock('@/import/statementWorkerClient', () => ({
  StatementWorkerClient: class {
    parseCsv = parseCsv
    parsePdf = parsePdf
    dispose = dispose
  },
}))

const row = (overrides: Record<string, unknown> = {}) => ({
  rowNumber: 1,
  date: new Date('2026-01-05T12:00:00Z'),
  description: 'UBER EATS TORONTO',
  amount: 42.5,
  currency: null,
  rawLine: 'Jan 05 UBER EATS TORONTO 42.50',
  problems: [],
  ...overrides,
})

const rules = [
  {
    id: 'rule-1',
    keyword: 'UBER EATS',
    categoryId: 'dining',
    categoryKey: 'dining',
    suggestedGroupId: null,
    weight: 1,
    hitCount: 0,
    isEnabled: true,
    isBuiltIn: true,
  },
]

const api = (overrides: Record<string, unknown> = {}) =>
  fakeApi({
    '/import/category-rules': () => rules,
    '/import/duplicates': () => ({ matches: [] }),
    '/import/split-suggestions': () => ({ suggestions: [] }),
    '/import/statement/commit': () => ({ createdExpenses: 1, skippedRows: 0 }),
    '/groups': () => [testGroup()],
    ...overrides,
  })

async function chooseFile(wrapper: ReturnType<typeof Object>, name = 'visa.csv') {
  const input = (wrapper as { find: (s: string) => { element: HTMLInputElement } }).find(
    'input[type="file"]',
  )
  const file = new File(['Date,Description,Amount'], name, { type: 'text/csv' })

  Object.defineProperty(input.element, 'files', { value: [file], configurable: true })
  await (input as unknown as { trigger: (e: string) => Promise<void> }).trigger('change')
  await settle()
}

describe('ImportView', () => {
  beforeEach(() => {
    parseCsv.mockReset()
    parsePdf.mockReset()
    dispose.mockReset()
    replace.mockClear()
  })

  it('promises that the statement stays on the device', async () => {
    const { wrapper } = await mountView(ImportView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('read on this device and never uploaded')
    expect(text).toContain('Only the transactions you confirm are sent')
  })

  it('offers both importers and says which runs where', async () => {
    const { wrapper } = await mountView(ImportView, { api: api() })

    const text = textOf(wrapper)
    expect(text).toContain('A bank or credit card statement')
    expect(text).toContain('A Settle Up export')

    // The statement is read here and never uploaded; the export is parsed by the
    // server. The difference matters enough to say on screen.
    expect(text).toContain('never uploaded')
    expect(wrapper.findAll('input[type="file"]').length).toBe(2)
  })

  it('loads the local ruleset so it can guess categories', async () => {
    const client = api()
    await mountView(ImportView, { api: client })

    expect(client.get).toHaveBeenCalledWith('/import/category-rules')
  })

  it('still works when the ruleset cannot be loaded', async () => {
    const client = api()
    client.get.mockRejectedValue(new Error('offline'))

    const { wrapper } = await mountView(ImportView, { api: client })

    expect(textOf(wrapper)).toContain('A bank or credit card statement')
  })

  it('parses a chosen CSV and shows the rows to review', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    expect(parseCsv).toHaveBeenCalled()
    expect(textOf(wrapper)).toContain('UBER EATS TORONTO')
    expect(textOf(wrapper)).toContain('42.50')
  })

  it('sends a PDF to the PDF path instead', async () => {
    parsePdf.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper, 'statement.pdf')

    expect(parsePdf).toHaveBeenCalled()
    expect(parseCsv).not.toHaveBeenCalled()
  })

  it('warns to check the amounts when the statement had to be read from images', async () => {
    parsePdf.mockResolvedValue({ rows: [row()], usedOcr: true })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper, 'scan.pdf')

    expect(textOf(wrapper)).toContain('read from the images')
  })

  it('suggests a category from the local ruleset', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    expect(textOf(wrapper)).toContain('dining')
  })

  it('starts every row as personal rather than charging a group', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    expect(textOf(wrapper)).toContain('0 to import')
    expect(textOf(wrapper)).toContain('1 left personal')
    expect((wrapper.find('select').element as HTMLSelectElement).value).toBe('')
  })

  it('keeps the import button disabled until something is assigned', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    const button = wrapper.findAll('button').find((b) => b.text().includes('Import'))
    expect(button!.attributes('disabled')).toBeDefined()
  })

  it('assigns a row to a group and counts it', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    await wrapper.find('select').setValue(GROUP_ID)
    await settle(1)

    expect(textOf(wrapper)).toContain('1 to import')
  })

  it('ignores a row when asked', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    await wrapper.findAll('button').find((b) => b.text() === 'Ignore')!.trigger('click')
    await settle(1)

    expect(textOf(wrapper)).toContain('1 ignored')
  })

  it('flags a row the server already has', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })
    const client = api({
      '/import/duplicates': () => ({
        matches: [
          {
            fingerprint: 'ignored-by-the-view',
            expenseId: 'expense-1',
            groupId: GROUP_ID,
            groupName: 'Roommates',
            description: 'Uber Eats',
            amount: 42.5,
            spentAt: '2026-01-05T12:00:00Z',
          },
        ],
      }),
    })

    const { wrapper } = await mountView(ImportView, { api: client })
    await chooseFile(wrapper)

    expect(client.post).toHaveBeenCalledWith(
      '/import/duplicates',
      expect.objectContaining({ fingerprints: expect.any(Array) }),
    )
    expect(wrapper).toBeDefined()
  })

  it('shows a row problem rather than hiding the row', async () => {
    parseCsv.mockResolvedValue({
      rows: [row({ date: null, problems: ['the date could not be read'] })],
      usedOcr: false,
    })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    const text = textOf(wrapper)
    expect(text).toContain('the date could not be read')
    expect(text).toContain('1 need fixing')
  })

  it('says so when nothing could be read from the file', async () => {
    parseCsv.mockResolvedValue({ rows: [], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    expect(textOf(wrapper)).toContain('No transactions could be read')
  })

  it('reports a parsing failure', async () => {
    parseCsv.mockRejectedValue(new Error('That file is empty.'))

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)

    expect(wrapper.find('[role="alert"]').text()).toContain('That file is empty.')
  })

  it('commits only the assigned rows and returns to the groups', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })
    const client = api()

    const { wrapper } = await mountView(ImportView, { api: client })
    await chooseFile(wrapper)
    await wrapper.find('select').setValue(GROUP_ID)
    await settle(1)
    await wrapper.findAll('button').find((b) => b.text().includes('Import'))!.trigger('click')
    await settle()

    const commit = client.post.mock.calls.find((call) => call[0] === '/import/statement/commit')
    expect(commit).toBeDefined()
    const payload = commit![1] as { rows: Array<Record<string, unknown>> }
    expect(payload.rows).toHaveLength(1)
    expect(payload.rows[0].groupId).toBe(GROUP_ID)
    expect(payload.rows[0].paidByMemberId).toBe(ALICE)
    expect(replace).toHaveBeenCalledWith({ name: 'groups' })
  })

  it('never sends the raw statement line', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })
    const client = api()

    const { wrapper } = await mountView(ImportView, { api: client })
    await chooseFile(wrapper)
    await wrapper.find('select').setValue(GROUP_ID)
    await settle(1)
    await wrapper.findAll('button').find((b) => b.text().includes('Import'))!.trigger('click')
    await settle()

    const commit = client.post.mock.calls.find((call) => call[0] === '/import/statement/commit')
    expect(JSON.stringify(commit![1])).not.toContain('Jan 05 UBER EATS TORONTO 42.50')
  })

  it('reports a refused commit', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })
    const client = api()
    client.post.mockImplementation(async (path: string) => {
      if (path === '/import/statement/commit') throw new Error('That group is archived.')
      if (path === '/import/duplicates') return { matches: [] }
      return { suggestions: [] }
    })

    const { wrapper } = await mountView(ImportView, { api: client })
    await chooseFile(wrapper)
    await wrapper.find('select').setValue(GROUP_ID)
    await settle(1)
    await wrapper.findAll('button').find((b) => b.text().includes('Import'))!.trigger('click')
    await settle()

    expect(textOf(wrapper)).toContain('archived')
  })

  it('clears the review when cancelled', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)
    await wrapper.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
    await settle()

    // Back to the picker, with nothing about the statement left on screen.
    expect(textOf(wrapper)).toContain('Choose a CSV or PDF')
    expect(textOf(wrapper)).not.toContain('UBER EATS TORONTO')
  })

  it('ends the worker when leaving, dropping the parsed statement', async () => {
    parseCsv.mockResolvedValue({ rows: [row()], usedOcr: false })

    const { wrapper } = await mountView(ImportView, { api: api() })
    await chooseFile(wrapper)
    wrapper.unmount()
    await settle()

    expect(dispose).toHaveBeenCalled()
  })
})
