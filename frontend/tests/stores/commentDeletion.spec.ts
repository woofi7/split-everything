import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'
import { signInForTests, waitFor } from '../support/viewHarness'

/**
 * Deleting your own comment.
 *
 * A comment is the one thing in the app written in someone's own words, which
 * makes being unable to take it back worse than for an expense: a wrong amount can
 * be corrected, a sentence cannot be unsaid. The server has always allowed the
 * author to remove one; nothing on the client asked.
 *
 * A tombstone rather than a delete, like everything else here, because a device
 * that is still offline has to learn the comment is gone.
 */

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

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
      groupCursors: { [groupId]: 1 },
    })),
    pull: vi.fn(async () => ({ entries: [], groupCursors: {}, snapshots: [], hasMore: false })),
    acknowledge: vi.fn(async () => {}),
  }
}

async function seed(): Promise<void> {
  await db.groups.put({
    id: groupId,
    name: 'Roommates',
    baseCurrency: 'CAD',
    colorHex: '#4f46e5',
    isArchived: false,
    lineageId: 'l1',
    members: [],
    myNetBalance: 0,
    totalSpend: 0,
    expenseCount: 0,
    updatedAt: '2026-01-01T00:00:00Z',
  })

  await db.expenses.put({
    id: 'expense-1',
    groupId,
    paidByMemberId: alice,
    description: 'Dinner',
    amount: 60,
    currency: 'CAD',
    amountInBaseCurrency: 60,
    exchangeRate: 1,
    spentAt: '2026-01-05T12:00:00.000Z',
    splitType: 'Equal',
    receiptId: null,
    notes: null,
    splits: [],
    items: [],
    revision: 1,
    isDeleted: false,
    vectorClock: {},
    serverSeq: 1,
    pending: false,
  })

  await db.comments.put({
    id: 'comment-1',
    expenseId: 'expense-1',
    groupId,
    authorMemberId: alice,
    parentCommentId: null,
    body: 'Was the taxi in this?',
    createdAt: '2026-01-05T13:00:00.000Z',
    isDeleted: false,
    vectorClock: {},
    pending: false,
  })
}

describe('removing a comment', () => {
  let api: ReturnType<typeof fakeSyncApi>

  beforeEach(async () => {
    setActivePinia(createPinia())
    // The sync path refuses to talk to the server as nobody.
    signInForTests()
    await resetDatabase()
    await seed()
  })

  async function store() {
    api = fakeSyncApi()
    const created = useExpensesStore()
    created.attachSync(new SyncEngine(api, () => true))
    await created.hydrate()
    return created
  }

  it('takes it off the expense straight away', async () => {
    const expenses = await store()

    await expenses.removeComment('comment-1')

    expect(expenses.commentsFor('expense-1')).toHaveLength(0)
  })

  it('tombstones rather than deleting', async () => {
    const expenses = await store()

    await expenses.removeComment('comment-1')

    // A device still offline has to learn it is gone.
    const stored = await db.comments.get('comment-1')
    expect(stored?.isDeleted).toBe(true)
  })

  it('tells the server', async () => {
    const expenses = await store()

    await expenses.removeComment('comment-1')
    await waitFor(() => api.push.mock.calls.length > 0)

    const operation = api.push.mock.calls[0][0].operations[0]
    expect(operation.entityType).toBe('ExpenseComment')
    expect(operation.operation).toBe('Delete')
    expect(operation.entityId).toBe('comment-1')
  })

  it('works offline, queued for later', async () => {
    api = fakeSyncApi()
    const expenses = useExpensesStore()
    expenses.attachSync(new SyncEngine(api, () => false))
    await expenses.hydrate()

    await expenses.removeComment('comment-1')

    expect(api.push).not.toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(1)
    expect(expenses.commentsFor('expense-1')).toHaveLength(0)
  })

  it('says so when the comment is not on this device', async () => {
    const expenses = await store()

    await expect(expenses.removeComment('nope')).rejects.toThrow('not on this device')
  })

  it('is a no-op the second time', async () => {
    const expenses = await store()
    await expenses.removeComment('comment-1')

    await expect(expenses.removeComment('comment-1')).rejects.toThrow()
  })
})
