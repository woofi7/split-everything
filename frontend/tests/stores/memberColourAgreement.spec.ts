import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import { memberColor, memberColors } from '@/domain/memberColors'
import { testGroup } from '../support/viewHarness'

/**
 * Every screen agreeing on who is which colour.
 *
 * The palette resolves a clash by walking to the next free colour in the order it
 * is given. So the answer depends on the list: hand it a subset, or the same
 * people in another order, and somebody comes out a different colour. Each screen
 * used to build its own list, which is why the activity feed and the charts
 * disagreed with the expense cards.
 */
describe('the colour of a member', () => {
  beforeEach(async () => {
    setActivePinia(createPinia())
    await resetDatabase()
  })

  async function seed(memberIds: string[]) {
    const group = {
      ...testGroup(),
      members: memberIds.map((id, index) => ({
        ...testGroup().members[0],
        id,
        userId: `user-${index}`,
        displayName: `Person ${index}`,
      })),
    }
    await db.groups.put(group)
    const store = useGroupsStore()
    await store.hydrate?.()
    store.groups.push(group as never)
    return { store, group }
  }

  it('comes from the roster, whoever is asking', async () => {
    const ids = ['member-a', 'member-b', 'member-c']
    const { store, group } = await seed(ids)

    expect(store.colorsOf(group.id)).toEqual(memberColors(ids))
  })

  it('holds even for a person the palette had to nudge', async () => {
    // Two ids that want the same colour, so the second one gets nudged. Found
    // rather than hard-coded, because the hash is free to change.
    const clashing = (() => {
      for (let i = 0; i < 500; i++) {
        for (let j = i + 1; j < 500; j++) {
          const first = `member-${i}`
          const second = `member-${j}`
          if (memberColor(first) === memberColor(second)) return [first, second]
        }
      }
      throw new Error('no colliding pair found')
    })()

    const { store, group } = await seed(clashing)
    const colours = store.colorsOf(group.id)

    // Alone, the second would keep its derived colour. In the roster it moves.
    expect(memberColors([clashing[1]])[clashing[1]]).toBe(memberColor(clashing[1]))
    expect(colours[clashing[1]]).not.toBe(memberColor(clashing[1]))

    // Which is exactly why one list has to decide: the feed used to pass whoever
    // had acted, so a nudged person came out the other colour there.
    expect(colours[clashing[0]]).toBe(memberColor(clashing[0]))
    expect(colours[clashing[0]]).not.toBe(colours[clashing[1]])
  })

  it('gives everyone in the group a colour of their own', async () => {
    const ids = Array.from({ length: 8 }, (_, index) => `member-${index}`)
    const { store, group } = await seed(ids)

    const colours = Object.values(store.colorsOf(group.id))
    expect(new Set(colours).size).toBe(ids.length)
  })

  it('answers with nothing for a group it does not know', async () => {
    const { store } = await seed(['member-a'])

    expect(store.colorsOf('no-such-group')).toEqual({})
  })
})
