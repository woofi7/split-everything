import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { signInForTests, testExpense, testGroup, GROUP_ID } from '../support/viewHarness'
import { db, resetDatabase } from '@/offline/db'
import { useExpensesStore } from '@/stores/expenses'
import { SyncEngine } from '@/offline/syncEngine'

/**
 * Filing many expenses at once.
 *
 * The thing that makes categories usable on a group that already existed: a year
 * of rows filed under nothing, fixed a hundred at a time. It touches one field
 * and recomputes nothing, which is the whole point - a bulk edit that rebuilt
 * splits would quietly rewrite money nobody asked it to.
 */
describe('filing many expenses at once', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    signInForTests()
    await resetDatabase()
    await db.groups.put(testGroup())
  })

  /** Offline, so the queue is left standing where it can be counted. */
  function storeWith() {
    const store = useExpensesStore()
    store.attachSync(new SyncEngine({ push: vi.fn(), pull: vi.fn(), acknowledge: vi.fn() } as never, () => false))
    return store
  }

  async function seed(...expenses: Array<Parameters<typeof testExpense>[0]>) {
    for (const overrides of expenses) await db.expenses.put(testExpense(overrides))
    const store = storeWith()
    await store.hydrate()
    return store
  }

  it('files every one of them under the same category', async () => {
    const store = await seed(
      { id: 'a', description: 'Metro' },
      { id: 'b', description: 'IGA' },
    )

    const filed = await store.refile(['a', 'b'], 'groceries')

    expect(filed).toBe(2)
    expect((await db.expenses.get('a'))?.categoryKey).toBe('groceries')
    expect((await db.expenses.get('b'))?.categoryKey).toBe('groceries')
  })

  it('shows the change on the list without waiting for a round trip', async () => {
    const store = await seed({ id: 'a' })

    await store.refile(['a'], 'groceries')

    expect(store.forGroup(GROUP_ID)[0].categoryKey).toBe('groceries')
    expect(store.forGroup(GROUP_ID)[0].pending).toBe(true)
  })

  it('queues one change per expense, carrying the new category', async () => {
    const store = await seed({ id: 'a' }, { id: 'b' })

    await store.refile(['a', 'b'], 'groceries')

    const queued = await db.outbox.toArray()
    expect(queued).toHaveLength(2)
    expect(queued.every((operation) => operation.operation === 'Update')).toBe(true)
    expect(JSON.parse(queued[0].payloadJson).categoryKey).toBe('groceries')
    expect(await store.unsentCount()).toBe(2)
  })

  /**
   * The reason to skip rather than re-send: selecting a whole month to fix the
   * three that are wrong should queue three operations, not ninety.
   */
  it('leaves alone the ones already filed there', async () => {
    const store = await seed(
      { id: 'a', categoryKey: 'groceries' },
      { id: 'b', categoryKey: null },
    )

    const filed = await store.refile(['a', 'b'], 'groceries')

    expect(filed).toBe(1)
    expect(await db.outbox.count()).toBe(1)
    // And the untouched one keeps the revision it had, so nothing claims an edit
    // that never happened.
    expect((await db.expenses.get('a'))?.revision).toBe(1)
    expect((await db.expenses.get('b'))?.revision).toBe(2)
  })

  it('unfiles them when nothing is chosen', async () => {
    const store = await seed({ id: 'a', categoryKey: 'groceries' })

    expect(await store.refile(['a'], null)).toBe(1)
    expect((await db.expenses.get('a'))?.categoryKey).toBeNull()
  })

  it('changes nothing about the money', async () => {
    const store = await seed({ id: 'a' })
    const before = await db.expenses.get('a')

    await store.refile(['a'], 'groceries')
    const after = await db.expenses.get('a')

    expect(after?.amount).toBe(before?.amount)
    expect(after?.splits).toEqual(before?.splits)
    expect(after?.paidByMemberId).toBe(before?.paidByMemberId)
  })

  it('skips an id this device does not have, rather than failing the lot', async () => {
    const store = await seed({ id: 'a' })

    expect(await store.refile(['ghost', 'a'], 'groceries')).toBe(1)
  })

  it('skips one that has been deleted', async () => {
    const store = await seed({ id: 'a', isDeleted: true })

    expect(await store.refile(['a'], 'groceries')).toBe(0)
    expect(await db.outbox.count()).toBe(0)
  })

  it('queues nothing when there was nothing to change', async () => {
    const store = await seed({ id: 'a', categoryKey: 'groceries' })

    expect(await store.refile(['a'], 'groceries')).toBe(0)
    expect(await db.outbox.count()).toBe(0)
  })

  /**
   * It works off the replica, not off whatever the screen happens to be holding.
   *
   * Filing reaches the store from a list of ids, and a store that had not
   * hydrated would write the change to the database and then show the old row
   * until something else reloaded it.
   */
  it('shows one it did not have in memory yet', async () => {
    await db.expenses.put(testExpense({ id: 'a' }))

    const store = storeWith()
    expect(store.forGroup(GROUP_ID)).toHaveLength(0)

    await store.refile(['a'], 'groceries')

    expect(store.forGroup(GROUP_ID)[0].categoryKey).toBe('groceries')
  })

  it('refuses to file into a group this device does not have', async () => {
    const store = await seed({ id: 'a', groupId: 'somewhere-else' })

    await expect(store.refile(['a'], 'groceries')).rejects.toThrow(/not on this device/)
  })
})
