import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ExpenseFormView from '@/views/ExpenseFormView.vue'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { SyncEngine } from '@/offline/syncEngine'
import { textOf, waitFor } from '../support/viewHarness'

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

const push = vi.fn()
const replace = vi.fn()

/** Mutable, because this one form serves both adding and editing. */
let routeParams: Record<string, string> = {}

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, query: {}, fullPath: '/add' }),
  useRouter: () => ({ push, replace }),
  RouterLink: RouterLinkStub,
}))

const members = [
  {
    id: alice,
    userId: 'user-1',
    displayName: 'Alice',
    avatarUrl: null,
    role: 'Owner',
    status: 'Active',
    isPlaceholder: false,
    netBalance: 0,
  },
  {
    id: bob,
    userId: null,
    displayName: 'Bob',
    avatarUrl: null,
    role: 'Member',
    status: 'Active',
    isPlaceholder: true,
    netBalance: 0,
  },
]

const group = {
  id: groupId,
  name: 'Roommates',
  description: null,
  baseCurrency: 'CAD',
  iconName: null,
  colorHex: '#4f46e5',
  isArchived: false,
  lineageId: 'lineage-1',
  members,
  myNetBalance: 0,
  totalSpend: 0,
  expenseCount: 0,
  updatedAt: '2026-01-01T00:00:00Z',
}

function fakeSyncApi() {
  return {
    push: vi.fn(async (request: any) => ({
      accepted: request.operations.map((operation: any) => ({
        operationId: operation.operationId,
        entityId: operation.entityId,
        serverSeq: 1,
        vectorClock: operation.vectorClock,
      })),
      conflicts: [],
      rejected: [],
      groupCursors: {},
    })),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

async function mountView() {
  setActivePinia(createPinia())
  await resetDatabase()
  await db.groups.put(group)

  const auth = useAuthStore()
  auth.user = {
    id: 'user-1',
    email: 'alice@example.com',
    displayName: 'Alice',
    avatarUrl: null,
    defaultCurrency: 'CAD',
    prefersLightTheme: false,
  } as never

  const groups = useGroupsStore()
  const patch = vi.fn(async (_path: string, body: any) => ({
    ...groupOverrides,
    ...group,
    defaultSplitType: body.defaultSplitType,
    defaultSplitValues: Object.keys(body.defaultSplitValues ?? {}).length > 0
      ? body.defaultSplitValues
      : null,
  }))

  groups.attachApi({
    get: vi.fn(async (path: string) =>
      path === '/groups'
        ? [{ ...group, ...groupOverrides, memberCount: 2, lastActivityAt: null }]
        : { ...group, ...groupOverrides },
    ),
    post: vi.fn(),
    patch,
    delete: vi.fn(),
  } as never)
  groupsPatch = patch

  const expenses = useExpensesStore()
  expenses.attachSync(new SyncEngine(fakeSyncApi(), () => false))

  const wrapper = mount(ExpenseFormView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

  // The view loads groups from IndexedDB and then defaults the form. fake-indexeddb
  // resolves its transactions on a macrotask, so a microtask flush alone is not
  // enough to see the mounted state.
  await settle()

  return { wrapper, expenses, groups }
}

/** Lets a test set how the group splits before the form reads it. */
let groupOverrides: Record<string, unknown> = {}
let groupsPatch: ReturnType<typeof vi.fn> | null = null

/** Lets pending microtasks and IndexedDB transactions finish. */
async function settle(): Promise<void> {
  for (let i = 0; i < 5; i++) {
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 0))
  }
}

describe('ExpenseFormView', () => {
  beforeEach(() => {
    routeParams = {}
    groupOverrides = {}
    push.mockClear()
    replace.mockClear()
  })

  it('preselects the only group and everyone in it', async () => {
    const { wrapper } = await mountView()

    const checkboxes = wrapper.findAll('input[type="checkbox"]')
    expect(checkboxes).toHaveLength(2)
    expect(checkboxes.every((box) => (box.element as HTMLInputElement).checked)).toBe(true)
  })

  it('previews the split as the amount is typed', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()

    // The preview is the whole point: people check the numbers before saving. It
    // now sits on each person's chip rather than in a section of its own, which is
    // what bought the room to fit the form on one screen.
    expect(wrapper.text()).toContain('30.00')
  })

  it('updates the preview when someone is removed from the split', async () => {
    const { wrapper } = await mountView()
    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()

    await wrapper.findAll('input[type="checkbox"]')[1].setValue(false)
    await settle()

    expect(wrapper.text()).toContain('60.00')
  })

  it('keeps the save button disabled until the split is valid', async () => {
    const { wrapper } = await mountView()

    const submit = wrapper.find('button[type="submit"]')
    expect(submit.attributes('disabled')).toBeDefined()

    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()

    expect(submit.attributes('disabled')).toBeUndefined()
  })

  it('saves the expense locally and returns to the group', async () => {
    const { wrapper, expenses } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Groceries')
    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expenses.forGroup(groupId)).toHaveLength(1)
    expect(expenses.forGroup(groupId)[0].description).toBe('Groceries')

    // The navigation happens after the local write and the queue entry, so wait
    // for it rather than assuming a fixed number of turns.
    await vi.waitFor(() =>
      expect(replace).toHaveBeenCalledWith({ name: 'group', params: { groupId } }),
    )
  })

  it('queues the expense rather than waiting on the network', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Groceries')
    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(await db.outbox.count()).toBe(1)
  })

  it('says the expense is saved on the device and synced later', async () => {
    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('Saved on this device straight away')
  })

  it('explains an unbalanced percentage split instead of just refusing', async () => {
    const { wrapper } = await mountView()
    await wrapper.find('input[inputmode="decimal"]').setValue('60')

    const percentageButton = wrapper
      .findAll('button[type="button"]')
      .find((button) => button.text() === 'Percent')
    await percentageButton!.trigger('click')
    await settle()

    const inputs = wrapper.findAll('input[type="number"]')
    await inputs[0].setValue(40)
    await inputs[1].setValue(40)
    await settle()

    expect(wrapper.text()).toContain('100')
    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeDefined()
  })

  it('accepts a valid percentage split', async () => {
    const { wrapper, expenses } = await mountView()
    await wrapper.find('input[placeholder="Groceries"]').setValue('Rent')
    await wrapper.find('input[inputmode="decimal"]').setValue('100')

    const percentageButton = wrapper
      .findAll('button[type="button"]')
      .find((button) => button.text() === 'Percent')
    await percentageButton!.trigger('click')
    await settle()

    const inputs = wrapper.findAll('input[type="number"]')
    await inputs[0].setValue(30)
    await inputs[1].setValue(70)
    await settle()

    await wrapper.find('form').trigger('submit')
    await settle()

    const stored = expenses.forGroup(groupId)[0]
    expect(stored.splits.find((split) => split.memberId === bob)?.amount).toBe(70)
  })

  it('reads a comma decimal separator', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[inputmode="decimal"]').setValue('12,50')
    await settle()

    expect(wrapper.text()).toContain('6.25')
  })
})

describe('ExpenseFormView editing an expense', () => {
  const existing = {
    id: 'expense-1',
    groupId,
    paidByMemberId: bob,
    description: 'Groceries at Metro',
    amount: 84.32,
    currency: 'CAD',
    amountInBaseCurrency: 84.32,
    exchangeRate: 1,
    spentAt: '2026-03-14T12:00:00.000Z',
    splitType: 'Shares' as const,
    receiptId: null,
    notes: null,
    splits: [
      { memberId: alice, amount: 56.21, amountInBaseCurrency: 56.21, inputValue: 2 },
      { memberId: bob, amount: 28.11, amountInBaseCurrency: 28.11, inputValue: 1 },
    ],
    items: [],
    revision: 1,
    isDeleted: false,
    vectorClock: {},
    serverSeq: 1,
    pending: false,
  }

  beforeEach(() => {
    push.mockClear()
    replace.mockClear()
    routeParams = { groupId, expenseId: 'expense-1' }
  })

  async function mountEdit() {
    setActivePinia(createPinia())
    await resetDatabase()
    await db.groups.put(group)
    await db.expenses.put(existing)

    const auth = useAuthStore()
    auth.user = {
      id: 'user-1',
      email: 'alice@example.com',
      displayName: 'Alice',
      avatarUrl: null,
      defaultCurrency: 'CAD',
      prefersLightTheme: false,
    } as never

    const groups = useGroupsStore()
    groups.attachApi({
      get: vi.fn(async (path: string) =>
        path === '/groups' ? [{ ...group, memberCount: 2, lastActivityAt: null }] : group,
      ),
      post: vi.fn(),
      patch: vi.fn(),
      delete: vi.fn(),
    } as never)

    const expenses = useExpensesStore()
    expenses.attachSync(new SyncEngine(fakeSyncApi(), () => false))

    const wrapper = mount(ExpenseFormView, {
      global: { stubs: { RouterLink: RouterLinkStub } },
    })
    await settle()

    return { wrapper, expenses }
  }

  it('says it is editing rather than adding', async () => {
    const { wrapper } = await mountEdit()

    expect(wrapper.find('h1').text()).toBe('Edit expense')
  })

  it('fills the form from the expense', async () => {
    const { wrapper } = await mountEdit()

    expect((wrapper.find('input[placeholder="Groceries"]').element as HTMLInputElement).value)
      .toBe('Groceries at Metro')
    expect((wrapper.find('input[inputmode="decimal"]').element as HTMLInputElement).value)
      .toBe('84.32')
    expect((wrapper.find('input[type="date"]').element as HTMLInputElement).value)
      .toBe('2026-03-14')
  })

  it('keeps an uneven split someone set by hand', async () => {
    const { wrapper } = await mountEdit()

    // Read back out of the stored shares rather than recomputed, or the shares
    // people chose would be lost the moment they opened the form.
    // The chosen split type is the filled button; the others are outlined.
    const selected = wrapper
      .findAll('button')
      .filter((button) => button.classes().includes('btn-primary'))
      .map((button) => button.text())

    expect(selected).toContain('Shares')

    // The amount, plus one input per participant for their share.
    expect(wrapper.findAll('input[inputmode="decimal"]').length).toBe(3)
  })

  it('keeps who paid', async () => {
    const { wrapper } = await mountEdit()

    const payer = wrapper.findAll('select').find((select) =>
      (select.element as HTMLSelectElement).value === bob,
    )
    expect(payer).toBeDefined()
  })

  it('does not offer to move the expense to another group', async () => {
    const { wrapper } = await mountEdit()

    // Moving one has to carry its history, comments and audit trail; that is the
    // transfer feature, and a dropdown here would look like it did that.
    expect(textOf(wrapper)).not.toContain('Group')
  })

  it('saves the change and returns to the expense', async () => {
    const { wrapper, expenses } = await mountEdit()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Groceries at IGA')
    await wrapper.find('form').trigger('submit')
    // The redirect is the last thing the save does, so it is the only safe signal
    // that the whole action finished.
    await waitFor(() => replace.mock.calls.length > 0)

    expect(expenses.expenses.find((e) => e.id === 'expense-1')?.description)
      .toBe('Groceries at IGA')
    expect(replace).toHaveBeenCalledWith({
      name: 'expense',
      params: { groupId, expenseId: 'expense-1' },
    })
  })

  it('bumps the revision, so the change is on the record', async () => {
    const { wrapper, expenses } = await mountEdit()

    await wrapper.find('input[inputmode="decimal"]').setValue('90.00')
    await wrapper.find('form').trigger('submit')
    await waitFor(() => replace.mock.calls.length > 0)

    expect(expenses.expenses.find((e) => e.id === 'expense-1')?.revision).toBe(2)
  })

  it('queues the change so it works offline', async () => {
    const { wrapper } = await mountEdit()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Something else')
    await wrapper.find('form').trigger('submit')
    await waitFor(async () => (await db.outbox.count()) > 0)

    const queued = await db.outbox.toArray()
    expect(queued).toHaveLength(1)
    expect(queued[0].operation).toBe('Update')
  })

  it('offers a way back to the expense', async () => {
    const { wrapper } = await mountEdit()

    expect(wrapper.find('[data-testid="back"]').exists()).toBe(true)
  })

  it('says so when the expense is not on this device', async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    await db.groups.put(group)

    const auth = useAuthStore()
    auth.user = { id: 'user-1', email: 'a@b.c', displayName: 'Alice', avatarUrl: null, defaultCurrency: 'CAD', prefersLightTheme: false } as never

    const groups = useGroupsStore()
    groups.attachApi({ get: vi.fn(async () => group), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } as never)
    useExpensesStore().attachSync(new SyncEngine(fakeSyncApi(), () => false))

    const wrapper = mount(ExpenseFormView, { global: { stubs: { RouterLink: RouterLinkStub } } })
    await settle()

    expect(textOf(wrapper)).toContain('not on this device')
  })
})

describe('ExpenseFormView fits one screen', () => {
  beforeEach(() => {
    routeParams = {}
    push.mockClear()
    replace.mockClear()
  })

  it('asks for six things and no more', async () => {
    const { wrapper } = await mountView()

    // Adding an expense is what people open this app to do, usually one-handed, so
    // a form that scrolls hides half the decision. Amount, date, what it was,
    // group, who paid, and who it is between.
    const fields = wrapper.findAll('input:not([type="checkbox"]), select')
    expect(fields.length).toBeLessThanOrEqual(6)
  })

  it('asks which group', async () => {
    const { wrapper } = await mountView()

    expect(wrapper.find('[data-testid="group"]').exists()).toBe(true)
    expect(textOf(wrapper)).toContain('Roommates')
  })

  it('does not ask for a category', async () => {
    const { wrapper } = await mountView()

    // Dropped to keep the form on one screen. The by-category breakdown in stats
    // now only reflects what an import set.
    expect(wrapper.find('[data-testid="category"]').exists()).toBe(false)
    expect(textOf(wrapper)).not.toContain('Category')
  })

  it('does not offer to move an expense between groups while editing', async () => {
    routeParams = { groupId, expenseId: 'expense-1' }
    const { wrapper } = await mountView()

    expect(wrapper.find('[data-testid="group"]').exists()).toBe(false)
  })

  it('shows each share on the person rather than in a section below', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[inputmode="decimal"]').setValue('60')
    await settle()

    // One block instead of two: the row of people and the preview were the same
    // information twice.
    const chips = wrapper.findAll('fieldset li')
    expect(chips).toHaveLength(2)
    expect(chips[0].text()).toContain('30.00')
  })

  it('keeps the save button reachable however many people are in the group', async () => {
    const { wrapper } = await mountView()

    // Sticky, so a group of ten cannot push it off the bottom.
    expect(wrapper.find('button[type="submit"]').classes()).toContain('sticky')
  })

  it('fits the four split types on one row', async () => {
    const { wrapper } = await mountView()

    const labels = wrapper
      .findAll('fieldset button[type="button"]')
      .map((button) => button.text())

    expect(labels).toEqual(['Equally', 'Percent', 'Shares', 'Exact'])
    // Short enough that four abreast do not wrap on a phone.
    for (const label of labels) expect(label.length).toBeLessThanOrEqual(7)
  })
})

describe('ExpenseFormView switching split type', () => {
  beforeEach(() => {
    routeParams = {}
    push.mockClear()
    replace.mockClear()
  })

  async function withAmount(value: string) {
    const mounted = await mountView()
    await mounted.wrapper.find('input[inputmode="decimal"]').setValue(value)
    await settle()
    return mounted
  }

  const pick = async (wrapper: { findAll: (s: string) => any[] }, label: string) => {
    const button = wrapper.findAll('button[type="button"]').find((b: any) => b.text() === label)
    await button!.trigger('click')
  }

  const shareInputs = (wrapper: { findAll: (s: string) => any[] }) =>
    wrapper.findAll('input[type="number"]').map((input: any) => (input.element as HTMLInputElement).value)

  it('fills the percentages from an equal split', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Percent')
    await settle()

    // Empty boxes meant the split was invalid the moment the type changed.
    expect(shareInputs(wrapper)).toEqual(['50', '50'])
  })

  it('fills the exact amounts from an equal split', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Exact')
    await settle()

    expect(shareInputs(wrapper)).toEqual(['30', '30'])
  })

  it('keeps an uneven division across a change of type', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Percent')
    await settle()

    const inputs = wrapper.findAll('input[type="number"]')
    await inputs[0].setValue(70)
    await inputs[1].setValue(30)
    await settle()

    await pick(wrapper, 'Exact')
    await settle()

    // Seventy percent of sixty is forty two: the same division, said differently.
    expect(shareInputs(wrapper)).toEqual(['42', '18'])
  })

  it('leaves the split valid straight after switching', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Percent')
    await settle()

    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeUndefined()
  })

  it('shows what each person owes as well as the percentage', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Percent')
    await settle()

    // A percentage on its own does not say what anyone owes, and that is the
    // number people check before saving.
    expect(shareInputs(wrapper)).toEqual(['50', '50'])
    expect(wrapper.text()).toContain('30.00')
  })

  it('clears the values when going back to equal', async () => {
    const { wrapper } = await withAmount('60')

    await pick(wrapper, 'Exact')
    await settle()
    await pick(wrapper, 'Equally')
    await settle()

    // Equal needs no values, and stale ones would reappear on the next switch.
    expect(wrapper.findAll('input[type="number"]')).toHaveLength(0)
    expect(wrapper.text()).toContain('30.00')
  })

  it('does not fall over with no amount typed yet', async () => {
    const { wrapper } = await mountView()

    await pick(wrapper, 'Percent')
    await settle()

    expect(shareInputs(wrapper)).toEqual(['', ''])
  })

  it('saves what the carried values describe', async () => {
    const { wrapper, expenses } = await withAmount('60')

    await wrapper.find('input[placeholder="Groceries"]').setValue('Dinner')
    await pick(wrapper, 'Percent')
    await settle()
    await wrapper.find('form').trigger('submit')
    await waitFor(() => expenses.forGroup(groupId).length > 0)

    const saved = expenses.forGroup(groupId)[0]
    expect(saved.splitType).toBe('Percentage')
    expect(saved.splits.map((split) => split.amount)).toEqual([30, 30])
  })
})

describe('ExpenseFormView group default split', () => {
  beforeEach(() => {
    routeParams = {}
    groupOverrides = {}
    push.mockClear()
    replace.mockClear()
  })

  const shareInputs = (wrapper: { findAll: (s: string) => any[] }) =>
    wrapper.findAll('input[type="number"]').map((input: any) => (input.element as HTMLInputElement).value)

  it('starts equal when the group has no default', async () => {
    const { wrapper } = await mountView()

    const selected = wrapper.findAll('button').filter((b) => b.classes().includes('btn-primary'))
    expect(selected.map((b) => b.text())).toContain('Equally')
  })

  it('starts on the split the group uses', async () => {
    groupOverrides = {
      defaultSplitType: 'Shares',
      defaultSplitValues: { [alice]: 2, [bob]: 1 },
    }

    const { wrapper } = await mountView()

    // A household that always divides rent sixty forty had to say so every time.
    const selected = wrapper.findAll('button').filter((b) => b.classes().includes('btn-primary'))
    expect(selected.map((b) => b.text())).toContain('Shares')
    expect(shareInputs(wrapper)).toEqual(['2', '1'])
  })

  it('divides by that default without anything being typed', async () => {
    groupOverrides = {
      defaultSplitType: 'Shares',
      defaultSplitValues: { [alice]: 2, [bob]: 1 },
    }

    const { wrapper } = await mountView()
    await wrapper.find('input[inputmode="decimal"]').setValue('90')
    await settle()

    expect(wrapper.text()).toContain('60.00')
    expect(wrapper.text()).toContain('30.00')
  })

  it('ignores a stored weight for someone no longer in the group', async () => {
    groupOverrides = {
      defaultSplitType: 'Shares',
      defaultSplitValues: { [alice]: 2, 'member-who-left': 5 },
    }

    const { wrapper } = await mountView()
    await wrapper.find('input[inputmode="decimal"]').setValue('90')
    await settle()

    // Left in, the split would refuse to add up and nobody would know why.
    expect(wrapper.find('button[type="submit"]').attributes('disabled')).toBeUndefined()
  })

  it('offers to record the split as the default once it differs', async () => {
    const { wrapper } = await mountView()

    // Nothing to record while the form matches what the group already does.
    expect(wrapper.find('[data-testid="make-default"]').exists()).toBe(false)

    const shares = wrapper.findAll('button[type="button"]').find((b) => b.text() === 'Shares')
    await shares!.trigger('click')
    await settle()

    expect(wrapper.find('[data-testid="make-default"]').exists()).toBe(true)
  })

  it('records it when asked', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Rent')
    await wrapper.find('input[inputmode="decimal"]').setValue('90')
    const shares = wrapper.findAll('button[type="button"]').find((b) => b.text() === 'Shares')
    await shares!.trigger('click')
    await settle()

    await wrapper.find('[data-testid="make-default"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await waitFor(() => (groupsPatch?.mock.calls.length ?? 0) > 0)

    const [path, body] = groupsPatch!.mock.calls[0]
    expect(path).toBe(`/groups/${groupId}`)
    expect(body.defaultSplitType).toBe('Shares')
  })

  it('leaves the group alone when not asked', async () => {
    const { wrapper } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Rent')
    await wrapper.find('input[inputmode="decimal"]').setValue('90')
    const shares = wrapper.findAll('button[type="button"]').find((b) => b.text() === 'Shares')
    await shares!.trigger('click')
    await settle()

    await wrapper.find('form').trigger('submit')
    await settle()

    expect(groupsPatch).not.toHaveBeenCalled()
  })

  it('still saves the expense when recording the default fails', async () => {
    const { wrapper, expenses } = await mountView()
    groupsPatch!.mockRejectedValue(new Error('Only an admin can change that.'))

    await wrapper.find('input[placeholder="Groceries"]').setValue('Rent')
    await wrapper.find('input[inputmode="decimal"]').setValue('90')
    const shares = wrapper.findAll('button[type="button"]').find((b) => b.text() === 'Shares')
    await shares!.trigger('click')
    await settle()
    await wrapper.find('[data-testid="make-default"]').setValue(true)
    await wrapper.find('form').trigger('submit')
    await waitFor(() => expenses.forGroup(groupId).length > 0)

    // A group setting is not worth losing someone's expense over.
    expect(expenses.forGroup(groupId)).toHaveLength(1)
    expect(textOf(wrapper)).toContain('group default could not be changed')
  })
})
