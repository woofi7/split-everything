import { describe, expect, it } from 'vitest'
import { summariseMonths, type MonthSpend } from '@/domain/monthSummary'

const alice = 'member-alice'
const bob = 'member-bob'

const spend = (
  key: string,
  amount: number,
  description = 'Dinner',
  payers: Array<[string, number]> = [[alice, amount]],
): MonthSpend => ({
  key,
  amountInBaseCurrency: amount,
  description,
  payers: payers.map(([memberId, paid]) => ({ memberId, amountInBaseCurrency: paid })),
})

const inSeptember = new Date(2026, 8, 15)

describe('summarising a month', () => {
  it('totals each month and counts what is in it', () => {
    const summaries = summariseMonths(
      [spend('2026-07-01', 40), spend('2026-07-01', 60), spend('2026-08-01', 25)],
      'CAD',
      inSeptember,
    )

    expect(summaries.map((month) => [month.key, month.total, month.count])).toEqual([
      ['2026-08-01', 25, 1],
      ['2026-07-01', 100, 2],
    ])
  })

  it('describes the month that has not finished, and leads with it', () => {
    const summaries = summariseMonths(
      [spend('2026-09-01', 500), spend('2026-08-01', 25)],
      'CAD',
      inSeptember,
    )

    expect(summaries.map((month) => month.key)).toEqual(['2026-09-01', '2026-08-01'])
    expect(summaries[0].total).toBe(500)
  })

  it('compares the month in progress with nothing, because it is not finished', () => {
    const [september] = summariseMonths(
      [spend('2026-09-01', 500), spend('2026-08-01', 25), spend('2026-07-01', 30)],
      'CAD',
      inSeptember,
    )

    expect(september.versusPrevious).toBeNull()
    expect(september.versusAverage).toBeNull()
  })

  it('keeps the month in progress out of everybody else\'s average', () => {
    const [, august] = summariseMonths(
      [spend('2026-09-01', 5000), spend('2026-08-01', 100), spend('2026-07-01', 50)],
      'CAD',
      inSeptember,
    )

    expect(august.versusAverage).toBe(50)
  })

  it('splits the total by who paid it, largest first', () => {
    const summaries = summariseMonths(
      [
        spend('2026-08-01', 90, 'Rent', [[alice, 90]]),
        spend('2026-08-01', 30, 'Beer', [[bob, 30]]),
        spend('2026-08-01', 65, 'Pans', [[alice, 40], [bob, 25]]),
      ],
      'CAD',
      inSeptember,
    )

    expect(summaries[0].byMember).toEqual([
      { memberId: alice, amount: 130 },
      { memberId: bob, amount: 55 },
    ])
  })

  it('names the biggest single expense', () => {
    const summaries = summariseMonths(
      [
        spend('2026-08-01', 40, 'Groceries'),
        spend('2026-08-01', 1500, 'Rent'),
        spend('2026-08-01', 90, 'Dinner out'),
      ],
      'CAD',
      inSeptember,
    )

    expect(summaries[0].biggest).toEqual({ description: 'Rent', amount: 1500 })
  })

  it('compares a month with the one before it', () => {
    const summaries = summariseMonths(
      [spend('2026-07-01', 200), spend('2026-08-01', 250)],
      'CAD',
      inSeptember,
    )

    expect(summaries[0].versusPrevious).toEqual({
      difference: 50,
      percent: 25,
      label: '2026-07-01',
    })
  })

  it('says nothing about the previous month for the earliest one', () => {
    const summaries = summariseMonths([spend('2026-08-01', 250)], 'CAD', inSeptember)

    expect(summaries[0].versusPrevious).toBeNull()
  })

  it('leaves out the percentage when the month before was zero', () => {
    const summaries = summariseMonths(
      [spend('2026-07-01', 0), spend('2026-08-01', 80)],
      'CAD',
      inSeptember,
    )

    expect(summaries[0].versusPrevious?.difference).toBe(80)
    expect(summaries[0].versusPrevious?.percent).toBeNull()
  })

  it('compares a month with the average of the others', () => {
    const summaries = summariseMonths(
      [spend('2026-06-01', 100), spend('2026-07-01', 200), spend('2026-08-01', 600)],
      'CAD',
      inSeptember,
    )

    expect(summaries[0].versusAverage).toBe(450)
    expect(summaries[2].versusAverage).toBe(-300)
  })

  it('says nothing about the average when there is only one month', () => {
    const summaries = summariseMonths([spend('2026-08-01', 250)], 'CAD', inSeptember)

    expect(summaries[0].versusAverage).toBeNull()
  })

  it('does not count a month with no expenses as a month of zero', () => {
    const summaries = summariseMonths(
      [spend('2026-06-01', 300), spend('2026-08-01', 300)],
      'CAD',
      inSeptember,
    )

    expect(summaries.map((month) => month.key)).toEqual(['2026-08-01', '2026-06-01'])
    expect(summaries[0].versusAverage).toBe(0)
  })

  describe('names the group asked to leave out', () => {
    const withRent = [
      spend('2026-08-01', 1500, 'Loyer aout'),
      spend('2026-08-01', 167, 'Groceries at Metro'),
      spend('2026-08-01', 90, 'Dinner out'),
    ]

    it('picks the biggest of what is left', () => {
      const [august] = summariseMonths(withRent, 'CAD', inSeptember, ['Loyer'])

      expect(august.biggest).toEqual({ description: 'Groceries at Metro', amount: 167 })
    })

    it('totals what is left, and counts only that', () => {
      const [august] = summariseMonths(withRent, 'CAD', inSeptember, ['Loyer'])

      expect(august.total).toBe(257)
      expect(august.count).toBe(2)
    })

    it('says what was left out rather than dropping it quietly', () => {
      const [august] = summariseMonths(withRent, 'CAD', inSeptember, ['Loyer'])

      expect(august.ignored).toEqual({ total: 1500, count: 1 })
      expect(august.total + august.ignored!.total).toBe(1757)
    })

    it('leaves it out of who paid what, so the shares add up to the total', () => {
      const [august] = summariseMonths(
        [
          spend('2026-08-01', 1500, 'Loyer aout', [[alice, 1500]]),
          spend('2026-08-01', 60, 'Groceries', [[alice, 60]]),
          spend('2026-08-01', 40, 'Dinner', [[bob, 40]]),
        ],
        'CAD',
        inSeptember,
        ['Loyer'],
      )

      expect(august.byMember).toEqual([
        { memberId: alice, amount: 60 },
        { memberId: bob, amount: 40 },
      ])
      expect(august.byMember.reduce((sum, member) => sum + member.amount, 0)).toBe(august.total)
    })

    it('compares months on what was left, which is the comparison worth making', () => {
      const [august, july] = summariseMonths(
        [
          spend('2026-07-01', 1800, 'Loyer juillet'),
          spend('2026-07-01', 100, 'Groceries'),
          spend('2026-08-01', 1500, 'Loyer aout'),
          spend('2026-08-01', 140, 'Groceries'),
        ],
        'CAD',
        inSeptember,
        ['Loyer'],
      )

      expect(july.total).toBe(100)
      expect(august.total).toBe(140)
      expect(august.versusPrevious).toEqual({ difference: 40, percent: 40, label: '2026-07-01' })
    })

    it('matches whatever the case', () => {
      const [august] = summariseMonths(
        [spend('2026-08-01', 1500, 'LOYER'), spend('2026-08-01', 20, 'Coffee')],
        'CAD',
        inSeptember,
        ['loyer'],
      )

      expect(august.biggest?.description).toBe('Coffee')
    })

    it('takes a star for anything, which is what people type', () => {
      const [august] = summariseMonths(
        [
          spend('2026-08-01', 1500, 'Loyer aout'),
          spend('2026-08-01', 1200, 'Hydro Quebec'),
          spend('2026-08-01', 40, 'Pizza'),
        ],
        'CAD',
        inSeptember,
        ['Loyer*', 'Hydro*'],
      )

      expect(august.biggest?.description).toBe('Pizza')
      expect(august.ignored).toEqual({ total: 2700, count: 2 })
    })

    it('reads punctuation as itself rather than as a pattern', () => {
      const [august] = summariseMonths(
        [spend('2026-08-01', 900, 'Rent (flat)'), spend('2026-08-01', 30, 'Coffee')],
        'CAD',
        inSeptember,
        ['Rent (flat)'],
      )

      expect(august.biggest?.description).toBe('Coffee')
    })

    it('has no biggest when everything in the month was left out', () => {
      const [august] = summariseMonths(
        [spend('2026-08-01', 1500, 'Loyer')],
        'CAD',
        inSeptember,
        ['Loyer'],
      )

      expect(august.biggest).toBeNull()
      expect(august.total).toBe(0)
      expect(august.count).toBe(0)
      expect(august.ignored).toEqual({ total: 1500, count: 1 })
    })
  })

  it('reads newest first, which is the order the screen wants', () => {
    const summaries = summariseMonths(
      [spend('2026-05-01', 10), spend('2026-08-01', 10), spend('2026-06-01', 10)],
      'CAD',
      inSeptember,
    )

    expect(summaries.map((month) => month.key)).toEqual([
      '2026-08-01',
      '2026-06-01',
      '2026-05-01',
    ])
  })

  it('is the month in progress alone when nothing has finished yet', () => {
    const summaries = summariseMonths([spend('2026-09-01', 40)], 'CAD', inSeptember)

    expect(summaries.map((month) => [month.key, month.total])).toEqual([['2026-09-01', 40]])
  })

  it('is empty when there is nothing at all', () => {
    expect(summariseMonths([], 'CAD', inSeptember)).toEqual([])
  })
})
