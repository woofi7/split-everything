import { describe, expect, it } from 'vitest'
import {
  compareClocks,
  dominates,
  emptyClock,
  hasUnseenEvents,
  mergeClocks,
  tickClock,
} from '@/domain/vectorClock'

const clock = (entries: Record<string, number>) => ({ ...entries })

describe('vector clock, client side', () => {
  it('starts empty', () => {
    expect(emptyClock()).toEqual({})
  })

  it('ticks only the named device', () => {
    const next = tickClock(clock({ a: 3, b: 7 }), 'a')

    expect(next.a).toBe(4)
    expect(next.b).toBe(7)
  })

  it('starts an unknown device at one', () => {
    expect(tickClock(clock({ a: 3 }), 'new').new).toBe(1)
  })

  it('does not mutate the clock it was given', () => {
    const original = clock({ a: 1 })
    tickClock(original, 'a')

    expect(original.a).toBe(1)
  })

  it.each(['', '   '])('refuses to tick a blank device id (%s)', (deviceId) => {
    expect(() => tickClock(emptyClock(), deviceId)).toThrow()
  })

  it('takes the pointwise maximum when merging', () => {
    const merged = mergeClocks(clock({ a: 5, b: 1 }), clock({ a: 2, b: 9, c: 3 }))

    expect(merged).toEqual({ a: 5, b: 9, c: 3 })
  })

  it('merges commutatively', () => {
    const left = clock({ a: 5, b: 1 })
    const right = clock({ a: 2, c: 8 })

    expect(mergeClocks(left, right)).toEqual(mergeClocks(right, left))
  })

  it.each([
    [{ a: 2, b: 3 }, { b: 3, a: 2 }, 'equal'],
    [{ a: 3, b: 3 }, { a: 2, b: 3 }, 'after'],
    [{ a: 1 }, { a: 2 }, 'before'],
    [{ a: 2, b: 1 }, { a: 1, b: 2 }, 'concurrent'],
    [{ a: 1, z: 1 }, { a: 5 }, 'concurrent'],
    [{}, { a: 1 }, 'before'],
  ])('compares %o against %o as %s', (left, right, expected) => {
    expect(compareClocks(left, right)).toBe(expected)
  })

  it('dominates covers equal and after but not concurrent', () => {
    expect(dominates(clock({ a: 2 }), clock({ a: 2 }))).toBe(true)
    expect(dominates(clock({ a: 3 }), clock({ a: 2 }))).toBe(true)
    expect(dominates(clock({ a: 1 }), clock({ a: 2 }))).toBe(false)
    expect(dominates(clock({ a: 2, b: 1 }), clock({ a: 1, b: 2 }))).toBe(false)
  })

  it('reports unseen events only when the other side is ahead somewhere', () => {
    expect(hasUnseenEvents(clock({ a: 2 }), clock({ a: 3 }))).toBe(true)
    expect(hasUnseenEvents(clock({ a: 2 }), clock({ b: 1 }))).toBe(true)
    expect(hasUnseenEvents(clock({ a: 2 }), clock({ a: 2 }))).toBe(false)
    expect(hasUnseenEvents(clock({ a: 5 }), clock({ a: 2 }))).toBe(false)
  })

  it('converges after two devices edit offline and then sync', () => {
    const shared = clock({ phone: 4, laptop: 2 })
    const phone = tickClock(shared, 'phone')
    const laptop = tickClock(shared, 'laptop')

    expect(compareClocks(phone, laptop)).toBe('concurrent')

    const reconciled = mergeClocks(phone, laptop)
    expect(dominates(reconciled, phone)).toBe(true)
    expect(dominates(reconciled, laptop)).toBe(true)
  })

  it('agrees with the server on what counts as a conflict', () => {
    // Same fixtures as the backend SyncArbiter tests: the client has to reach the
    // same verdict locally, or it would queue an operation it knows will conflict.
    expect(compareClocks(clock({ a: 1 }), clock({ a: 1 }))).toBe('equal')
    expect(compareClocks(clock({ a: 2, b: 1 }), clock({ a: 1, b: 2 }))).toBe('concurrent')
    expect(compareClocks(clock({ a: 2, c: 1 }), clock({ a: 2 }))).toBe('after')
  })
})
