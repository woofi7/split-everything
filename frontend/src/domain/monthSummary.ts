import { roundMoney } from './money'
import { matchesAnyNamePattern } from './namePatterns'

export interface MonthSpend {
  key: string
  amountInBaseCurrency: number
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

  byMember: MonthTotalByMember[]

  biggest: { description: string; amount: number } | null

  ignored: { total: number; count: number } | null

  versusPrevious: { difference: number; percent: number | null; label: string } | null

  versusAverage: number | null
}

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

  const keys = [...byMonth.keys()].sort()

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

  const finished = summaries.reverse()

  if (running.length === 0) return finished

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

function monthKeyOf(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  return `${date.getFullYear()}-${month}-01`
}
