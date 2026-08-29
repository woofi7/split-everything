import { beforeEach, describe, expect, it, vi } from 'vitest'
import { flushPromises, mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AddExpenseView from '@/views/AddExpenseView.vue'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'
import { useAuthStore } from '@/stores/auth'
import { SyncEngine } from '@/offline/syncEngine'

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

const push = vi.fn()
const replace = vi.fn()

vi.mock('vue-router', () => ({
  useRoute: () => ({ params: {}, query: {}, fullPath: '/add' }),
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

  const wrapper = mount(AddExpenseView, {
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

describe('AddExpenseView', () => {
  beforeEach(() => {
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
