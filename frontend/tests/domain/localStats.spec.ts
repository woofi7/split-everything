import { describe, expect, it } from 'vitest'
import { computeStats, type LocalStatsInput } from '@/domain/localStats'

/**
 * The stats screen, worked out from the local replica.
 *
 * Every number on that screen is arithmetic over rows the device already holds, so
 * refusing to show them offline was refusing to add up what it had. These tests
 * pin the rules to the server's: the same totals, the same order of payers in a
 * bucket, and the rounding residue in the same place.
 */

const ALICE = 'member-alice'
const BOB = 'member-bob'

const input = (overrides: Partial<LocalStatsInput> = {}): LocalStatsInput => ({
  currency: 'CAD',
  granularity: 'month',
  myMemberIds: [ALICE],
  members: [
    { id: ALICE, displayName: 'Alice' },
    { id: BOB, displayName: 'Bob' },
  ],
  expenses: [
    {
      groupId: 'g1',
      paidByMemberId: ALICE,
      amountInBaseCurrency: 100,
      spentAt: '2026-01-10T12:00:00Z',
      splits: [
        { memberId: ALICE, amountInBaseCurrency: 50 },
        { memberId: BOB, amountInBaseCurrency: 50 },
      ],
    },
    {
      groupId: 'g1',
      paidByMemberId: BOB,
      amountInBaseCurrency: 60,
      spentAt: '2026-02-03T12:00:00Z',
      splits: [
        { memberId: ALICE, amountInBaseCurrency: 30 },
        { memberId: BOB, amountInBaseCurrency: 30 },
      ],
    },
  ],
  settlements: [],
  ...overrides,
})

describe('the stats worked out on this device', () => {
  it('totals what was spent', () => {
    expect(computeStats(input()).totalSpend).toBe(160)
  })

  it('separates what you owe from what you paid', () => {
    const stats = computeStats(input())

    expect(stats.myShare).toBe(80)
    expect(stats.myPaid).toBe(100)
    expect(stats.expenseCount).toBe(2)
  })

  it('counts every membership that is you', () => {
    // One person can be in several groups, so "me" is a set of memberships.
    const stats = computeStats(input({ myMemberIds: [ALICE, BOB] }))

    expect(stats.myPaid).toBe(160)
    expect(stats.myShare).toBe(160)
  })

  it('buckets spending by month, in order', () => {
    const stats = computeStats(input())

    expect(stats.spendOverTime.map((point) => point.bucket)).toEqual([
      '2026-01-01',
      '2026-02-01',
    ])
    expect(stats.spendOverTime.map((point) => point.amount)).toEqual([100, 60])
  })

  it('buckets by day and by the Monday of a week', () => {
    const daily = computeStats(input({ granularity: 'day' }))
    expect(daily.spendOverTime[0].bucket).toBe('2026-01-10')

    // The tenth of January 2026 is a Saturday, so its week starts on the fifth.
    const weekly = computeStats(input({ granularity: 'week' }))
    expect(weekly.spendOverTime[0].bucket).toBe('2026-01-05')
  })

  it('names who paid within a bucket, largest first', () => {
    const stats = computeStats(
      input({
        expenses: [
          {
            groupId: 'g1',
            paidByMemberId: ALICE,
            amountInBaseCurrency: 30,
            spentAt: '2026-01-10T12:00:00Z',
            splits: [{ memberId: ALICE, amountInBaseCurrency: 30 }],
          },
          {
            groupId: 'g1',
            paidByMemberId: BOB,
            amountInBaseCurrency: 70,
            spentAt: '2026-01-12T12:00:00Z',
            splits: [{ memberId: BOB, amountInBaseCurrency: 70 }],
          },
        ],
      }),
    )

    // Largest first, so a stack does not reshuffle its colours between buckets.
    expect(stats.spendOverTime[0].byMember.map((payer) => payer.memberName)).toEqual([
      'Bob',
      'Alice',
    ])
    expect(stats.spendOverTime[0].expenseCount).toBe(2)
  })

  it('keeps the parts of a bucket summing to its total', () => {
    // Thirds of a cent: rounding each share on its own leaves the parts short, and
    // a stacked bar whose parts do not sum to its total is a lie about both.
    const third = 10 / 3
    const stats = computeStats(
      input({
        expenses: [1, 2, 3].map((day) => ({
          groupId: 'g1',
          paidByMemberId: day === 1 ? ALICE : BOB,
          amountInBaseCurrency: third,
          spentAt: `2026-01-0${day}T12:00:00Z`,
          splits: [{ memberId: ALICE, amountInBaseCurrency: third }],
        })),
      }),
    )

    const point = stats.spendOverTime[0]
    const parts = point.byMember.reduce((sum, payer) => sum + payer.amount, 0)
    expect(Number(parts.toFixed(2))).toBe(point.amount)
  })

  it('states each person paid, owed and where that leaves them', () => {
    const stats = computeStats(input())
    const alice = stats.byMember.find((row) => row.memberId === ALICE)!

    expect(alice.paid).toBe(100)
    expect(alice.owed).toBe(80)
    expect(alice.net).toBe(20)
  })

  it('counts a settlement against what it settled', () => {
    const stats = computeStats(
      input({
        settlements: [{ fromMemberId: BOB, toMemberId: ALICE, amountInBaseCurrency: 20 }],
      }),
    )

    // Bob paid Alice the 20 he was behind by, so both are level.
    expect(stats.byMember.find((row) => row.memberId === ALICE)!.net).toBe(0);
    expect(stats.byMember.find((row) => row.memberId === BOB)!.net).toBe(0)
  })

  it('leaves out somebody who has neither paid nor owed anything', () => {
    const stats = computeStats(
      input({
        members: [
          { id: ALICE, displayName: 'Alice' },
          { id: BOB, displayName: 'Bob' },
          { id: 'member-ghost', displayName: 'Chloe' },
        ],
      }),
    )

    // An empty row per placeholder member is noise, not information.
    expect(stats.byMember.map((row) => row.memberName)).not.toContain('Chloe')
  })

  it('answers something for a group with nothing in it', () => {
    const stats = computeStats(input({ expenses: [], settlements: [] }))

    expect(stats.totalSpend).toBe(0)
    expect(stats.expenseCount).toBe(0)
    expect(stats.spendOverTime).toEqual([])
    expect(stats.byMember).toEqual([])
  })

  it('rounds to the currency it was given', () => {
    const yen = computeStats(
      input({
        currency: 'JPY',
        expenses: [
          {
            groupId: 'g1',
            paidByMemberId: ALICE,
            amountInBaseCurrency: 100.4,
            spentAt: '2026-01-10T12:00:00Z',
            splits: [{ memberId: ALICE, amountInBaseCurrency: 100.4 }],
          },
        ],
      }),
    )

    // Yen has no minor unit, so a fractional total is not a total.
    expect(yen.totalSpend).toBe(100)
  })
})

/**
 * The rounding residue in a bucket's payer split.
 *
 * The mirror of the rule the category split follows, and it had no test of its
 * own: rounding each share independently can leave the parts a cent off the
 * whole, and a stacked bar whose parts do not sum to its total is a lie about
 * both. The largest share absorbs it.
 */
describe('a bucket whose shares do not divide evenly', () => {
  it('puts the odd cent on the largest payer, so the stack fills its bar', () => {
    const stats = computeStats(
      input({
        expenses: [
          {
            groupId: 'g1',
            paidByMemberId: ALICE,
            amountInBaseCurrency: 10.005,
            spentAt: '2026-01-10T12:00:00Z',
            splits: [{ memberId: ALICE, amountInBaseCurrency: 10.005 }],
          },
          {
            groupId: 'g1',
            paidByMemberId: BOB,
            amountInBaseCurrency: 10.005,
            spentAt: '2026-01-11T12:00:00Z',
            splits: [{ memberId: BOB, amountInBaseCurrency: 10.005 }],
          },
        ],
      }),
    )

    const bucket = stats.spendOverTime[0]
    expect(bucket.amount).toBe(20.01)
    // Rounded on their own these come to 20.00, a cent short of the bar above them.
    expect(bucket.byMember.map((member) => member.amount)).toEqual([10.01, 10])
    expect(bucket.byMember.reduce((sum, member) => sum + member.amount, 0))
      .toBeCloseTo(bucket.amount, 2)
  })
})

/**
 * Where the money went, computed on the device.
 *
 * The same shape and the same order the server answers with: the two replace each
 * other on one screen, and a figure that moves when the network arrives is a
 * figure nobody trusts.
 */
describe('spending by category', () => {
  const filed = (categoryKey: string | null, amount: number) => ({
    groupId: 'group-1',
    paidByMemberId: 'member-alice',
    amountInBaseCurrency: amount,
    spentAt: '2026-03-02T12:00:00Z',
    categoryKey,
    splits: [{ memberId: 'member-alice', amountInBaseCurrency: amount }],
  })

  const statsOf = (expenses: ReturnType<typeof filed>[]) =>
    computeStats({
      currency: 'CAD',
      granularity: 'month',
      myMemberIds: ['member-alice'],
      members: [{ id: 'member-alice', displayName: 'Alice' }],
      expenses,
      settlements: [],
    })

  it('totals each category, largest first', () => {
    const stats = statsOf([
      filed('groceries', 60),
      filed('dining', 25),
      filed('groceries', 40),
    ])

    expect(stats.byCategory).toEqual([
      { key: 'groceries', amount: 100, expenseCount: 2 },
      { key: 'dining', amount: 25, expenseCount: 1 },
    ])
  })

  it('keeps what nobody filed rather than dropping it', () => {
    const stats = statsOf([filed('groceries', 60), filed(null, 10)])

    // A breakdown that quietly omits part of the spending is worse than one that
    // admits to it.
    expect(stats.byCategory).toContainEqual({ key: null, amount: 10, expenseCount: 1 })
  })

  it('says nothing about categories when nothing was filed at all', () => {
    expect(statsOf([]).byCategory).toEqual([])
  })

  /**
   * The same cut, bucket by bucket, which is what the line over the bars is drawn
   * from. Without it a category is one number for the whole window, and nobody can
   * tell a category that is creeping up from one that always cost that much.
   */
  describe('inside each bucket', () => {
    const on = (categoryKey: string | null, amount: number, spentAt: string) => ({
      ...filed(categoryKey, amount),
      spentAt,
    })

    it('splits the bucket by what it went on, largest first', () => {
      const stats = statsOf([
        on('groceries', 60, '2026-03-02T12:00:00Z'),
        on('dining', 25, '2026-03-04T12:00:00Z'),
        on('groceries', 40, '2026-04-01T12:00:00Z'),
      ])

      expect(stats.spendOverTime[0].byCategory).toEqual([
        { key: 'groceries', amount: 60 },
        { key: 'dining', amount: 25 },
      ])
      expect(stats.spendOverTime[1].byCategory).toEqual([{ key: 'groceries', amount: 40 }])
    })

    it('keeps the unfiled in the bucket too', () => {
      const stats = statsOf([
        on('groceries', 60, '2026-03-02T12:00:00Z'),
        on(null, 10, '2026-03-03T12:00:00Z'),
      ])

      expect(stats.spendOverTime[0].byCategory).toContainEqual({ key: null, amount: 10 })
    })

    /**
     * The line is drawn against the bar's own height, so a category that was the
     * whole bucket has to reach the top of it rather than stopping a cent short.
     */
    it('adds up to the bucket, to the cent', () => {
      const stats = statsOf([
        on('groceries', 10.005, '2026-03-02T12:00:00Z'),
        on('dining', 10.005, '2026-03-03T12:00:00Z'),
      ])

      const bucket = stats.spendOverTime[0]
      const parts = bucket.byCategory.reduce((sum, row) => sum + row.amount, 0)

      // Rounded each on its own, these two would come to a cent more than the
      // bucket they are inside. Closeness rather than equality only because
      // adding the parts back up in the test is itself floating point.
      expect(parts).toBeCloseTo(bucket.amount, 2)
      expect(bucket.byCategory.map((row) => row.amount)).toEqual([10.01, 10])
    })
  })
})
