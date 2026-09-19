import { describe, expect, it } from 'vitest'
import { readSwipe } from '@/domain/swipe'

const travel = (dx: number, dy: number, elapsedMs = 200) => ({ dx, dy, elapsedMs })

describe('reading a swipe', () => {
  it('reads a drag to the left as left', () => {
    expect(readSwipe(travel(-120, 5))).toBe('left')
  })

  it('reads a drag to the right as right', () => {
    expect(readSwipe(travel(120, -5))).toBe('right')
  })

  it('ignores a tap that barely moved', () => {
    expect(readSwipe(travel(-8, 2))).toBeNull()
  })

  it('ignores a press that drifted, which is the same finger not meaning it', () => {
    expect(readSwipe(travel(-40, 6))).toBeNull()
  })

  it('ignores a scroll, however far it drifted sideways', () => {
    expect(readSwipe(travel(-70, 400))).toBeNull()
  })

  it('ignores a diagonal, which could have meant either', () => {
    expect(readSwipe(travel(-100, 100))).toBeNull()
  })

  it('reads a shallow drift down as a swipe, since a thumb arcs', () => {
    expect(readSwipe(travel(-140, 60))).toBe('left')
  })

  it('ignores a finger that rested and then wandered off', () => {
    expect(readSwipe(travel(-200, 10, 4000))).toBeNull()
  })

  it('allows a swipe to be unhurried', () => {
    expect(readSwipe(travel(-200, 10, 900))).toBe('left')
  })

  it('ignores a gesture that went nowhere at all', () => {
    expect(readSwipe(travel(0, 0, 0))).toBeNull()
  })
})
