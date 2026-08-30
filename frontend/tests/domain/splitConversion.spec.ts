import { describe, expect, it } from 'vitest'
import { calculateSplit, splitValuesFor } from '@/domain/splitting'

/**
 * Carrying a split across a change of type.
 *
 * Switching from equal to percentage used to empty the form: the values meant
 * something different, so nothing was carried and the split became invalid until
 * every box was typed again. Switching type is usually the start of an
 * adjustment, not a reset, so the new values describe the division that was
 * already on screen.
 */

const shares = (...amounts: number[]) =>
  amounts.map((amount, index) => ({ memberId: `m${index}`, amount }))

describe('splitValuesFor', () => {
  it('needs no values for an equal split', () => {
    expect(splitValuesFor('Equal', shares(30, 30), 60)).toEqual({})
  })

  it('turns an even split into fifty fifty percent', () => {
    expect(splitValuesFor('Percentage', shares(30, 30), 60)).toEqual({ m0: 50, m1: 50 })
  })

  it('turns an even split into the amounts themselves', () => {
    expect(splitValuesFor('ExactAmount', shares(30, 30), 60)).toEqual({ m0: 30, m1: 30 })
  })

  it('keeps an uneven division when moving to percentages', () => {
    expect(splitValuesFor('Percentage', shares(42, 18), 60)).toEqual({ m0: 70, m1: 30 })
  })

  it('makes percentages add up to exactly one hundred', () => {
    // Three ways on 100 rounds to 33.33 each, which is 99.99 and refused.
    const values = splitValuesFor('Percentage', shares(33.34, 33.33, 33.33), 100)

    const sum = Object.values(values).reduce((total, value) => total + value, 0)
    expect(sum).toBe(100)
  })

  it('produces percentages the calculator accepts', () => {
    for (const amounts of [[33.34, 33.33, 33.33], [50.01, 49.99], [10, 20, 70], [0.01, 99.99]]) {
      const total = amounts.reduce((sum, amount) => sum + amount, 0)
      const values = splitValuesFor('Percentage', shares(...amounts), total)

      const inputs = Object.entries(values).map(([memberId, value]) => ({ memberId, value }))
      expect(() => calculateSplit(total, 'CAD', 'Percentage', inputs)).not.toThrow()
    }
  })

  it('produces exact amounts the calculator accepts', () => {
    const values = splitValuesFor('ExactAmount', shares(20.01, 19.99, 20), 60)
    const inputs = Object.entries(values).map(([memberId, value]) => ({ memberId, value }))

    expect(() => calculateSplit(60, 'CAD', 'ExactAmount', inputs)).not.toThrow()
  })

  it('keeps the ratio when moving to shares', () => {
    const values = splitValuesFor('Shares', shares(40, 20), 60)

    // Whatever the numbers, the division they describe has to be the same one.
    const inputs = Object.entries(values).map(([memberId, value]) => ({ memberId, value }))
    const result = calculateSplit(60, 'CAD', 'Shares', inputs)

    expect(result.find((s) => s.memberId === 'm0')?.amount).toBe(40)
    expect(result.find((s) => s.memberId === 'm1')?.amount).toBe(20)
  })

  it('has nothing to say when there is no total yet', () => {
    expect(splitValuesFor('Percentage', shares(0, 0), 0)).toEqual({})
    expect(splitValuesFor('ExactAmount', [], 60)).toEqual({})
  })

  it('has nothing to say when the total is negative', () => {
    expect(splitValuesFor('Percentage', shares(30), -60)).toEqual({})
  })

  it('leaves one person with the whole hundred percent', () => {
    expect(splitValuesFor('Percentage', shares(60), 60)).toEqual({ m0: 100 })
  })
})
