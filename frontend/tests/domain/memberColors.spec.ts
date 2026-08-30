import { describe, expect, it } from 'vitest'
import { MEMBER_COLORS, memberColor, memberColors, textOnColor } from '@/domain/memberColors'

/**
 * Colour per person.
 *
 * Derived from the member id so every device agrees offline and without a column
 * to migrate. That makes two properties worth pinning: the same id always gives
 * the same colour, and two people in one group never share one.
 */

describe('memberColor', () => {
  it('gives the same colour for the same person every time', () => {
    expect(memberColor('member-alice')).toBe(memberColor('member-alice'))
  })

  it('gives different people different colours', () => {
    expect(memberColor('member-alice')).not.toBe(memberColor('member-bob'))
  })

  it('only ever returns a palette colour', () => {
    for (const id of ['a', 'bb', 'ccc', 'member-1', crypto.randomUUID()]) {
      expect(MEMBER_COLORS).toContain(memberColor(id))
    }
  })

  it('has an answer for an empty id', () => {
    expect(MEMBER_COLORS).toContain(memberColor(''))
  })
})

describe('memberColors', () => {
  it('gives everyone in a group a different colour', () => {
    const ids = Array.from({ length: MEMBER_COLORS.length }, (_, i) => `member-${i}`)

    const assigned = memberColors(ids)

    expect(new Set(Object.values(assigned)).size).toBe(ids.length)
  })

  it('keeps a person their own colour when it is free', () => {
    const assigned = memberColors(['member-alice', 'member-bob'])

    expect(assigned['member-alice']).toBe(memberColor('member-alice'))
  })

  it('does not change a person colour when someone else joins later', () => {
    const before = memberColors(['member-alice'])
    const after = memberColors(['member-alice', 'member-bob'])

    // Their colour appears beside their name all over the app; it moving because
    // someone else arrived would be worse than two people sharing a hue.
    expect(after['member-alice']).toBe(before['member-alice'])
  })

  it('still answers for more people than there are colours', () => {
    const ids = Array.from({ length: MEMBER_COLORS.length + 5 }, (_, i) => `m-${i}`)

    const assigned = memberColors(ids)

    expect(Object.keys(assigned)).toHaveLength(ids.length)
    for (const colour of Object.values(assigned)) expect(MEMBER_COLORS).toContain(colour)
  })

  it('has nothing to say about an empty group', () => {
    expect(memberColors([])).toEqual({})
  })
})

describe('textOnColor', () => {
  it('uses dark text on a light colour', () => {
    expect(textOnColor('#eab308')).toBe('#0f172a')
  })

  it('uses light text on a dark colour', () => {
    expect(textOnColor('#6366f1')).toBe('#ffffff')
  })

  it('falls back to light text on something it cannot read', () => {
    expect(textOnColor('nonsense')).toBe('#ffffff')
  })

  it('is readable on every palette colour', () => {
    for (const colour of MEMBER_COLORS) {
      expect(['#0f172a', '#ffffff']).toContain(textOnColor(colour))
    }
  })
})
