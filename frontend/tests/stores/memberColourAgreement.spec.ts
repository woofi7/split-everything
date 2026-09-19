import { beforeEach, describe, expect, it } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { db, resetDatabase } from '@/offline/db'
import { useGroupsStore } from '@/stores/groups'
import { memberColor, memberColors } from '@/domain/memberColors'
import { testGroup } from '../support/viewHarness'

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

    expect(memberColors([clashing[1]])[clashing[1]]).toBe(memberColor(clashing[1]))
    expect(colours[clashing[1]]).not.toBe(memberColor(clashing[1]))

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
