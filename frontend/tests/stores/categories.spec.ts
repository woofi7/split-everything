import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useCategoriesStore } from '@/stores/categories'

/**
 * Categories.
 *
 * The API has served these from the start and nothing ever asked for them, so
 * every expense the app created was uncategorised and the whole by-category
 * breakdown in stats read "Uncategorised: 100%".
 *
 * Cached locally, because choosing a category is part of adding an expense and
 * adding an expense works offline.
 */

const serverCategories = [
  { id: 'c1', key: 'groceries', name: 'Groceries', iconName: 'cart-shopping', colorHex: '#16a34a', sortOrder: 1 },
  { id: 'c2', key: 'dining', name: 'Restaurants', iconName: 'utensils', colorHex: '#f97316', sortOrder: 2 },
]

function fakeApi(overrides: Record<string, unknown> = {}) {
  return {
    get: vi.fn(async (path: string) => (path === '/categories' ? serverCategories : [])),
    post: vi.fn(async () => null),
    patch: vi.fn(async () => null),
    delete: vi.fn(async () => null),
    ...overrides,
  }
}

function storeWith(api = fakeApi()) {
  const store = useCategoriesStore()
  store.attachApi(api as never)
  return { store, api }
}

describe('categories store', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
  })

  it('loads them from the server', async () => {
    const { store } = storeWith()

    await store.load()

    expect(store.all.map((category) => category.name)).toEqual(['Groceries', 'Restaurants'])
  })

  it('caches them so the next start has them without the network', async () => {
    const { store } = storeWith()
    await store.load()

    setActivePinia(createPinia())
    const { store: reloaded, api } = storeWith(
      fakeApi({ get: vi.fn(async () => { throw new Error('offline') }) }),
    )
    await reloaded.load()

    // Choosing a category is part of adding an expense, and that works offline.
    expect(reloaded.all).toHaveLength(2)
    expect(api.get).toHaveBeenCalled()
  })

  it('keeps the cache when the refresh fails', async () => {
    const { store } = storeWith()
    await store.load()

    store.attachApi(fakeApi({ get: vi.fn(async () => { throw new Error('offline') }) }) as never)
    await store.load()

    expect(store.all).toHaveLength(2)
  })

  it('has nothing rather than throwing on a first load with no network', async () => {
    const { store } = storeWith(
      fakeApi({ get: vi.fn(async () => { throw new Error('offline') }) }),
    )

    await store.load()

    expect(store.all).toEqual([])
  })

  it('keeps the server order', async () => {
    const { store } = storeWith(
      fakeApi({
        get: vi.fn(async () => [serverCategories[1], serverCategories[0]]),
      }),
    )

    await store.load()

    // Sorted by the server, which knows the intended order.
    expect(store.all[0].name).toBe('Restaurants')
  })

  it('finds one by id', async () => {
    const { store } = storeWith()
    await store.load()

    expect(store.byId('c2')?.name).toBe('Restaurants')
    expect(store.byId('nope')).toBeUndefined()
    expect(store.byId(null)).toBeUndefined()
  })

  it('carries the icon name so a category can be drawn', async () => {
    const { store } = storeWith()
    await store.load()

    expect(store.byId('c1')?.iconName).toBe('cart-shopping')
  })

  it('replaces the cache rather than accumulating', async () => {
    const { store } = storeWith()
    await store.load()

    store.attachApi(fakeApi({ get: vi.fn(async () => [serverCategories[0]]) }) as never)
    await store.load()

    expect(store.all).toHaveLength(1)
    expect(await db.categories.count()).toBe(1)
  })

  it('ignores a response that is not a list', async () => {
    const { store } = storeWith(fakeApi({ get: vi.fn(async () => null) }))

    await store.load()

    expect(store.all).toEqual([])
  })
})
