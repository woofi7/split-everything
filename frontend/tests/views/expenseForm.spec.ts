import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ExpenseFormView from '@/views/ExpenseFormView.vue'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { useCategoriesStore } from '@/stores/categories'
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

  const categories = useCategoriesStore()
  categories.attachApi({
    get: vi.fn(async () => [
      { id: 'c1', key: 'groceries', name: 'Groceries', iconName: 'cart-shopping', colorHex: '#16a34a', sortOrder: 1 },
      { id: 'c2', key: 'dining', name: 'Restaurants', iconName: 'utensils', colorHex: '#f97316', sortOrder: 2 },
    ]),
    post: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  } as never)

  const wrapper = mount(ExpenseFormView, {
    global: { stubs: { RouterLink: RouterLinkStub } },
  })

  // The view loads groups from IndexedDB and then defaults the form. fake-indexeddb
  // resolves its transactions on a macrotask, so a microtask flush alone is not
  // enough to see the mounted state.
  await settle()

  return { wrapper, expenses, groups }
}

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

    // The preview is the whole point: people check the numbers before saving.
    expect(wrapper.text()).toContain('Each person owes')
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

    await wrapper.find('input[type="text"]').setValue('Groceries')
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

    await wrapper.find('input[type="text"]').setValue('Groceries')
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
      .find((button) => button.text() === 'By percentage')
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
    await wrapper.find('input[type="text"]').setValue('Rent')
    await wrapper.find('input[inputmode="decimal"]').setValue('100')

    const percentageButton = wrapper
      .findAll('button[type="button"]')
      .find((button) => button.text() === 'By percentage')
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

describe('ExpenseFormView categories', () => {
  beforeEach(() => {
    push.mockClear()
    replace.mockClear()
  })

  it('offers the categories the server knows', async () => {
    const { wrapper } = await mountView()

    const picker = wrapper.find('[data-testid="category"]')
    expect(picker.exists()).toBe(true)
    expect(picker.text()).toContain('Groceries')
    expect(picker.text()).toContain('Restaurants')
  })

  it('files the expense under the category chosen', async () => {
    const { wrapper, expenses } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Metro run')
    await wrapper.find('input[inputmode="decimal"]').setValue('42.50')
    await wrapper.find('[data-testid="category"]').setValue('c1')
    await wrapper.find('form').trigger('submit')
    await settle()

    // Without this every expense was uncategorised, and the by-category breakdown
    // in stats read "Uncategorised, 100%".
    expect(expenses.expenses.at(-1)?.categoryId).toBe('c1')
  })

  it('allows no category, since not everything has one', async () => {
    const { wrapper, expenses } = await mountView()

    await wrapper.find('input[placeholder="Groceries"]').setValue('Something')
    await wrapper.find('input[inputmode="decimal"]').setValue('10')
    await wrapper.find('form').trigger('submit')
    await settle()

    expect(expenses.expenses.at(-1)?.categoryId).toBeNull()
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
    categoryId: 'c2',
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

    const categories = useCategoriesStore()
    categories.attachApi({
      get: vi.fn(async () => [
        { id: 'c1', key: 'groceries', name: 'Groceries', iconName: 'cart-shopping', colorHex: '#16a34a', sortOrder: 1 },
        { id: 'c2', key: 'dining', name: 'Restaurants', iconName: 'utensils', colorHex: '#f97316', sortOrder: 2 },
      ]),
      post: vi.fn(), patch: vi.fn(), delete: vi.fn(),
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
    expect((wrapper.find('[data-testid="category"]').element as HTMLSelectElement).value)
      .toBe('c2')
  })

  it('keeps an uneven split someone set by hand', async () => {
    const { wrapper } = await mountEdit()

    // Read back out of the stored shares rather than recomputed, or the shares
    // people chose would be lost the moment they opened the form.
    const selected = wrapper
      .findAll('button')
      .filter((button) => button.classes().includes('text-brand-400'))
      .map((button) => button.text())

    expect(selected).toContain('By shares')

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
    useCategoriesStore().attachApi({ get: vi.fn(async () => []), post: vi.fn(), patch: vi.fn(), delete: vi.fn() } as never)
    useExpensesStore().attachSync(new SyncEngine(fakeSyncApi(), () => false))

    const wrapper = mount(ExpenseFormView, { global: { stubs: { RouterLink: RouterLinkStub } } })
    await settle()

    expect(textOf(wrapper)).toContain('not on this device')
  })
})
