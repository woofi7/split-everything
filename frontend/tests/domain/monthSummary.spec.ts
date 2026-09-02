import { describe, expect, it } from 'vitest'
import { summariseMonths, type MonthSpend } from '@/domain/monthSummary'

/**
 * What a finished month came to.
 *
 * A month that has ended can be totalled and compared. The one running cannot, not
 * usefully: three days of September against the whole of August says only that
 * September is young.
 */
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

/** Well into the month, so nothing depends on which day the suite runs. */
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

  it('leaves out the month that has not finished', () => {
    const summaries = summariseMonths(
      [spend('2026-09-01', 500), spend('2026-08-01', 25)],
      'CAD',
      inSeptember,
    )

    // September is a fact in progress. Comparing it to a whole August would say
    // only that September is young.
    expect(summaries.map((month) => month.key)).toEqual(['2026-08-01'])
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
    // Everything is infinitely more than nothing, which is true and useless.
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

    // August against the mean of June and July, which is 150.
    expect(summaries[0].versusAverage).toBe(450)
    // June against the mean of July and August, which is 400.
    expect(summaries[2].versusAverage).toBe(-300)
  })

  it('says nothing about the average when there is only one month', () => {
    const summaries = summariseMonths([spend('2026-08-01', 250)], 'CAD', inSeptember)

    expect(summaries[0].versusAverage).toBeNull()
  })

  it('does not count a month with no expenses as a month of zero', () => {
    // A gap is not a month somebody spent nothing in; it is a month nobody entered
    // anything for, and averaging it in would drag the mean towards a floor.
    const summaries = summariseMonths(
      [spend('2026-06-01', 300), spend('2026-08-01', 300)],
      'CAD',
      inSeptember,
    )

    expect(summaries.map((month) => month.key)).toEqual(['2026-08-01', '2026-06-01'])
    expect(summaries[0].versusAverage).toBe(0)
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

  it('is empty when nothing has finished yet', () => {
    expect(summariseMonths([spend('2026-09-01', 40)], 'CAD', inSeptember)).toEqual([])
  })
})
