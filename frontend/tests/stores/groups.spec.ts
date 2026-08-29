import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'

const groupId = 'group-1'
const alice = 'member-alice'
const bob = 'member-bob'

const groupDto = {
  id: groupId,
  name: 'Roommates',
  description: null,
  baseCurrency: 'CAD',
  emojiIcon: null,
  colorHex: '#4f46e5',
  isArchived: false,
  sequenceCounter: 3,
  lineageId: 'lineage-1',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
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
}

function fakeApi(overrides: Record<string, unknown> = {}) {
  return {
    get: vi.fn(async (path: string) => {
      if (path === '/groups') return [{ ...groupDto, memberCount: 2, lastActivityAt: null }]
      if (path.startsWith('/groups/')) return groupDto
      return []
    }),
    post: vi.fn(async () => groupDto),
    patch: vi.fn(async () => ({ ...groupDto, name: 'Renamed' })),
    delete: vi.fn(async () => null),
    ...overrides,
  }
}

describe('groups store', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
  })

  it('loads groups from the server and caches them locally', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)

    await store.loadAll()

    expect(store.groups).toHaveLength(1)
    expect(await db.groups.count()).toBe(1)
  })

  it('shows the cached groups when the server is unreachable', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    setActivePinia(createPinia())
    const offlineStore = useGroupsStore()
    offlineStore.attachApi(
      fakeApi({
        get: vi.fn(async () => {
          throw new Error('offline')
        }),
      }) as never,
    )

    await offlineStore.loadAll()

    // The whole point of the local replica: the group list still renders.
    expect(offlineStore.groups).toHaveLength(1)
    expect(offlineStore.isOffline).toBe(true)
  })

  it('reads a single group from the cache first', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    const group = await store.get(groupId)

    expect(group?.name).toBe('Roommates')
  })

  it('creates a group', async () => {
    const store = useGroupsStore()
    const api = fakeApi()
    store.attachApi(api as never)

    await store.create({ name: 'Roommates', baseCurrency: 'CAD', placeholderMemberNames: ['Bob'] })

    expect(api.post).toHaveBeenCalledWith('/groups', expect.objectContaining({ name: 'Roommates' }))
    expect(store.groups.some((g) => g.id === groupId)).toBe(true)
  })

  it('renames a group', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    await store.update(groupId, { name: 'Renamed' })

    expect(store.groups.find((g) => g.id === groupId)?.name).toBe('Renamed')
  })

  it('archives a group and drops it from the default list', async () => {
    const store = useGroupsStore()
    store.attachApi(
      fakeApi({ post: vi.fn(async () => ({ ...groupDto, isArchived: true })) }) as never,
    )
    await store.loadAll()

    await store.archive(groupId)

    expect(store.visibleGroups).toHaveLength(0)
    expect(store.groups).toHaveLength(1)
  })

  it('shows archived groups when asked', async () => {
    const store = useGroupsStore()
    store.attachApi(
      fakeApi({ post: vi.fn(async () => ({ ...groupDto, isArchived: true })) }) as never,
    )
    await store.loadAll()
    await store.archive(groupId)

    store.includeArchived = true

    expect(store.visibleGroups).toHaveLength(1)
  })

  it('lists members of a group', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    expect(store.membersOf(groupId).map((m) => m.displayName)).toEqual(['Alice', 'Bob'])
  })

  it('knows which member is me', async () => {
    const store = useGroupsStore()
    store.attachApi(fakeApi() as never)
    await store.loadAll()

    expect(store.myMemberId(groupId, 'user-1')).toBe(alice)
    expect(store.myMemberId(groupId, 'someone-else')).toBeNull()
  })

  it('returns no members for a group it does not know', () => {
    const store = useGroupsStore()

    expect(store.membersOf('missing')).toEqual([])
  })

  it('sorts groups with the unsettled ones first', async () => {
    const store = useGroupsStore()
    store.attachApi(
      fakeApi({
        get: vi.fn(async () => [
          { ...groupDto, id: 'settled', name: 'Settled', myNetBalance: 0 },
          { ...groupDto, id: 'owing', name: 'Owing', myNetBalance: -40 },
        ]),
      }) as never,
    )

    await store.loadAll()

    expect(store.visibleGroups[0].name).toBe('Owing')
  })

  it('reports the overall net across groups', async () => {
    const store = useGroupsStore()
    store.attachApi(
      fakeApi({
        get: vi.fn(async () => [
          { ...groupDto, id: 'a', myNetBalance: 50 },
          { ...groupDto, id: 'b', myNetBalance: -20 },
        ]),
      }) as never,
    )

    await store.loadAll()

    expect(store.netAcrossGroups).toBe(30)
  })

  it('surfaces a real server error rather than pretending to be offline', async () => {
    const store = useGroupsStore()
    store.attachApi(
      fakeApi({
        post: vi.fn(async () => {
          throw new Error('Group name is required.')
        }),
      }) as never,
    )

    await expect(store.create({ name: '', baseCurrency: 'CAD' })).rejects.toThrow()
  })
})
