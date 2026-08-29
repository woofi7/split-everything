import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'
import { settle } from '../support/viewHarness'

/**
 * A local write has to reach the server on its own.
 *
 * The rest of the store suite calls sync() by hand, which cannot catch a mutation
 * that queues an operation and then nothing ever drains it. Without a trigger here
 * an expense sits marked "waiting" until the app is reloaded, and because a pull
 * skips any row with unsent local work, it stays that way.
 */

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

function fakeSyncApi(behaviour: { failPush?: boolean } = {}) {
  return {
    push: vi.fn(async (request: any) => {
      if (behaviour.failPush) throw new Error('network down')
      return {
        accepted: request.operations.map((operation: any) => ({
          operationId: operation.operationId,
          entityId: operation.entityId,
          serverSeq: 1,
          vectorClock: operation.vectorClock,
        })),
        conflicts: [],
        rejected: [],
        groupCursors: { [groupId]: 1 },
      }
    }),
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

describe('a local write syncs itself', () => {
  let api: ReturnType<typeof fakeSyncApi>

  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    await seedGroup()
  })

  function storeWith(online = true, behaviour: { failPush?: boolean } = {}) {
    api = fakeSyncApi(behaviour)
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(api, () => online))
    return store
  }

  it('pushes a new expense with no explicit sync call', async () => {
    const store = storeWith()

    await store.add(draft)
    await settle()

    expect(api.push).toHaveBeenCalled()
    expect(api.push.mock.calls[0][0].operations).toHaveLength(1)
  })

  it('clears the waiting marker once the push is accepted', async () => {
    const store = storeWith()

    const expense = await store.add(draft)
    await settle()

    expect((await db.expenses.get(expense.id))!.pending).toBe(false)
    expect(store.forGroup(groupId)[0].pending).toBe(false)
    expect(store.pendingCount).toBe(0)
    expect(await db.outbox.count()).toBe(0)
  })

  it('does not make the caller wait for the network', async () => {
    const store = storeWith()
    let pushed = false
    api = fakeSyncApi()
    api.push.mockImplementation(async () => {
      pushed = true
      return { accepted: [], conflicts: [], rejected: [], groupCursors: {} }
    })
    store.attachSync(new SyncEngine(api, () => true))

    // add() resolves on the local write; the push is still to come.
    const expense = await store.add(draft)

    expect(expense.pending).toBe(true)
    expect(pushed).toBe(false)
  })

  it('leaves the change queued when the push fails, without throwing', async () => {
    const store = storeWith(true, { failPush: true })

    const expense = await store.add(draft)
    await settle()

    expect(await db.outbox.count()).toBe(1)
    expect((await db.expenses.get(expense.id))!.pending).toBe(true)
    expect(store.pendingCount).toBe(1)
  })

  it('does not try to push while offline', async () => {
    const store = storeWith(false)

    await store.add(draft)
    await settle()

    expect(api.push).not.toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(1)
  })

  it('pushes an edit', async () => {
    const store = storeWith()
    const expense = await store.add(draft)
    await settle()
    api.push.mockClear()

    await store.edit(expense.id, { description: 'Late dinner' })
    await settle()

    expect(api.push).toHaveBeenCalled()
    expect((await db.expenses.get(expense.id))!.pending).toBe(false)
  })

  it('pushes a delete', async () => {
    const store = storeWith()
    const expense = await store.add(draft)
    await settle()
    api.push.mockClear()

    await store.remove(expense.id)
    await settle()

    expect(api.push).toHaveBeenCalled()
    expect(await db.outbox.count()).toBe(0)
  })

  it('pushes a settlement', async () => {
    const store = storeWith()

    const settlement = await store.settle({
      groupId,
      fromMemberId: bob,
      toMemberId: alice,
      amount: 30,
      currency: 'CAD',
    })
    await settle()

    expect(api.push).toHaveBeenCalled()
    expect((await db.settlements.get(settlement.id))!.pending).toBe(false)
  })

  it('pushes a comment', async () => {
    const store = storeWith()
    const expense = await store.add(draft)
    await settle()
    api.push.mockClear()

    const comment = await store.comment(expense.id, 'Was this the taxi too?', alice)
    await settle()

    expect(api.push).toHaveBeenCalled()
    expect((await db.comments.get(comment.id))!.pending).toBe(false)
  })

  it('does not send the same operation twice when writes come back to back', async () => {
    const store = storeWith()

    await store.add(draft)
    await store.add({ ...draft, description: 'Taxi' })
    await settle()

    const sent = api.push.mock.calls.flatMap((call: any[]) =>
      call[0].operations.map((operation: any) => operation.operationId),
    )

    expect(new Set(sent).size).toBe(sent.length)
    expect(await db.outbox.count()).toBe(0)
  })
})
