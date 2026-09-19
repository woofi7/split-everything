import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { signInForTests, testExpense, testGroup, GROUP_ID } from '../support/viewHarness'
import { db, resetDatabase } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { useGroupsStore } from '@/stores/groups'
import { SyncEngine } from '@/offline/syncEngine'
import { ApiError } from '@/api/client'

const OTHER_GROUP = 'group-2'

function fakeSyncApi(entries: unknown[] = []) {
  return {
    push: vi.fn(async (request: { operations: Array<{ operationId: string; entityId: string; vectorClock: unknown }> }) => ({
      accepted: request.operations.map((operation) => ({
        operationId: operation.operationId,
        entityId: operation.entityId,
        serverSeq: 1,
        vectorClock: operation.vectorClock,
      })),
      conflicts: [],
      rejected: [],
      groupCursors: {},
    })),
    pull: vi.fn(async () => ({
      entries,
      groupCursors: {},
      snapshots: [],
      hasMore: false,
    })),
    acknowledge: vi.fn(async () => {}),
  }
}

describe('moving an expense between groups', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    signInForTests()
    await resetDatabase()
    await db.groups.put(testGroup())
    await db.groups.put(testGroup({ id: OTHER_GROUP, name: 'Ski trip' }))
    await db.expenses.put(testExpense())
  })

  function storeWith(entries: unknown[] = [], post = vi.fn(async () => ({}))) {
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(fakeSyncApi(entries) as never, () => true))
    store.attachApi({ post } as never)

    useGroupsStore().attachApi({ get: vi.fn(async () => []) } as never)

    return { store, post }
  }

  it('asks the server to move it, with the people it could not match', async () => {
    const { store, post } = storeWith()

    await store.transfer('expense-1', OTHER_GROUP, { 'member-bob': 'member-carol' })

    expect(post).toHaveBeenCalledWith('/expenses/expense-1/transfer', {
      targetGroupId: OTHER_GROUP,
      memberMapping: { 'member-bob': 'member-carol' },
    })
  })

  it('sends no mapping at all when the two groups can work it out themselves', async () => {
    const { store, post } = storeWith()

    await store.transfer('expense-1', OTHER_GROUP, {})

    expect(post.mock.calls[0][1]).toEqual({ targetGroupId: OTHER_GROUP, memberMapping: undefined })
  })

  it('takes the moved expense from the sync that follows', async () => {
    const moved = [
      {
        serverSeq: 4,
        groupId: OTHER_GROUP,
        entityType: 'Expense',
        entityId: 'expense-1',
        operation: 'Transfer',
        deviceId: 'device-b',
        payloadJson: JSON.stringify({
          id: 'expense-1',
          groupId: OTHER_GROUP,
          paidByMemberId: 'member-carol',
          description: 'Dinner',
          amount: 60,
          currency: 'CAD',
          amountInBaseCurrency: 60,
          exchangeRate: 1,
          spentAt: '2026-01-05T12:00:00Z',
          splitType: 1,
          splits: [{ memberId: 'member-carol', amount: 60, amountInBaseCurrency: 60 }],
          items: [],
          revision: 2,
          isDeleted: false,
        }),
        vectorClock: { 'device-b': 2 },
        lineageId: 'lineage-2',
        sourceGroupId: GROUP_ID,
        counterpartGroupId: null,
        createdAt: '2026-02-01T12:00:00Z',
      },
    ]

    const { store } = storeWith(moved)

    await store.transfer('expense-1', OTHER_GROUP)

    expect(store.forGroup(GROUP_ID)).toHaveLength(0)
    expect(store.forGroup(OTHER_GROUP)).toHaveLength(1)
    expect(store.forGroup(OTHER_GROUP)[0].paidByMemberId).toBe('member-carol')
  })

  it('refuses to move an expense whose last change has not been sent', async () => {
    await db.expenses.put(testExpense({ description: 'Edited offline', pending: true }))
    const { store, post } = storeWith()

    await expect(store.transfer('expense-1', OTHER_GROUP)).rejects.toThrow('not been sent yet')
    expect(post).not.toHaveBeenCalled()
  })

  it('refuses to move an expense into the group it is already in', async () => {
    const { store, post } = storeWith()

    await expect(store.transfer('expense-1', GROUP_ID)).rejects.toThrow('already in this group')
    expect(post).not.toHaveBeenCalled()
  })

  it('says a move needs a connection rather than repeating a network error', async () => {
    const offline = vi.fn(async () => {
      throw new ApiError(0, 'NetworkError', 'Failed to fetch', true)
    })
    const { store } = storeWith([], offline)

    await expect(store.transfer('expense-1', OTHER_GROUP)).rejects.toThrow('needs a connection')
  })

  it('passes the refusal from the server back to whoever asked', async () => {
    const refused = vi.fn(async () => {
      throw new ApiError(400, 'ValidationException', 'Bob has no match in the destination group.')
    })
    const { store } = storeWith([], refused)

    await expect(store.transfer('expense-1', OTHER_GROUP)).rejects.toThrow('Bob has no match')
  })

  it('knows nothing of an expense this device does not have', async () => {
    const { store } = storeWith()

    await expect(store.transfer('expense-404', OTHER_GROUP)).rejects.toThrow('not on this device')
  })

  it('needs an API client, and says so rather than failing quietly', async () => {
    const store = useExpensesStore()
    store.attachSync(new SyncEngine(fakeSyncApi() as never, () => true))

    await expect(store.transfer('expense-1', OTHER_GROUP)).rejects.toThrow('no API client')
  })

  it('reads the balances of both groups again once it has moved', async () => {
    const { store } = storeWith()
    const groups = useGroupsStore()
    const loadAll = vi.spyOn(groups, 'loadAll')

    await store.transfer('expense-1', OTHER_GROUP)

    expect(loadAll).toHaveBeenCalled()
  })
})
