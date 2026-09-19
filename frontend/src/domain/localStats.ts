import { bucketOf, type Granularity } from '@/domain/buckets'
import { roundMoney } from '@/domain/money'

export interface LocalStatsExpense {
  groupId: string
  paidByMemberId: string
  categoryKey?: string | null
  amountInBaseCurrency: number
  spentAt: string
  splits: readonly { memberId: string; amountInBaseCurrency: number }[]
  payers?: readonly { memberId: string; amountInBaseCurrency: number }[]
}

function payersOf(
  expense: LocalStatsExpense,
): readonly { memberId: string; amountInBaseCurrency: number }[] {
  return expense.payers && expense.payers.length > 0
    ? expense.payers
    : [{ memberId: expense.paidByMemberId, amountInBaseCurrency: expense.amountInBaseCurrency }]
}

export interface LocalStatsSettlement {
  fromMemberId: string
  toMemberId: string
  amountInBaseCurrency: number
}

export interface LocalStatsInput {
  currency: string
  granularity: Granularity
  myMemberIds: readonly string[]
  members: readonly { id: string; displayName: string }[]
  expenses: readonly LocalStatsExpense[]
  settlements: readonly LocalStatsSettlement[]
}

export interface LocalSpendPointMember {
  memberId: string
  memberName: string
  amount: number
}

export interface LocalSpendPointCategory {
  key: string | null
  amount: number
}

export interface LocalSpendPoint {
  bucket: string
  amount: number
  expenseCount: number
  byMember: LocalSpendPointMember[]
  byCategory: LocalSpendPointCategory[]
}

export interface LocalMemberSpend {
  memberId: string
  memberName: string
  paid: number
  owed: number
  net: number
}

export interface LocalCategorySpend {
  key: string | null
  amount: number
  expenseCount: number
}

export interface LocalStats {
  currency: string
  totalSpend: number
  myShare: number
  myPaid: number
  expenseCount: number
  spendOverTime: LocalSpendPoint[]
  byMember: LocalMemberSpend[]
  byCategory: LocalCategorySpend[]
}

export function computeStats(input: LocalStatsInput): LocalStats {
  const { currency, expenses } = input
  const mine = new Set(input.myMemberIds)
  const names = new Map(input.members.map((member) => [member.id, member.displayName]))
  const round = (amount: number) => roundMoney(amount, currency)

  const totalSpend = expenses.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0)

  const myShare = expenses.reduce(
    (sum, expense) =>
      sum +
      expense.splits
        .filter((split) => mine.has(split.memberId))
        .reduce((part, split) => part + split.amountInBaseCurrency, 0),
    0,
  )

  const myPaid = expenses.reduce(
    (sum, expense) =>
      sum +
      payersOf(expense)
        .filter((payer) => mine.has(payer.memberId))
        .reduce((part, payer) => part + payer.amountInBaseCurrency, 0),
    0,
  )

  return {
    currency,
    totalSpend: round(totalSpend),
    myShare: round(myShare),
    myPaid: round(myPaid),
    expenseCount: expenses.length,
    spendOverTime: spendOverTime(expenses, input.granularity, names, round),
    byMember: byMember(input, names, round),
    byCategory: byCategory(expenses, round),
  }
}

function byCategory(
  expenses: readonly LocalStatsExpense[],
  round: (amount: number) => number,
): LocalCategorySpend[] {
  const totals = new Map<string | null, { amount: number; expenseCount: number }>()

  for (const expense of expenses) {
    const key = expense.categoryKey ?? null
    const found = totals.get(key) ?? { amount: 0, expenseCount: 0 }

    found.amount += expense.amountInBaseCurrency
    found.expenseCount += 1
    totals.set(key, found)
  }

  return [...totals]
    .map(([key, row]) => ({ key, amount: round(row.amount), expenseCount: row.expenseCount }))
    .filter((row) => row.amount !== 0)
    .sort(
      (left, right) =>
        right.amount - left.amount || (left.key ?? '').localeCompare(right.key ?? ''),
    )
}

function categoriesIn(
  inBucket: readonly LocalStatsExpense[],
  total: number,
  round: (amount: number) => number,
): LocalSpendPointCategory[] {
  const totals = new Map<string | null, number>()

  for (const expense of inBucket) {
    const key = expense.categoryKey ?? null
    totals.set(key, (totals.get(key) ?? 0) + expense.amountInBaseCurrency)
  }

  const rows = [...totals.entries()]
    .map(([key, amount]) => ({ key, amount: round(amount) }))
    .filter((row) => row.amount !== 0)
    .sort(
      (left, right) =>
        right.amount - left.amount || (left.key ?? '').localeCompare(right.key ?? ''),
    )

  const residue = round(total - rows.reduce((sum, row) => sum + row.amount, 0))
  if (residue !== 0 && rows.length > 0) rows[0] = { ...rows[0], amount: round(rows[0].amount + residue) }

  return rows
}

function spendOverTime(
  expenses: readonly LocalStatsExpense[],
  granularity: Granularity,
  names: Map<string, string>,
  round: (amount: number) => number,
): LocalSpendPoint[] {
  const buckets = new Map<string, LocalStatsExpense[]>()

  for (const expense of expenses) {
    const bucket = bucketOf(expense.spentAt, granularity)
    const held = buckets.get(bucket)
    if (held) held.push(expense)
    else buckets.set(bucket, [expense])
  }

  return [...buckets.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([bucket, inBucket]) => {
      const total = round(
        inBucket.reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0),
      )

      const paid = new Map<string, number>()
      for (const expense of inBucket) {
        for (const payer of payersOf(expense)) {
          paid.set(
            payer.memberId,
            (paid.get(payer.memberId) ?? 0) + payer.amountInBaseCurrency,
          )
        }
      }

      const payers = [...paid.entries()]
        .map(([memberId, amount]) => ({
          memberId,
          memberName: names.get(memberId) ?? 'Someone',
          amount: round(amount),
        }))
        .filter((payer) => payer.amount !== 0)
        .sort((left, right) => right.amount - left.amount || left.memberName.localeCompare(right.memberName))

      const residue = round(total - payers.reduce((sum, payer) => sum + payer.amount, 0))
      if (residue !== 0 && payers.length > 0) {
        payers[0] = { ...payers[0], amount: round(payers[0].amount + residue) }
      }

      return {
        bucket,
        amount: total,
        expenseCount: inBucket.length,
        byMember: payers,
        byCategory: categoriesIn(inBucket, total, round),
      }
    })
}

function byMember(
  input: LocalStatsInput,
  names: Map<string, string>,
  round: (amount: number) => number,
): LocalMemberSpend[] {
  return input.members
    .map((member) => {
      const paid = input.expenses
        .flatMap((expense) => payersOf(expense).filter((payer) => payer.memberId === member.id))
        .reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0)

      const owed = input.expenses.reduce(
        (sum, expense) =>
          sum +
          expense.splits
            .filter((split) => split.memberId === member.id)
            .reduce((part, split) => part + split.amountInBaseCurrency, 0),
        0,
      )

      const settledOut = input.settlements
        .filter((settlement) => settlement.fromMemberId === member.id)
        .reduce((sum, settlement) => sum + settlement.amountInBaseCurrency, 0)

      const settledIn = input.settlements
        .filter((settlement) => settlement.toMemberId === member.id)
        .reduce((sum, settlement) => sum + settlement.amountInBaseCurrency, 0)

      return {
        memberId: member.id,
        memberName: names.get(member.id) ?? member.displayName,
        paid: round(paid),
        owed: round(owed),
        net: round(paid - owed + settledOut - settledIn),
      }
    })
    .filter((row) => row.paid !== 0 || row.owed !== 0 || row.net !== 0)
    .sort((left, right) => right.paid - left.paid)
}
