import { roundMoney } from './money'

/**
 * What a finished month came to.
 *
 * A month that has ended is a fact: it can be totalled, compared and argued about.
 * The one running is a fact in progress, and comparing three days of September
 * against the whole of August says only that September is young - so the summary
 * covers complete months and leaves the current one to its own total.
 */
export interface MonthSpend {
  /** The month, as a bucket key: the first of it. */
  key: string
  amountInBaseCurrency: number
  /** Who put money in, which is not the same as who owed it. */
  payers: ReadonlyArray<{ memberId: string; amountInBaseCurrency: number }>
  description: string
}

export interface MonthTotalByMember {
  memberId: string
  amount: number
}

export interface MonthSummary {
  key: string
  total: number
  count: number

  /** Largest contribution first, so the list reads in the order that matters. */
  byMember: MonthTotalByMember[]

  /** The single largest expense of the month, or null in a month with none. */
  biggest: { description: string; amount: number } | null

  /**
   * Against the month before it, when there is one to compare with. The percentage
   * is left out when the previous month was zero: everything is infinitely more
   * than nothing, which is true and useless.
   */
  versusPrevious: { difference: number; percent: number | null; label: string } | null

  /**
   * Against the mean of every other complete month. Null when this is the only
   * complete month there is, because a month cannot differ from itself.
   */
  versusAverage: number | null
}

/**
 * Summarises every complete month, newest first.
 *
 * Complete means ended: the month `today` falls in is left out however much is in
 * it. Months with no expenses do not appear at all - a summary of nothing is a row
 * that says nothing - but they are not treated as zero in the average either, which
 * would drag it towards a floor nobody spent at.
 */
export function summariseMonths(
  expenses: readonly MonthSpend[],
  currency: string,
  today: Date = new Date(),
): MonthSummary[] {
  const current = monthKeyOf(today)

  const byMonth = new Map<string, MonthSpend[]>()
  for (const expense of expenses) {
    if (expense.key === current) continue

    const found = byMonth.get(expense.key)
    if (found) found.push(expense)
    else byMonth.set(expense.key, [expense])
  }

  // Oldest first while the comparisons are worked out, because each month looks
  // back at the one before it.
  const keys = [...byMonth.keys()].sort()
  const totals = new Map<string, number>(
    keys.map((key) => [
      key,
      roundMoney(
        byMonth.get(key)!.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
        currency,
      ),
    ]),
  )

  const summaries = keys.map((key, index) => {
    const month = byMonth.get(key)!
    const total = totals.get(key)!

    const paid = new Map<string, number>()
    for (const expense of month) {
      for (const payer of expense.payers) {
        paid.set(
          payer.memberId,
          (paid.get(payer.memberId) ?? 0) + payer.amountInBaseCurrency,
        )
      }
    }

    const byMember = [...paid]
      .map(([memberId, amount]) => ({ memberId, amount: roundMoney(amount, currency) }))
      .filter((entry) => entry.amount !== 0)
      .sort((left, right) => right.amount - left.amount || left.memberId.localeCompare(right.memberId))

    const biggest = month.reduce<MonthSpend | null>(
      (largest, expense) =>
        largest === null || expense.amountInBaseCurrency > largest.amountInBaseCurrency
          ? expense
          : largest,
      null,
    )

    const previousKey = index > 0 ? keys[index - 1] : null
    const previousTotal = previousKey === null ? null : totals.get(previousKey)!

    const others = keys.filter((other) => other !== key).map((other) => totals.get(other)!)
    const average = others.length === 0
      ? null
      : others.reduce((sum, amount) => sum + amount, 0) / others.length

    return {
      key,
      total,
      count: month.length,
      byMember,
      biggest: biggest === null
        ? null
        : {
            description: biggest.description,
            amount: roundMoney(biggest.amountInBaseCurrency, currency),
          },
      versusPrevious: previousKey === null || previousTotal === null
        ? null
        : {
            difference: roundMoney(total - previousTotal, currency),
            percent: previousTotal === 0
              ? null
              : Math.round(((total - previousTotal) / previousTotal) * 100),
            label: previousKey,
          },
      versusAverage: average === null ? null : roundMoney(total - average, currency),
    }
  })

  // Newest first for reading, which is the order the screen wants.
  return summaries.reverse()
}

/** The bucket key of a date's month: the first of it, in local time. */
function monthKeyOf(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${date.getFullYear()}-${month}-01`
}
