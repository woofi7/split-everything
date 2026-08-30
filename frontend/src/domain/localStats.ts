/**
 * The stats screen, computed from the local replica.
 *
 * The endpoint that normally answers this needs a connection, and the screen used
 * to say so and show nothing: "Stats need a connection" on a device holding every
 * expense it was about. Everything the chart and the tables show is arithmetic over
 * rows this device already has, so it is done here instead, and the server's answer
 * - which can also convert between currencies - replaces it when one arrives.
 *
 * The rules mirror StatsService deliberately, down to the order of the payers in a
 * bucket and where the rounding residue lands, so the same spending does not read
 * differently depending on whether the request got through.
 */
import { bucketOf, type Granularity } from '@/domain/buckets'
import { roundMoney } from '@/domain/money'

export interface LocalStatsExpense {
  groupId: string
  paidByMemberId: string
  amountInBaseCurrency: number
  spentAt: string
  splits: readonly { memberId: string; amountInBaseCurrency: number }[]
}

export interface LocalStatsSettlement {
  fromMemberId: string
  toMemberId: string
  amountInBaseCurrency: number
}

export interface LocalStatsInput {
  currency: string
  granularity: Granularity
  /** The memberships that are this person, across the groups in scope. */
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

export interface LocalSpendPoint {
  bucket: string
  amount: number
  expenseCount: number
  byMember: LocalSpendPointMember[]
}

export interface LocalMemberSpend {
  memberId: string
  memberName: string
  paid: number
  owed: number
  net: number
}

export interface LocalStats {
  currency: string
  totalSpend: number
  myShare: number
  myPaid: number
  expenseCount: number
  spendOverTime: LocalSpendPoint[]
  byMember: LocalMemberSpend[]
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

  const myPaid = expenses
    .filter((expense) => mine.has(expense.paidByMemberId))
    .reduce((sum, expense) => sum + expense.amountInBaseCurrency, 0)

  return {
    currency,
    totalSpend: round(totalSpend),
    myShare: round(myShare),
    myPaid: round(myPaid),
    expenseCount: expenses.length,
    spendOverTime: spendOverTime(expenses, input.granularity, names, round),
    byMember: byMember(input, names, round),
  }
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
        paid.set(
          expense.paidByMemberId,
          (paid.get(expense.paidByMemberId) ?? 0) + expense.amountInBaseCurrency,
        )
      }

      const payers = [...paid.entries()]
        .map(([memberId, amount]) => ({
          memberId,
          memberName: names.get(memberId) ?? 'Someone',
          amount: round(amount),
        }))
        .filter((payer) => payer.amount !== 0)
        // Largest first, so a stack does not reshuffle its colours from one bucket
        // to the next.
        .sort((left, right) => right.amount - left.amount || left.memberName.localeCompare(right.memberName))

      /*
       * Rounding each share on its own can leave the parts a cent off the whole, and
       * a stacked bar whose parts do not sum to its total is a lie about both. The
       * largest share absorbs it, as everywhere else in this app.
       */
      const residue = round(total - payers.reduce((sum, payer) => sum + payer.amount, 0))
      if (residue !== 0 && payers.length > 0) {
        payers[0] = { ...payers[0], amount: round(payers[0].amount + residue) }
      }

      return { bucket, amount: total, expenseCount: inBucket.length, byMember: payers }
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
        .filter((expense) => expense.paidByMemberId === member.id)
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
    // Somebody who has neither paid nor owed anything is not in this table: an
    // empty row per placeholder member is noise, not information.
    .filter((row) => row.paid !== 0 || row.owed !== 0 || row.net !== 0)
    .sort((left, right) => right.paid - left.paid)
}
