import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { mount, flushPromises } from '@vue/test-utils'
import SettleUpWizard from '@/components/import/SettleUpWizard.vue'
import { setApiClient } from '@/api/provider'
import type { ApiClient } from '@/api/client'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

/**
 * The Settle Up import, as a person walks it.
 *
 * The server has done this work for a while with no way to reach it. The shape
 * comes from a real export: the purpose of each row is the thing worth reading,
 * the people are names rather than accounts, and a row can be a transfer rather
 * than an expense.
 */

const analysis = {
  analysisId: 'analysis-1',
  headers: [
    'Who paid', 'Amount', 'Currency', 'For whom', 'Split amounts', 'Purpose',
    'Category', 'Date & time', 'Timezone', 'Exchange rate', 'Converted amount',
    'Type', 'Receipt',
  ],
  sampleRows: [
    ['Emma', '418.86', 'CAD', 'Nicolas;Emma', '209.43;209.43', 'Flights YYC to YUL', ' ',
      '2026-05-15 21:38:24', '', '', '418.86', 'expense', ''],
  ],
  suggestedMapping: {
    paidBy: 0, amount: 1, currency: 2, participants: 3, splitAmounts: 4,
    description: 5, category: 6, date: 7, type: 11,
  },
  detectedMemberNames: ['Emma', 'Nicolas'],
  detectedDelimiter: ',',
  detectedCurrency: 'CAD',
  rowCount: 28,
}

const preview = {
  analysisId: 'preview-1',
  rows: [
    {
      rowNumber: 1,
      spentAt: '2026-05-15T21:38:24Z',
      description: 'Flights YYC to YUL',
      amount: 418.86,
      currency: 'CAD',
      paidByName: 'Emma',
      paidByMemberId: null,
      participantNames: ['Nicolas', 'Emma'],
      participantMemberIds: [],
      fingerprint: 'aaa',
      isDuplicate: false,
      duplicateOfExpenseId: null,
      problems: [],
      splitAmounts: [209.43, 209.43],
      isSettlement: false,
    },
    {
      rowNumber: 2,
      spentAt: '2026-07-10T01:32:58Z',
      description: 'Debt settlement',
      amount: 590.8,
      currency: 'CAD',
      paidByName: 'Emma',
      paidByMemberId: null,
      participantNames: ['Nicolas'],
      participantMemberIds: [],
      fingerprint: 'bbb',
      isDuplicate: false,
      duplicateOfExpenseId: null,
      problems: [],
      splitAmounts: [590.8],
      isSettlement: true,
    },
    {
      rowNumber: 3,
      spentAt: '2026-06-21T19:32:22Z',
      description: 'Paella Madrid',
      amount: 42,
      currency: 'CAD',
      paidByName: 'Nicolas',
      paidByMemberId: null,
      participantNames: ['Emma', 'Nicolas'],
      participantMemberIds: [],
      fingerprint: 'ccc',
      isDuplicate: true,
      duplicateOfExpenseId: 'expense-9',
      problems: [],
      splitAmounts: [21, 21],
      isSettlement: false,
    },
  ],
  committableCount: 3,
  problemCount: 0,
  duplicateCount: 1,
  unmappedMemberNames: ['Emma', 'Nicolas'],
}

function fakeClient(overrides: Partial<Record<string, unknown>> = {}) {
  const upload = vi.fn(async (path: string) => {
    if (path.endsWith('/analyze')) return analysis
    if (path.endsWith('/preview')) return preview
    return { importBatchId: 'b1', groupId: 'group-new', createdExpenses: 2, createdSettlements: 1, skippedRows: 0, createdMemberIds: [], warnings: [] }
  })

  return {
    upload,
    get: vi.fn(async () => []),
    post: vi.fn(async () => ({})),
    put: vi.fn(async () => ({})),
    delete: vi.fn(async () => ({})),
    ...overrides,
  } as unknown as ApiClient & { upload: ReturnType<typeof vi.fn> }
}

const file = () => new File(['x'], 'World tour.csv', { type: 'text/csv' })

async function choose(wrapper: ReturnType<typeof mount>, named = file()) {
  const input = wrapper.find('input[type="file"]')
  Object.defineProperty(input.element, 'files', { value: [named], configurable: true })
  await input.trigger('change')
  await flushPromises()
  await flushPromises()
}

describe('SettleUpWizard', () => {
  let client: ReturnType<typeof fakeClient>

  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    client = fakeClient()
    setApiClient(client)

    const groups = useGroupsStore()
    groups.attachApi(client)
    groups.groups = [
      {
        id: 'group-ski',
        name: 'Ski trip',
        baseCurrency: 'CAD',
        colorHex: '#0ea5e9',
        isArchived: false,
        lineageId: 'l1',
        members: [
          { id: 'm1', userId: 'u1', displayName: 'Nicolas', avatarUrl: null, role: 'Owner', status: 'Active', isPlaceholder: false, netBalance: 0 },
        ],
        myNetBalance: 0,
        totalSpend: 0,
        expenseCount: 0,
        updatedAt: '2026-01-01T00:00:00Z',
      },
    ]
  })

  const mountWizard = () => mount(SettleUpWizard)

  it('reads the file and names the columns it found', async () => {
    const wrapper = mountWizard()

    await choose(wrapper)

    expect(client.upload).toHaveBeenCalledWith('/import/csv/analyze', { file: expect.any(File) })
    expect(wrapper.text()).toContain('28')
  })

  it('defaults to a new group named after the file', async () => {
    const wrapper = mountWizard()

    await choose(wrapper)

    // An export is one group's history, and the reason to import it is that the
    // group is not here yet.
    const newGroup = wrapper.find('input[value="new"]')
    expect((newGroup.element as HTMLInputElement).checked).toBe(true)
    expect((wrapper.find('[data-testid="new-group-name"]').element as HTMLInputElement).value)
      .toBe('World tour')
  })

  it('can import into an existing group instead', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('input[value="existing"]').setValue()
    await flushPromises()

    const select = wrapper.find('[data-testid="existing-group"]')
    expect(select.exists()).toBe(true)
    expect(select.text()).toContain('Ski trip')
  })

  it('lists the people in the export so they can be mapped', async () => {
    const wrapper = mountWizard()

    await choose(wrapper)

    const rows = wrapper.findAll('[data-testid="name-map"]')
    expect(rows).toHaveLength(2)
    expect(rows.map((row) => row.text()).join(' ')).toContain('Emma')
    expect(rows.map((row) => row.text()).join(' ')).toContain('Nicolas')
  })

  it('shows the purpose of every row, not just the amount and date', async () => {
    const wrapper = mountWizard()

    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('Flights YYC to YUL')
    expect(text).toContain('Paella Madrid')
    expect(text).toContain('Debt settlement')
  })

  it('marks a transfer as a settlement rather than an expense', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const settlement = wrapper.findAll('[data-testid="row"]')
      .find((row) => row.text().includes('Debt settlement'))

    expect(settlement!.text()).toContain('Settlement')
  })

  it('shows an ignored row as ignored', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const first = wrapper.findAll('[data-testid="row"]')[0]
    await first.find('[data-testid="toggle-row"]').trigger('click')

    // Struck through and dimmed, not merely a changed button label: the state has
    // to be readable at a glance down a list of twenty eight rows.
    expect(first.attributes('data-ignored')).toBe('true')
    expect(first.find('.line-through').exists()).toBe(true)
  })

  it('leaves an ignored row out of the count to import', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    // Two of the three, because the duplicate starts ignored: importing something
    // already recorded is the one outcome nobody wants, and it is one tap to undo.
    expect(wrapper.find('[data-testid="commit"]').text()).toContain('2')

    await wrapper.findAll('[data-testid="row"]')[0]
      .find('[data-testid="toggle-row"]').trigger('click')

    expect(wrapper.find('[data-testid="commit"]').text()).toContain('1')
  })

  it('can restore a row it ignored', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const first = wrapper.findAll('[data-testid="row"]')[0]
    await first.find('[data-testid="toggle-row"]').trigger('click')
    await first.find('[data-testid="toggle-row"]').trigger('click')

    expect(first.attributes('data-ignored')).toBe('false')
  })

  it('starts a row already recorded as ignored', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const duplicate = wrapper.findAll('[data-testid="row"]')
      .find((row) => row.text().includes('Paella Madrid'))

    expect(duplicate!.attributes('data-ignored')).toBe('true')
  })

  it('flags a row already recorded', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const duplicate = wrapper.findAll('[data-testid="row"]')
      .find((row) => row.text().includes('Paella Madrid'))

    expect(duplicate!.text()).toContain('Already recorded')
  })

  it('sends the group name and the skipped rows on commit', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    await wrapper.findAll('[data-testid="row"]')[0]
      .find('[data-testid="toggle-row"]').trigger('click')
    await wrapper.find('[data-testid="commit"]').trigger('click')
    await flushPromises()
    await flushPromises()

    const [path, form] = client.upload.mock.calls.at(-1)!
    expect(path).toBe('/import/csv/commit')

    const request = JSON.parse((form as Record<string, string>).request)
    expect(request.groupId).toBeNull()
    expect(request.newGroupName).toBe('World tour')
    // The row just ignored, plus the duplicate that started that way.
    expect(request.skipRowNumbers).toEqual([1, 3])
    expect(request.createMissingMembers).toBe(true)
  })

  it('reports what it imported', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()
    await wrapper.find('[data-testid="commit"]').trigger('click')
    await flushPromises()
    await flushPromises()

    expect(wrapper.emitted('imported')).toBeTruthy()
  })

  it('explains a file it cannot read', async () => {
    client = fakeClient({
      upload: vi.fn(async () => {
        throw new Error('That file does not look like a CSV export.')
      }),
    })
    setApiClient(client)

    const wrapper = mountWizard()
    await choose(wrapper)

    expect(wrapper.text()).toContain('does not look like a CSV export')
  })

  it('refuses to go on without a group chosen to import into', async () => {
    const groups = useGroupsStore()
    groups.groups = []

    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('input[value="existing"]').setValue()
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Choose the group')
  })

  it('explains a failure to read the rows', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    client.upload.mockRejectedValueOnce(new Error('the date could not be read'))
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('the date could not be read')
  })

  it('explains a failure to import', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()
    await flushPromises()

    client.upload.mockRejectedValueOnce(new Error('That group is archived.'))
    await wrapper.find('[data-testid="commit"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('That group is archived.')
    expect(wrapper.emitted('imported')).toBeFalsy()
  })

  it('offers the members of the group chosen, to map a name onto', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('input[value="existing"]').setValue()
    await flushPromises()

    const select = wrapper.findAll('[data-testid="name-map"]')[0].find('select')
    expect(select.text()).toContain('Nicolas')
    expect(select.text()).toContain('Add as a new person')
  })

  it('sends a name mapped onto an existing member', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('input[value="existing"]').setValue()
    await flushPromises()
    await wrapper.findAll('[data-testid="name-map"]')[0].find('select').setValue('m1')
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    const [, form] = client.upload.mock.calls.at(-1)!
    const request = JSON.parse((form as Record<string, string>).request)
    expect(request.groupId).toBe('group-ski')
    expect(request.memberNameMapping.Emma).toBe('m1')
  })

  it('names the columns it will use, so a wrong guess is visible', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    const [, form] = client.upload.mock.calls.at(-1)!
    const request = JSON.parse((form as Record<string, string>).request)

    // The three the wizard cannot work without on a real export.
    expect(request.mapping.participantsColumn).toBe(3)
    expect(request.mapping.splitAmountsColumn).toBe(4)
    expect(request.mapping.typeColumn).toBe(11)
    expect(request.mapping.descriptionColumn).toBe(5)
  })

  it('falls back to sensible columns when the export names none of them', async () => {
    client = fakeClient({
      upload: vi.fn(async (path: string) =>
        path.endsWith('/analyze')
          ? { ...analysis, suggestedMapping: {}, detectedMemberNames: [], detectedCurrency: null }
          : preview,
      ),
    })
    setApiClient(client)

    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    const [, form] = client.upload.mock.calls.at(-1)!
    const request = JSON.parse((form as Record<string, string>).request)

    expect(request.mapping.dateColumn).toBe(0)
    expect(request.mapping.descriptionColumn).toBe(1)
    expect(request.mapping.amountColumn).toBe(2)
    expect(request.mapping.participantsColumn).toBeNull()
    expect(request.fallbackCurrency).toBe('CAD')
  })

  it('shows a row with no date rather than hiding it', async () => {
    client = fakeClient({
      upload: vi.fn(async (path: string) =>
        path.endsWith('/analyze')
          ? analysis
          : {
              ...preview,
              rows: [{ ...preview.rows[0], spentAt: null, problems: ['the date could not be read'] }],
            },
      ),
    })
    setApiClient(client)

    const wrapper = mountWizard()
    await choose(wrapper)
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    const text = wrapper.text()
    expect(text).toContain('No date')
    expect(text).toContain('the date could not be read')
  })

  it('starts over when cancelled', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.findAll('button').find((b) => b.text() === 'Cancel')!.trigger('click')
    await flushPromises()

    expect(wrapper.emitted('cancel')).toBeTruthy()
    expect(wrapper.find('input[type="file"]').exists()).toBe(true)
  })

  it('refuses to go on without a name for a new group', async () => {
    const wrapper = mountWizard()
    await choose(wrapper)

    await wrapper.find('[data-testid="new-group-name"]').setValue('   ')
    await wrapper.find('[data-testid="to-preview"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('Name the group')
    expect(client.upload).toHaveBeenCalledTimes(1)
  })
})
