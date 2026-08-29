import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase, type LocalExpense } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'
import { settle, waitFor } from '../support/viewHarness'

/**
 * Repairing a replica whose queue and pending markers have drifted apart.
 *
 * A row marked unsent with nothing queued for it is stranded in both directions:
 * no push will ever send it, and a pull skips it precisely because it looks like
 * unsent local work. It reads "waiting to sync" forever. Two ordinary things get
 * a replica into that state, so it needs repairing rather than avoiding.
 */

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

function fakeSyncApi(behaviour: { rejectAll?: boolean } = {}) {
  return {
    push: vi.fn(async (request: any) => ({
      accepted: behaviour.rejectAll
        ? []
        : request.operations.map((operation: any) => ({
            operationId: operation.operationId,
            entityId: operation.entityId,
            serverSeq: 1,
            vectorClock: operation.vectorClock,
          })),
      conflicts: [],
      rejected: behaviour.rejectAll
        ? request.operations.map((operation: any) => ({
            operationId: operation.operationId,
            entityId: operation.entityId,
            reason: 'The payload could not be read as JSON.',
            code: 'InvalidPayload',
          }))
        : [],
      groupCursors: { [groupId]: 1 },
    })),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

async function seedGroup() {
  await db.groups.put({
    id: groupId,
    name: 'Roommates',
    baseCurrency: 'CAD',
    colorHex: '#4f46e5',
    isArchived: false,
    lineageId: 'lineage-1',
    members: [
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
    ],
    myNetBalance: 0,
    totalSpend: 0,
    expenseCount: 0,
    updatedAt: '2026-01-01T00:00:00Z',
  })
}

const strandedExpense = (overrides: Partial<LocalExpense> = {}): LocalExpense => ({
  id: 'expense-stranded',
  groupId,
  paidByMemberId: alice,
  description: 'Dinner',
  amount: 60,
  currency: 'CAD',
  amountInBaseCurrency: 60,
  exchangeRate: 1,
  spentAt: '2026-01-05T12:00:00.000Z',
  categoryId: null,
  splitType: 'Equal',
  receiptId: null,
  notes: null,
  splits: [
    { memberId: alice, amount: 30, amountInBaseCurrency: 30, inputValue: null },
    { memberId: bob, amount: 30, amountInBaseCurrency: 30, inputValue: null },
  ],
  items: [],
  revision: 1,
  isDeleted: false,
  vectorClock: { 'device-a': 1 },
  serverSeq: 0,
  pending: true,
  ...overrides,
})

const draft = {
  groupId,
  paidByMemberId: alice,
  description: 'Dinner',
  amount: 60,
  currency: 'CAD',
  spentAt: new Date('2026-01-05T12:00:00Z'),
  splitType: 'Equal' as const,
  participantIds: [alice, bob],
}

describe('reconcile', () => {
  let api: ReturnType<typeof fakeSyncApi>

  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    await seedGroup()
  })

  function storeWith(behaviour: { rejectAll?: boolean } = {}) {
    api = fakeSyncApi(behaviour)
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(api, () => true))
    return store
  }

  it('sends an expense that was left marked unsent with nothing queued', async () => {
    const store = storeWith()
    await db.expenses.put(strandedExpense())
    await store.hydrate()

    await store.reconcile()
    await waitFor(async () => (await db.expenses.get('expense-stranded'))?.pending === false)

    expect(api.push).toHaveBeenCalled()
    expect((await db.expenses.get('expense-stranded'))!.pending).toBe(false)
  })

  it('retries a change the server refused earlier', async () => {
    // Exactly the state a server-side fix leaves behind: parked, and nothing
    // retries it, so the row reads as waiting forever.
    const store = storeWith({ rejectAll: true })
    await store.add(draft)
    // The store's own counter, not the queue: the queue changes first and the
    // store catches up when the drain rehydrates it.
    await waitFor(() => store.rejectedCount === 1)

    expect(await db.outbox.where('status').equals('rejected').count()).toBe(1)

    // The server now accepts what it refused before.
    api = fakeSyncApi()
    store.attachSync(new SyncEngine(api, () => true))

    await store.reconcile()
    await waitFor(
      () => store.rejectedCount === 0 && store.forGroup(groupId)[0]?.pending === false,
    )

    expect(api.push).toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(0)
  })

  it('parks a change again when the server still refuses it', async () => {
    const store = storeWith({ rejectAll: true })
    await store.add(draft)
    await waitFor(() => store.rejectedCount === 1)

    const pushesBefore = api.push.mock.calls.length
    await store.reconcile()
    // The retry has to reach the server, and the store has to hear the verdict,
    // before its fate means anything.
    await waitFor(() => api.push.mock.calls.length > pushesBefore && store.rejectedCount === 1)

    // Not lost, and not looping: still parked, still visible for attention.
    expect(await db.outbox.where('status').equals('rejected').count()).toBe(1)
  })

  it('sends a stranded settlement and comment too', async () => {
    const store = storeWith()

    await db.settlements.put({
      id: 'settlement-stranded',
      groupId,
      fromMemberId: bob,
      toMemberId: alice,
      amount: 30,
      currency: 'CAD',
      amountInBaseCurrency: 30,
      settledAt: '2026-01-06T00:00:00.000Z',
      note: null,
      isDeleted: false,
      vectorClock: {},
      serverSeq: 0,
      pending: true,
    })
    await db.expenses.put(strandedExpense({ pending: false }))
    await db.comments.put({
      id: 'comment-stranded',
      expenseId: 'expense-stranded',
      groupId,
      authorMemberId: alice,
      parentCommentId: null,
      body: 'Was the taxi in this?',
      createdAt: '2026-01-06T00:00:00.000Z',
      isDeleted: false,
      vectorClock: {},
      pending: true,
    })
    await store.hydrate()

    await store.reconcile()
    await waitFor(
      async () =>
        (await db.settlements.get('settlement-stranded'))?.pending === false &&
        (await db.comments.get('comment-stranded'))?.pending === false,
    )

    expect(api.push).toHaveBeenCalled()
  })

  it('sends a stranded deletion as a deletion', async () => {
    const store = storeWith()
    await db.expenses.put(strandedExpense({ isDeleted: true }))
    await store.hydrate()

    await store.reconcile()
    await waitFor(() => api.push.mock.calls.length > 0)

    const sent = api.push.mock.calls[0][0].operations
    expect(sent).toHaveLength(1)
    expect(sent[0].operation).toBe('Delete')
  })

  it('leaves a confirmed row alone', async () => {
    const store = storeWith()
    await db.expenses.put(strandedExpense({ pending: false }))
    await store.hydrate()

    await store.reconcile()
    await settle()

    expect(api.push).not.toHaveBeenCalled()
  })

  it('does not queue a second operation for a row already waiting', async () => {
    const store = storeWith({ rejectAll: true })
    await store.add(draft)
    await settle()
    await db.outbox.toCollection().modify({ status: 'pending' })

    await store.reconcile()

    expect(await db.outbox.count()).toBe(1)
  })

  it('is safe to run twice', async () => {
    const store = storeWith()
    await db.expenses.put(strandedExpense())
    await store.hydrate()

    await store.reconcile()
    await waitFor(() => store.pendingCount === 0)
    await store.reconcile()
    await settle()

    const sent = api.push.mock.calls.flatMap((call: any[]) =>
      call[0].operations.map((operation: any) => operation.entityId),
    )
    expect(sent).toEqual(['expense-stranded'])
  })
})

describe('discarding a refused change', () => {
  let api: ReturnType<typeof fakeSyncApi>

  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    await seedGroup()
  })

  it('removes a refused create instead of leaving a row that never syncs', async () => {
    api = fakeSyncApi({ rejectAll: true })
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(api, () => true))

    const expense = await store.add(draft)
    await waitFor(() => store.rejectedCount === 1)

    const parked = await db.outbox.where('status').equals('rejected').first()
    await store.discardRejected(parked!.operationId)

    // Nothing on the server ever held this expense, so there is nothing to fall
    // back to: leaving it would strand a row that claims to be waiting.
    expect(await db.expenses.get(expense.id)).toBeUndefined()
    expect(store.forGroup(groupId)).toHaveLength(0)
    expect(store.rejectedCount).toBe(0)
  })

  it('keeps a refused edit but stops it claiming to be unsent', async () => {
    api = fakeSyncApi()
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(api, () => true))

    const expense = await store.add(draft)
    await waitFor(() => store.pendingCount === 0)

    // The edit is refused, the create was not.
    api = fakeSyncApi({ rejectAll: true })
    store.attachSync(new SyncEngine(api, () => true))
    await store.edit(expense.id, { description: 'Late dinner' })
    await waitFor(() => store.rejectedCount === 1)

    const parked = await db.outbox.where('status').equals('rejected').first()
    await store.discardRejected(parked!.operationId)

    // The row stays, so the person keeps seeing the expense; the next pull
    // replaces it with the server's version.
    const stored = await db.expenses.get(expense.id)
    expect(stored).toBeDefined()
    expect(stored!.pending).toBe(false)
    expect(store.rejectedCount).toBe(0)
  })
})
