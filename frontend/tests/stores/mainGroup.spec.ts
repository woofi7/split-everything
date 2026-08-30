import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useGroupsStore } from '@/stores/groups'
import { resetDatabase } from '@/offline/db'

/**
 * The group the app is about.
 *
 * Most people have one group they use constantly and a few they barely touch.
 * Making every screen ask which group turns that common case into a chore, so one
 * group is the main one and the screens follow it.
 *
 * A device preference rather than account state: which group you are looking at is
 * about this screen in your hand, and it must survive a reload without waiting on
 * the network.
 */

const summary = (id: string, name: string, archived = false) => ({
  id,
  name,
  baseCurrency: 'CAD',
  colorHex: '#4f46e5',
  isArchived: archived,
  myNetBalance: 0,
  memberCount: 2,
  lastActivityAt: null,
})

function fakeApi(groups = [summary('g1', 'Roommates'), summary('g2', 'Ski trip')]) {
  return {
    get: vi.fn(async (path: string) => (path === '/groups' ? groups : [])),
    post: vi.fn(async () => groups[0]),
    patch: vi.fn(async () => groups[0]),
    delete: vi.fn(async () => null),
  }
}

function storeWith(api = fakeApi()) {
  const store = useGroupsStore()
  store.attachApi(api as never)
  return { store, api }
}

describe('the main group', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    localStorage.clear()
    await resetDatabase()
  })

  it('picks one on its own the first time', async () => {
    const { store } = storeWith()

    await store.loadAll()

    // Nobody has chosen yet, and asking on arrival would be worse than choosing.
    expect(store.mainGroupId).toBe('g1')
    expect(store.mainGroup?.name).toBe('Roommates')
  })

  it('remembers the one that was chosen', async () => {
    const { store } = storeWith()
    await store.loadAll()

    store.setMainGroup('g2')

    expect(store.mainGroup?.name).toBe('Ski trip')
  })

  it('survives a reload', async () => {
    const { store } = storeWith()
    await store.loadAll()
    store.setMainGroup('g2')

    setActivePinia(createPinia())
    const { store: reloaded } = storeWith()
    reloaded.restoreMainGroup()
    await reloaded.loadAll()

    expect(reloaded.mainGroupId).toBe('g2')
  })

  it('moves on when the chosen group is gone', async () => {
    const { store } = storeWith()
    await store.loadAll()
    store.setMainGroup('g2')

    store.attachApi(fakeApi([summary('g1', 'Roommates')]) as never)
    await store.loadAll()

    // Left pointing at a group that no longer exists, every screen would be empty.
    expect(store.mainGroupId).toBe('g1')
  })

  it('does not settle on an archived group', async () => {
    const { store } = storeWith(
      fakeApi([summary('g1', 'Old flat', true), summary('g2', 'Ski trip')]),
    )
    await store.loadAll()

    expect(store.mainGroupId).toBe('g2')
  })

  it('has no main group when there are none', async () => {
    const { store } = storeWith(fakeApi([]))
    await store.loadAll()

    expect(store.mainGroupId).toBeNull()
    expect(store.mainGroup).toBeUndefined()
  })

  it('refuses a group it does not know', async () => {
    const { store } = storeWith()
    await store.loadAll()

    store.setMainGroup('nonexistent')

    // Better to stay where we are than to point every screen at nothing.
    expect(store.mainGroupId).toBe('g1')
  })

  it('takes the first group as main once one exists', async () => {
    const { store } = storeWith(fakeApi([]))
    await store.loadAll()
    expect(store.mainGroupId).toBeNull()

    store.attachApi(fakeApi() as never)
    await store.loadAll()

    expect(store.mainGroupId).toBe('g1')
  })

  /**
   * Stepping through the groups, which is what a swipe across the screen does.
   *
   * In the order they are listed, so the cycle is one the picker already shows,
   * and wrapping around, so a swipe never does nothing.
   */
  describe('cycling through the groups', () => {
    const three = () =>
      fakeApi([summary('g1', 'Alpha'), summary('g2', 'Beta'), summary('g3', 'Gamma')])

    it('steps to the next one', async () => {
      const { store } = storeWith(three())
      await store.loadAll()

      expect(store.cycleMainGroup(1)).toBe('g2')
      expect(store.mainGroup?.name).toBe('Beta')
    })

    it('steps back to the one before', async () => {
      const { store } = storeWith(three())
      await store.loadAll()
      store.setMainGroup('g3')

      expect(store.cycleMainGroup(-1)).toBe('g2')
    })

    it('comes round to the first from the last', async () => {
      const { store } = storeWith(three())
      await store.loadAll()
      store.setMainGroup('g3')

      expect(store.cycleMainGroup(1)).toBe('g1')
    })

    it('comes round to the last from the first', async () => {
      const { store } = storeWith(three())
      await store.loadAll()

      expect(store.cycleMainGroup(-1)).toBe('g3')
    })

    it('returns to where it started after a full turn', async () => {
      const { store } = storeWith(three())
      await store.loadAll()

      store.cycleMainGroup(1)
      store.cycleMainGroup(1)
      store.cycleMainGroup(1)

      // What makes it usable without counting.
      expect(store.mainGroupId).toBe('g1')
    })

    it('follows the order the groups are listed in', async () => {
      const { store } = storeWith(
        // Outstanding first, whatever their names: that is the listed order, and
        // an order of its own would match nothing on screen.
        fakeApi([
          { ...summary('g1', 'Alpha') },
          { ...summary('g2', 'Beta'), myNetBalance: -20 },
        ]),
      )
      await store.loadAll()

      expect(store.mainGroupId).toBe('g1')
      expect(store.cycleMainGroup(1)).toBe('g2')
      expect(store.cycleMainGroup(1)).toBe('g1')
    })

    it('leaves archived groups out of the cycle', async () => {
      const { store } = storeWith(
        fakeApi([summary('g1', 'Alpha'), summary('g2', 'Old flat', true), summary('g3', 'Gamma')]),
      )
      await store.loadAll()

      expect(store.cycleMainGroup(1)).toBe('g3')
    })

    it('does nothing with a single group', async () => {
      const { store } = storeWith(fakeApi([summary('g1', 'Alpha')]))
      await store.loadAll()

      // Nothing to move to, so nothing should flash up saying it moved.
      expect(store.cycleMainGroup(1)).toBeNull()
      expect(store.mainGroupId).toBe('g1')
    })

    it('does nothing with no groups at all', async () => {
      const { store } = storeWith(fakeApi([]))
      await store.loadAll()

      expect(store.cycleMainGroup(1)).toBeNull()
      expect(store.mainGroupId).toBeNull()
    })

    it('remembers where it got to, like any other choice', async () => {
      const { store } = storeWith(three())
      await store.loadAll()
      store.cycleMainGroup(1)

      setActivePinia(createPinia())
      const { store: reloaded } = storeWith(three())
      reloaded.restoreMainGroup()
      await reloaded.loadAll()

      expect(reloaded.mainGroupId).toBe('g2')
    })
  })
})
