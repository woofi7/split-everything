import { describe, expect, it } from 'vitest'
import { calculateSplit, calculateItemizedSplit } from '@/domain/splitting'

const alice = '11111111-1111-1111-1111-111111111111'
const bob = '22222222-2222-2222-2222-222222222222'
const carol = '33333333-3333-3333-3333-333333333333'

const members = (...ids: string[]) => ids.map((memberId) => ({ memberId, value: null }))

describe('splitting, offline', () => {
  it('divides an equal split evenly', () => {
    const shares = calculateSplit(90, 'CAD', 'Equal', members(alice, bob, carol))

    expect(shares.every((s) => s.amount === 30)).toBe(true)
  })

  it('always sums to the total, even when it does not divide', () => {
    const shares = calculateSplit(10, 'CAD', 'Equal', members(alice, bob, carol))

    expect(shares.reduce((sum, s) => sum + s.amount, 0)).toBe(10)
    expect(shares.map((s) => s.amount).sort()).toEqual([3.33, 3.33, 3.34])
  })

  it('gives the leftover cent to the same member the server would', () => {
    // Pinned against the identical backend fixture: largest-remainder rounding with
    // a member-id tie-break, so the leftover goes to the lowest id. If the two
    // sides ever disagree, an expense entered offline would be silently rewritten
    // into different amounts than the person was shown.
    const shares = calculateSplit(10, 'CAD', 'Equal', members(carol, bob, alice))
    const byMember = Object.fromEntries(shares.map((s) => [s.memberId, s.amount]))

    expect(byMember[alice]).toBe(3.34)
    expect(byMember[bob]).toBe(3.33)
    expect(byMember[carol]).toBe(3.33)
  })

  it('is independent of the order participants were listed in', () => {
    const forwards = calculateSplit(10, 'CAD', 'Equal', members(alice, bob, carol))
    const backwards = calculateSplit(10, 'CAD', 'Equal', members(carol, bob, alice))

    const normalise = (shares: ReturnType<typeof calculateSplit>) =>
      Object.fromEntries(shares.map((s) => [s.memberId, s.amount]))

    expect(normalise(backwards)).toEqual(normalise(forwards))
  })

  it('never produces fractions in a zero-decimal currency', () => {
    const shares = calculateSplit(10, 'JPY', 'Equal', members(alice, bob, carol))

    expect(shares.every((s) => Number.isInteger(s.amount))).toBe(true)
    expect(shares.reduce((sum, s) => sum + s.amount, 0)).toBe(10)
  })

  it('applies percentages', () => {
    const shares = calculateSplit(200, 'CAD', 'Percentage', [
      { memberId: alice, value: 25 },
      { memberId: bob, value: 75 },
    ])

    expect(shares.find((s) => s.memberId === alice)!.amount).toBe(50)
    expect(shares.find((s) => s.memberId === bob)!.amount).toBe(150)
  })

  it('rejects percentages that do not reach 100', () => {
    expect(() =>
      calculateSplit(100, 'CAD', 'Percentage', [
        { memberId: alice, value: 40 },
        { memberId: bob, value: 40 },
      ]),
    ).toThrow(/100/)
  })

  it('weights by share count', () => {
    const shares = calculateSplit(120, 'CAD', 'Shares', [
      { memberId: alice, value: 1 },
      { memberId: bob, value: 2 },
      { memberId: carol, value: 3 },
    ])

    expect(shares.map((s) => s.amount)).toEqual([20, 40, 60])
  })

  it('keeps the share count as the input value', () => {
    const shares = calculateSplit(120, 'CAD', 'Shares', [
      { memberId: alice, value: 1 },
      { memberId: bob, value: 3 },
    ])

    expect(shares.find((s) => s.memberId === bob)!.inputValue).toBe(3)
  })

  it('rejects an all-zero weighting', () => {
    expect(() =>
      calculateSplit(100, 'CAD', 'Shares', [
        { memberId: alice, value: 0 },
        { memberId: bob, value: 0 },
      ]),
    ).toThrow()
  })

  it('accepts exact amounts that add up', () => {
    const shares = calculateSplit(100, 'CAD', 'ExactAmount', [
      { memberId: alice, value: 30.5 },
      { memberId: bob, value: 69.5 },
    ])

    expect(shares.find((s) => s.memberId === alice)!.amount).toBe(30.5)
  })

  it('rejects exact amounts that miss the total', () => {
    expect(() =>
      calculateSplit(100, 'CAD', 'ExactAmount', [
        { memberId: alice, value: 30 },
        { memberId: bob, value: 60 },
      ]),
    ).toThrow()
  })

  it('rejects an empty participant list', () => {
    expect(() => calculateSplit(10, 'CAD', 'Equal', [])).toThrow()
  })

  it('rejects a duplicated participant', () => {
    expect(() => calculateSplit(10, 'CAD', 'Equal', members(alice, alice))).toThrow()
  })

  it('sends an itemized split through its own entry point', () => {
    expect(() => calculateSplit(10, 'CAD', 'Itemized', members(alice))).toThrow(/item/i)
  })

  it.each([
    [0.01, 3],
    [100.01, 7],
    [9999.99, 11],
    [1, 100],
  ])('an equal split of %f across %i people sums exactly', (total, count) => {
    const ids = Array.from({ length: count }, (_, i) => `member-${i.toString().padStart(4, '0')}`)
    const shares = calculateSplit(total, 'CAD', 'Equal', members(...ids))

    expect(Number(shares.reduce((sum, s) => sum + s.amount, 0).toFixed(2))).toBe(total)
  })
})

describe('itemized splitting', () => {
  it('charges each line to whoever had it', () => {
    const shares = calculateItemizedSplit(
      30,
      'CAD',
      [
        { amount: 20, quantity: 1, memberIds: [alice] },
        { amount: 10, quantity: 1, memberIds: [bob] },
      ],
      [alice, bob],
    )

    expect(shares.find((s) => s.memberId === alice)!.amount).toBe(20)
    expect(shares.find((s) => s.memberId === bob)!.amount).toBe(10)
  })

  it('splits a shared line between its participants', () => {
    const shares = calculateItemizedSplit(
      30,
      'CAD',
      [{ amount: 30, quantity: 1, memberIds: [alice, bob] }],
      [alice, bob],
    )

    expect(shares.every((s) => s.amount === 15)).toBe(true)
  })

  it('multiplies a line by its quantity', () => {
    const shares = calculateItemizedSplit(
      30,
      'CAD',
      [{ amount: 10, quantity: 3, memberIds: [alice] }],
      [alice, bob],
    )

    expect(shares.find((s) => s.memberId === alice)!.amount).toBe(30)
  })

  it('spreads tax and tip in proportion to what each person ordered', () => {
    const shares = calculateItemizedSplit(
      36,
      'CAD',
      [
        { amount: 20, quantity: 1, memberIds: [alice] },
        { amount: 10, quantity: 1, memberIds: [bob] },
      ],
      [alice, bob],
    )

    expect(shares.find((s) => s.memberId === alice)!.amount).toBe(24)
    expect(shares.find((s) => s.memberId === bob)!.amount).toBe(12)
    expect(shares.reduce((sum, s) => sum + s.amount, 0)).toBe(36)
  })

  it('handles a discount that makes the total lower than the items', () => {
    const shares = calculateItemizedSplit(
      27,
      'CAD',
      [
        { amount: 20, quantity: 1, memberIds: [alice] },
        { amount: 10, quantity: 1, memberIds: [bob] },
      ],
      [alice, bob],
    )

    expect(shares.reduce((sum, s) => sum + s.amount, 0)).toBe(27)
  })

  it('falls back to the group for a line with nobody on it', () => {
    const shares = calculateItemizedSplit(
      10,
      'CAD',
      [{ amount: 10, quantity: 1, memberIds: [] }],
      [alice, bob],
    )

    expect(shares.every((s) => s.amount === 5)).toBe(true)
  })

  it('falls back to an equal split with no items at all', () => {
    const shares = calculateItemizedSplit(10, 'CAD', [], [alice, bob])

    expect(shares).toHaveLength(2)
    expect(shares.reduce((sum, s) => sum + s.amount, 0)).toBe(10)
  })

  it('rejects having neither items nor participants', () => {
    expect(() => calculateItemizedSplit(10, 'CAD', [], [])).toThrow()
  })

  it('sums to the total across a long awkward receipt', () => {
    const lines = Array.from({ length: 17 }, (_, i) => ({
      amount: 3.33,
      quantity: 1,
      memberIds: i % 2 === 0 ? [alice, bob] : [carol],
    }))

    const shares = calculateItemizedSplit(71.11, 'CAD', lines, [alice, bob, carol])

    expect(Number(shares.reduce((sum, s) => sum + s.amount, 0).toFixed(2))).toBe(71.11)
  })
})
