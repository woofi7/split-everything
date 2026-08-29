import { flushPromises, mount, RouterLinkStub, type VueWrapper } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { vi } from 'vitest'
import type { Component } from 'vue'
import {
  db,
  resetDatabase,
  type LocalComment,
  type LocalConflict,
  type LocalExpense,
  type LocalGroup,
  type LocalSettlement,
  type OutboxOperation,
} from '@/offline/db'
import { setApiClient } from '@/api/provider'
import { SyncEngine } from '@/offline/syncEngine'
import { useAuthStore } from '@/stores/auth'
import { useGroupsStore } from '@/stores/groups'
import { useExpensesStore } from '@/stores/expenses'

export const GROUP_ID = 'group-1'
export const ALICE = 'member-alice'
export const BOB = 'member-bob'
export const USER_ID = 'user-1'

/**
 * Lets pending microtasks and IndexedDB transactions finish.
 *
 * fake-indexeddb resolves its transactions on a macrotask, so a microtask flush
 * alone never sees the mounted state of a view that reads the local replica.
 */
/**
 * Waits for a condition rather than a fixed number of turns.
 *
 * A background drain crosses several macrotasks and fake-indexeddb resolves on a
 * macrotask, so counting turns is a race that only shows up under load.
 */
export async function waitFor(
  condition: () => boolean | Promise<boolean>,
  turns = 200,
): Promise<void> {
  for (let i = 0; i < turns; i++) {
    if (await condition()) return
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 0))
  }

  throw new Error('Timed out waiting for the expected state.')
}

export async function settle(turns = 5): Promise<void> {
  for (let i = 0; i < turns; i++) {
    await flushPromises()
    await new Promise((resolve) => setTimeout(resolve, 0))
  }
}

export const testGroup = (overrides: Partial<LocalGroup> = {}): LocalGroup => ({
  id: GROUP_ID,
  name: 'Roommates',
  description: null,
  baseCurrency: 'CAD',
  iconName: null,
  colorHex: '#4f46e5',
  isArchived: false,
  lineageId: 'lineage-1',
  members: [
    {
      id: ALICE,
      userId: USER_ID,
      displayName: 'Alice',
      avatarUrl: null,
      role: 'Owner',
      status: 'Active',
      isPlaceholder: false,
      netBalance: 0,
    },
    {
      id: BOB,
      userId: null,
      displayName: 'Bob',
      avatarUrl: null,
      role: 'Member',
      status: 'Active',
      isPlaceholder: true,
      netBalance: 0,
    },
  ],
  myNetBalance: 0,
  totalSpend: 0,
  expenseCount: 0,
  updatedAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

export const testExpense = (overrides: Partial<LocalExpense> = {}): LocalExpense => ({
  id: 'expense-1',
  groupId: GROUP_ID,
  paidByMemberId: ALICE,
  description: 'Dinner',
  amount: 60,
  currency: 'CAD',
  amountInBaseCurrency: 60,
  exchangeRate: 1,
  spentAt: '2026-01-05T12:00:00Z',
  categoryId: null,
  splitType: 'Equal',
  receiptId: null,
  notes: null,
  splits: [
    { memberId: ALICE, amount: 30, amountInBaseCurrency: 30, inputValue: null },
    { memberId: BOB, amount: 30, amountInBaseCurrency: 30, inputValue: null },
  ],
  items: [],
  revision: 1,
  isDeleted: false,
  vectorClock: { 'device-a': 1 },
  serverSeq: 1,
  pending: false,
  ...overrides,
})

export const testConflict = (overrides: Partial<LocalConflict> = {}): LocalConflict => ({
  conflictId: 'conflict-1',
  groupId: GROUP_ID,
  entityType: 'Expense',
  entityId: 'expense-1',
  storedPayloadJson: JSON.stringify({ description: 'Server version' }),
  incomingPayloadJson: JSON.stringify({ description: 'My version' }),
  conflictingFields: ['description'],
  detectedAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

export const testRejectedOperation = (
  overrides: Partial<OutboxOperation> = {},
): OutboxOperation => ({
  operationId: 'operation-1',
  entityType: 'Expense',
  entityId: 'expense-2',
  operation: 'Create',
  groupId: GROUP_ID,
  payloadJson: '{}',
  vectorClock: {},
  clientTimestamp: '2026-01-01T00:00:00Z',
  sequence: 1,
  status: 'rejected',
  attempts: 1,
  lastError: 'InvalidPayload: An expense needs a description.',
  ...overrides,
})

export const testUser = {
  id: USER_ID,
  email: 'alice@example.com',
  displayName: 'Alice',
  avatarUrl: null,
  defaultCurrency: 'CAD',
  prefersLightTheme: false,
}

export interface FakeApi {
  get: ReturnType<typeof vi.fn>
  post: ReturnType<typeof vi.fn>
  patch: ReturnType<typeof vi.fn>
  put: ReturnType<typeof vi.fn>
  delete: ReturnType<typeof vi.fn>
  blob: ReturnType<typeof vi.fn>
  upload: ReturnType<typeof vi.fn>
}

/**
 * An API that answers the reads a view makes, and records the writes.
 *
 * Patterns are matched longest first, so a specific route is never shadowed by a
 * shorter prefix. Insertion order would make '/groups' swallow
 * '/groups/x/invites' and let a test pass against the wrong response.
 */
export function fakeApi(routes: Record<string, unknown> = {}): FakeApi {
  const patterns = Object.keys(routes).sort((left, right) => right.length - left.length)

  const answer = (path: string) => {
    for (const pattern of patterns) {
      if (path === pattern || path.startsWith(pattern)) {
        const value = routes[pattern]
        return typeof value === 'function' ? (value as () => unknown)() : value
      }
    }
    return null
  }

  return {
    get: vi.fn(async (path: string) => answer(path)),
    post: vi.fn(async (path: string) => answer(path)),
    patch: vi.fn(async (path: string) => answer(path)),
    put: vi.fn(async (path: string) => answer(path)),
    delete: vi.fn(async (path: string) => answer(path)),
    blob: vi.fn(async () => new Blob(['bytes'], { type: 'image/png' })),
    upload: vi.fn(async (path: string) => answer(path)),
  }
}

export function fakeSyncApi() {
  return {
    push: vi.fn(
      async (request: {
        operations: Array<{ operationId: string; entityId: string; vectorClock: unknown }>
      }) => ({
        accepted: request.operations.map((operation) => ({
          operationId: operation.operationId,
          entityId: operation.entityId,
          serverSeq: 1,
          vectorClock: operation.vectorClock,
        })),
        conflicts: [],
        rejected: [],
        groupCursors: {},
      }),
    ),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

export interface MountViewOptions {
  api?: FakeApi
  groups?: LocalGroup[]
  expenses?: LocalExpense[]
  settlements?: LocalSettlement[]
  comments?: LocalComment[]
  conflicts?: LocalConflict[]
  outbox?: OutboxOperation[]
  signedIn?: boolean
  /** Whose device this is, as restore() would have set it before any view mounts. */
  rememberedAccount?: { email: string; displayName: string; avatarUrl: string | null }
  online?: boolean
}

export interface MountedView {
  wrapper: VueWrapper
  api: FakeApi
  auth: ReturnType<typeof useAuthStore>
  groupsStore: ReturnType<typeof useGroupsStore>
  expensesStore: ReturnType<typeof useExpensesStore>
}

/**
 * Mounts a view over the real stores and the real local replica, with only the
 * API faked. That keeps the tests honest about what the view does with real store
 * state, rather than asserting against a mocked store.
 */
export async function mountView(
  component: Component,
  options: MountViewOptions = {},
): Promise<MountedView> {
  setActivePinia(createPinia())
  localStorage.clear()
  await resetDatabase()

  // Seeded before mount, because a view reads the local replica in onMounted.
  const groups = options.groups ?? [testGroup()]
  for (const group of groups) await db.groups.put(group)
  for (const expense of options.expenses ?? []) await db.expenses.put(expense)
  for (const settlement of options.settlements ?? []) await db.settlements.put(settlement)
  for (const comment of options.comments ?? []) await db.comments.put(comment)
  for (const conflict of options.conflicts ?? []) await db.conflicts.put(conflict)
  for (const operation of options.outbox ?? []) await db.outbox.put(operation)

  const api = options.api ?? fakeApi()
  setApiClient(api as never)

  const auth = useAuthStore()
  if (options.signedIn !== false) {
    auth.user = testUser as never
    auth.tokens = {
      accessToken: 'access-1',
      accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
      refreshToken: 'refresh-1',
      refreshTokenExpiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    } as never
  }
  if (options.rememberedAccount) auth.rememberedAccount = options.rememberedAccount
  auth.attachApi(api as never)

  const groupsStore = useGroupsStore()
  groupsStore.attachApi(api as never)

  const expensesStore = useExpensesStore()
  expensesStore.attachSync(new SyncEngine(fakeSyncApi(), () => options.online ?? false))

  const wrapper = mount(component, {
    global: {
      stubs: {
        RouterLink: RouterLinkStub,
        // The icon picker teleports to the body to escape any clipping parent.
        // Stubbing it renders inline, which is what lets the wrapper query it.
        teleport: true,
      },
    },
  })

  await settle()

  return { wrapper, api, auth, groupsStore, expensesStore }
}

/** Reads the text of the whole view, collapsed for readable assertions. */
export function textOf(wrapper: VueWrapper): string {
  return wrapper.text().replace(/\s+/g, ' ')
}
