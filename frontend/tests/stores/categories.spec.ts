import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import type { Category } from '@/domain/categories'
import { waitFor } from '../support/viewHarness'

const groupId = 'group-1'

const summary = {
  id: groupId,
  name: 'Roommates',
  baseCurrency: 'CAD',
  colorHex: '#4f46e5',
  isArchived: false,
  myNetBalance: 0,
  memberCount: 2,
  lastActivityAt: null,
}

const categories: Category[] = [
  {
    key: 'groceries',
    name: 'Groceries',
    iconName: 'cart-shopping',
    colorHex: '#16a34a',
    sortOrder: 10,
    keywords: ['metro'],
  },
]

function fakeApi(overrides: Record<string, unknown> = {}) {
  return {
    get: vi.fn(async (path: string) => {
      if (path.endsWith('/categories')) return categories
      if (path === '/groups') return [summary]
      return { ...summary, members: [], sequenceCounter: 1, lineageId: 'lineage-1' }
    }),
    post: vi.fn(),
    patch: vi.fn(),
    put: vi.fn(async () => categories),
    delete: vi.fn(),
    ...overrides,
  }
}

describe('a group’s categories', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
  })

  it('arrives with the group and is kept', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)

    await store.refresh(groupId)
    await waitFor(() => store.categoriesOf(groupId).length > 0)

    expect(store.categoriesOf(groupId)).toHaveLength(1)
    expect((await db.categories.get(groupId))?.categories?.[0].key).toBe('groceries')
  })

  it('is read back from the replica on a cold start', async () => {
    await db.categories.put({ groupId, categories })

    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    expect(store.categoriesOf(groupId)[0].name).toBe('Groceries')
  })

  it('stands when the server cannot be reached', async () => {
    await db.categories.put({ groupId, categories })

    const store = useGroupsStore()
    store.attachApi(
      fakeApi({
        get: vi.fn(async () => {
          throw new Error('offline')
        }),
      }) as never,
    )
    await store.loadAll()

    expect(store.categoriesOf(groupId)).toHaveLength(1)
  })

  it('ignores an answer that is not a list', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi({ get: vi.fn(async () => ({ not: 'a list' })) }) as never)

    await store.loadCategories(groupId)

    expect(store.categoriesOf(groupId)).toEqual([])
  })

  it('saves a list, and knows the group now keeps its own', async () => {
    const api = fakeApi()
    const store = useGroupsStore()
    store.attachApi(api as never)

    expect(store.hasOwnCategories(groupId)).toBe(false)

    await store.setCategories(groupId, [{ name: 'Groceries', keywords: ['metro'] }])

    expect(api.put).toHaveBeenCalledWith(`/groups/${groupId}/categories`, {
      categories: [{ name: 'Groceries', keywords: ['metro'] }],
    })
    expect(store.hasOwnCategories(groupId)).toBe(true)
    expect(store.categoriesOf(groupId)).toHaveLength(1)
  })

  it('clearing the list puts the group back on the server’s', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)

    await store.setCategories(groupId, [{ name: 'Groceries' }])
    await store.setCategories(groupId, [])

    expect(store.hasOwnCategories(groupId)).toBe(false)
  })
})
