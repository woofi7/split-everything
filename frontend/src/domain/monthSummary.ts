import { roundMoney } from './money'
import { matchesAnyNamePattern } from './namePatterns'

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

/**
 * A month, as the screen states it.
 *
 * Every figure here is the month without the names the group asked to leave out.
 * A household with rent in it spends fifteen hundred a month before it has bought
 * anything at all, and a total carrying that is a total that barely moves: August
 * against July is two rents and a rounding error, and the question people actually
 * ask - did we spend more than usual? - cannot be read off it. What was left out is
 * never dropped, only stated separately, so the two halves add back up to the money
 * that moved.
 *
 * What is owed is a different question and no business of a display rule: balances
 * and who owes whom count every expense there is.
 */
export interface MonthSummary {
  key: string

  /** The month's everyday spending: what it came to, less whatever was left out. */
  total: number

  /** How many expenses that total covers, which is not how many the month holds. */
  count: number

  /** Largest contribution first, so the list reads in the order that matters. */
  byMember: MonthTotalByMember[]

  /**
   * The single largest expense of the month, ignoring any name the group asked to
   * leave out. Null in a month where everything was ignored, or which has none.
   */
  biggest: { description: string; amount: number } | null

  /**
   * What the left-out expenses came to, and how many there were.
   *
   * Shown rather than silently dropped, because the total above no longer carries
   * it: a total that is quietly missing fifteen hundred is a total nobody can
   * check against the list underneath, and the whole defence of leaving anything
   * out is that the screen says so and the two figures still add up.
   */
  ignored: { total: number; count: number } | null

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
 * Summarises every month there is, newest first.
 *
 * The month running gets one too: it is the month somebody is looking at, and the
 * screen had nothing to say about it - who has paid for September so far is a fair
 * question, and it was answered with a blank. What it does not get is the
 * comparisons. Three days of September against the whole of August says only that
 * September is young, and the same goes for the average, which is worked out from
 * finished months alone so a month two days old cannot drag it down.
 *
 * Months with no expenses do not appear at all - a summary of nothing is a row that
 * says nothing - and they are not treated as zero in the average either. A month
 * holding nothing but rent does appear, with a total of zero and the rent stated
 * beside it, because that is what the month was.
 */
export function summariseMonths(
  expenses: readonly MonthSpend[],
  currency: string,
  today: Date = new Date(),
  ignoredNamePatterns: readonly string[] = [],
): MonthSummary[] {
  const isIgnored = (description: string) =>
    matchesAnyNamePattern(description, ignoredNamePatterns)

  const current = monthKeyOf(today)

  const byMonth = new Map<string, MonthSpend[]>()
  const running: MonthSpend[] = []

  for (const expense of expenses) {
    if (expense.key === current) {
      running.push(expense)
      continue
    }

    const found = byMonth.get(expense.key)
    if (found) found.push(expense)
    else byMonth.set(expense.key, [expense])
  }

  // Oldest first while the comparisons are worked out, because each month looks
  // back at the one before it.
  const keys = [...byMonth.keys()].sort()

  // Every total, every comparison and every share of who paid is drawn from these
  // rather than from the month: one filter, applied once, so no two figures on the
  // screen can ever be counting different expenses.
  const kept = new Map<string, MonthSpend[]>(
    keys.map((key) => [
      key,
      byMonth.get(key)!.filter((expense) => !isIgnored(expense.description)),
    ]),
  )

  const totals = new Map<string, number>(
    keys.map((key) => [
      key,
      roundMoney(
        kept.get(key)!.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
        currency,
      ),
    ]),
  )

  const summaries = keys.map((key, index) => {
    const previousKey = index > 0 ? keys[index - 1] : null
    const previousTotal = previousKey === null ? null : totals.get(previousKey)!
    const total = totals.get(key)!

    const others = keys.filter((other) => other !== key).map((other) => totals.get(other)!)
    const average = others.length === 0
      ? null
      : others.reduce((sum, amount) => sum + amount, 0) / others.length

    return {
      ...describe(key, byMonth.get(key)!, kept.get(key)!, currency, isIgnored),
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
  const finished = summaries.reverse()

  if (running.length === 0) return finished

  // The month in progress, described and not compared. It leads, because it is the
  // month on screen.
  return [
    {
      ...describe(
        current,
        running,
        running.filter((expense) => !isIgnored(expense.description)),
        currency,
        isIgnored,
      ),
      versusPrevious: null,
      versusAverage: null,
    },
    ...finished,
  ]
}

/**
 * One month, without the comparisons: what it came to, who paid it, the biggest
 * thing in it and what was left out.
 *
 * Shared by the months that have ended and the one that has not, so the two cannot
 * drift into saying the same thing differently - which they would, being written
 * twice.
 */
function describe(
  key: string,
  month: readonly MonthSpend[],
  considered: readonly MonthSpend[],
  currency: string,
  isIgnored: (description: string) => boolean,
): Omit<MonthSummary, 'versusPrevious' | 'versusAverage'> {
  const paid = new Map<string, number>()
  for (const expense of considered) {
    for (const payer of expense.payers) {
      paid.set(payer.memberId, (paid.get(payer.memberId) ?? 0) + payer.amountInBaseCurrency)
    }
  }

  const byMember = [...paid]
    .map(([memberId, amount]) => ({ memberId, amount: roundMoney(amount, currency) }))
    .filter((entry) => entry.amount !== 0)
    .sort((left, right) => right.amount - left.amount || left.memberId.localeCompare(right.memberId))

  // The biggest thing worth calling biggest. A rent line every month is larger than
  // everything else put together and says nothing the total has not already said.
  const biggest = considered.reduce<MonthSpend | null>(
    (largest, expense) =>
      largest === null || expense.amountInBaseCurrency > largest.amountInBaseCurrency
        ? expense
        : largest,
    null,
  )

  const skipped = month.filter((expense) => isIgnored(expense.description))

  return {
    key,
    total: roundMoney(
      considered.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
      currency,
    ),
    count: considered.length,
    byMember,
    biggest: biggest === null
      ? null
      : {
          description: biggest.description,
          amount: roundMoney(biggest.amountInBaseCurrency, currency),
        },
    ignored: skipped.length === 0
      ? null
      : {
          total: roundMoney(
            skipped.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
            currency,
          ),
          count: skipped.length,
        },
  }
}


/** The bucket key of a date's month: the first of it, in local time. */
function monthKeyOf(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${date.getFullYear()}-${month}-01`
}
