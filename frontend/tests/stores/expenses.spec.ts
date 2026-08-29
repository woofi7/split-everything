import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'

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

async function seedGroup(baseCurrency = 'CAD') {
  await db.groups.put({
    id: groupId,
    name: 'Roommates',
    baseCurrency,
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

describe('expenses store', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
    await seedGroup()
  })

  function storeWith(online = true) {
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(fakeSyncApi(), () => online))
    return store
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

  it('adds an expense offline and shows it immediately', async () => {
    const store = storeWith(false)

    const expense = await store.add(draft)

    // The list has to update now, not after a round trip.
    expect(store.forGroup(groupId)).toHaveLength(1)
    expect(expense.pending).toBe(true)
  })

  it('computes the splits locally', async () => {
    const store = storeWith(false)

    const expense = await store.add(draft)

    expect(expense.splits).toHaveLength(2)
    expect(expense.splits.every((split) => split.amount === 30)).toBe(true)
  })

  it('queues the change for sync', async () => {
    const store = storeWith(false)

    await store.add(draft)

    expect(await db.outbox.count()).toBe(1)
  })

  it('persists the expense so it survives a reload', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    expect((await db.expenses.get(expense.id))?.description).toBe('Dinner')
  })

  it('rejects an expense with no amount', async () => {
    const store = storeWith(false)

    await expect(store.add({ ...draft, amount: 0 })).rejects.toThrow()
  })

  it('rejects an expense with no description', async () => {
    const store = storeWith(false)

    await expect(store.add({ ...draft, description: '  ' })).rejects.toThrow()
  })

  it('rejects an expense with no participants', async () => {
    const store = storeWith(false)

    await expect(store.add({ ...draft, participantIds: [] })).rejects.toThrow()
  })

  it('rejects an expense for an unknown group', async () => {
    const store = storeWith(false)

    await expect(store.add({ ...draft, groupId: 'nope' })).rejects.toThrow()
  })

  it('rejects a payer who is not in the group', async () => {
    const store = storeWith(false)

    await expect(store.add({ ...draft, paidByMemberId: 'stranger' })).rejects.toThrow()
  })

  it('applies a percentage split', async () => {
    const store = storeWith(false)

    const expense = await store.add({
      ...draft,
      splitType: 'Percentage',
      splitValues: { [alice]: 25, [bob]: 75 },
    })

    expect(expense.splits.find((s) => s.memberId === bob)?.amount).toBe(45)
  })

  it('applies an itemized split from its lines', async () => {
    const store = storeWith(false)

    const expense = await store.add({
      ...draft,
      amount: 30,
      splitType: 'Itemized',
      items: [
        { description: 'Starter', amount: 10, quantity: 1, memberIds: [bob], sortOrder: 0 },
        { description: 'Main', amount: 20, quantity: 1, memberIds: [alice], sortOrder: 1 },
      ],
    })

    expect(expense.splits.find((s) => s.memberId === bob)?.amount).toBe(10)
  })

  it('marks a foreign currency expense for conversion on the server', async () => {
    const store = storeWith(false)

    const expense = await store.add({ ...draft, currency: 'EUR' })

    // The client does not know the rate; the server freezes it on arrival.
    expect(expense.currency).toBe('EUR')
    expect(expense.exchangeRate).toBe(1)
    expect(expense.amountInBaseCurrency).toBe(expense.amount)
  })

  it('edits an expense and bumps its revision', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    const updated = await store.edit(expense.id, { description: 'Dinner out' })

    expect(updated.description).toBe('Dinner out')
    expect(updated.revision).toBe(2)
  })

  it('recomputes the splits when the amount changes', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    const updated = await store.edit(expense.id, { amount: 40 })

    expect(updated.splits.every((split) => split.amount === 20)).toBe(true)
  })

  it('deletes an expense as a tombstone', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    await store.remove(expense.id)

    expect(store.forGroup(groupId)).toHaveLength(0)
    expect((await db.expenses.get(expense.id))?.isDeleted).toBe(true)
  })

  it('queues a delete for sync', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)
    await db.outbox.clear()

    await store.remove(expense.id)

    expect(await db.outbox.count()).toBe(1)
  })

  it('lists a group expenses newest first', async () => {
    const store = storeWith(false)
    await store.add({ ...draft, description: 'Older', spentAt: new Date('2026-01-01T00:00:00Z') })
    await store.add({ ...draft, description: 'Newer', spentAt: new Date('2026-02-01T00:00:00Z') })

    expect(store.forGroup(groupId).map((e) => e.description)).toEqual(['Newer', 'Older'])
  })

  it('computes the group balance locally from what it has', async () => {
    const store = storeWith(false)
    await store.add(draft)

    const balance = store.balanceFor(groupId)

    expect(balance.find((b) => b.memberId === alice)?.net).toBe(30)
    expect(balance.find((b) => b.memberId === bob)?.net).toBe(-30)
  })

  it('offers a simplified settle-up plan locally', async () => {
    const store = storeWith(false)
    await store.add(draft)

    const plan = store.settleUpPlan(groupId)

    expect(plan).toEqual([{ fromMemberId: bob, toMemberId: alice, amount: 30 }])
  })

  it('excludes a deleted expense from the balance', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)
    await store.remove(expense.id)

    expect(store.balanceFor(groupId).every((b) => b.net === 0)).toBe(true)
  })

  it('pushes the queue when online', async () => {
    const store = storeWith(true)
    await store.add(draft)

    await store.sync()

    expect(await db.outbox.count()).toBe(0)
    expect(store.forGroup(groupId)[0].pending).toBe(false)
  })

  it('reports how many changes are waiting', async () => {
    const store = storeWith(false)
    await store.add(draft)
    await store.add({ ...draft, description: 'Another' })

    await store.refreshPendingCount()

    expect(store.pendingCount).toBe(2)
  })

  it('adds a comment offline', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    await store.comment(expense.id, 'Was this the taxi?', alice)

    expect(store.commentsFor(expense.id)).toHaveLength(1)
    expect(await db.outbox.count()).toBe(2)
  })

  it('rejects an empty comment', async () => {
    const store = storeWith(false)
    const expense = await store.add(draft)

    await expect(store.comment(expense.id, '   ', alice)).rejects.toThrow()
  })

  it('records a settlement offline', async () => {
    const store = storeWith(false)
    await store.add(draft)

    await store.settle({ groupId, fromMemberId: bob, toMemberId: alice, amount: 30, currency: 'CAD' })

    expect(store.balanceFor(groupId).every((b) => Math.abs(b.net) < 0.01)).toBe(true)
  })

  it('rejects a settlement between the same member', async () => {
    const store = storeWith(false)

    await expect(
      store.settle({ groupId, fromMemberId: alice, toMemberId: alice, amount: 10, currency: 'CAD' }),
    ).rejects.toThrow()
  })

  it('rejects a settlement with no amount', async () => {
    const store = storeWith(false)

    await expect(
      store.settle({ groupId, fromMemberId: bob, toMemberId: alice, amount: 0, currency: 'CAD' }),
    ).rejects.toThrow()
  })

  it('loads from the local replica on start', async () => {
    const first = storeWith(false)
    await first.add(draft)

    setActivePinia(createPinia())
    const revived = useExpensesStore()
    revived.attachSync(new SyncEngine(fakeSyncApi(), () => false))
    await revived.hydrate()

    expect(revived.forGroup(groupId)).toHaveLength(1)
  })
})
