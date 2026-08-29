import { minorUnit, roundMoney } from './money'

export interface MemberBalance {
  memberId: string
  net: number
}

export interface BalanceExpense {
  payerMemberId: string
  amount: number
  splits: Array<{ memberId: string; amount: number }>
}

export interface BalanceSettlement {
  fromMemberId: string
  toMemberId: string
  amount: number
}

export interface Transfer {
  fromMemberId: string
  toMemberId: string
  amount: number
}

/**
 * Balance math run locally, so a group screen shows correct numbers while offline
 * and updates the instant an expense is added rather than after a round trip.
 *
 * Amounts are expected to already be in the group base currency, exactly as the
 * server stores them, so this never converts and never disagrees with the server
 * about a rate.
 */
export function netBalances(
  memberIds: string[],
  expenses: BalanceExpense[],
  settlements: BalanceSettlement[],
  currency = 'CAD',
): MemberBalance[] {
  const net = new Map<string, number>()
  for (const memberId of new Set(memberIds)) net.set(memberId, 0)

  // Members who left still carry history, so ids outside the roster are accepted.
  const bump = (memberId: string, delta: number) =>
    net.set(memberId, (net.get(memberId) ?? 0) + delta)

  for (const expense of expenses) {
    bump(expense.payerMemberId, expense.amount)
    for (const split of expense.splits) bump(split.memberId, -split.amount)
  }

  for (const settlement of settlements) {
    bump(settlement.fromMemberId, settlement.amount)
    bump(settlement.toMemberId, -settlement.amount)
  }

  return [...net.entries()]
    .map(([memberId, value]) => ({ memberId, net: roundMoney(value, currency) }))
    .sort((left, right) => (left.memberId < right.memberId ? -1 : 1))
}

/**
 * Fewest transfers that settle everyone: collapse to net positions, then match the
 * biggest debtor to the biggest creditor. Ties break on member id so this agrees
 * with the server's plan rather than offering the user a different one.
 */
export function simplifyDebts(balances: MemberBalance[], currency = 'CAD'): Transfer[] {
  const epsilon = minorUnit(currency) / 2

  const creditors: MemberBalance[] = []
  const debtors: MemberBalance[] = []

  for (const balance of balances) {
    const net = roundMoney(balance.net, currency)
    if (net > epsilon) creditors.push({ memberId: balance.memberId, net })
    else if (net < -epsilon) debtors.push({ memberId: balance.memberId, net: -net })
  }

  if (creditors.length === 0 || debtors.length === 0) return []

  const byAmountThenId = (left: MemberBalance, right: MemberBalance) => {
    const difference = right.net - left.net
    if (Math.abs(difference) > 1e-9) return difference
    return left.memberId < right.memberId ? -1 : 1
  }
  creditors.sort(byAmountThenId)
  debtors.sort(byAmountThenId)

  const transfers: Transfer[] = []
  let ci = 0
  let di = 0
  let creditRemaining = creditors[0].net
  let debtRemaining = debtors[0].net

  while (ci < creditors.length && di < debtors.length) {
    const amount = Math.min(creditRemaining, debtRemaining)

    if (amount > epsilon) {
      transfers.push({
        fromMemberId: debtors[di].memberId,
        toMemberId: creditors[ci].memberId,
        amount: roundMoney(amount, currency),
      })
    }

    creditRemaining -= amount
    debtRemaining -= amount

    if (creditRemaining <= epsilon && ++ci < creditors.length) creditRemaining = creditors[ci].net
    if (debtRemaining <= epsilon && ++di < debtors.length) debtRemaining = debtors[di].net
  }

  return transfers
}

/**
 * The unreduced view. Some people prefer it because it shows the actual expense
 * that created a debt rather than a netted-off suggestion.
 */
export function pairwiseDebts(
  expenses: BalanceExpense[],
  settlements: BalanceSettlement[],
  currency = 'CAD',
): Transfer[] {
  const ledger = new Map<string, number>()
  const key = (from: string, to: string) => `${from}>${to}`

  const add = (from: string, to: string, amount: number) => {
    if (from === to || amount === 0) return

    // Fold the reverse direction into one signed entry per unordered pair.
    const reverseKey = key(to, from)
    if (ledger.has(reverseKey)) {
      ledger.set(reverseKey, ledger.get(reverseKey)! - amount)
      return
    }

    const forwardKey = key(from, to)
    ledger.set(forwardKey, (ledger.get(forwardKey) ?? 0) + amount)
  }

  for (const expense of expenses) {
    for (const split of expense.splits) add(split.memberId, expense.payerMemberId, split.amount)
  }

  for (const settlement of settlements) {
    add(settlement.toMemberId, settlement.fromMemberId, settlement.amount)
  }

  const epsilon = minorUnit(currency) / 2
  const result: Transfer[] = []

  for (const [pair, amount] of ledger) {
    const [from, to] = pair.split('>')
    const rounded = roundMoney(amount, currency)
    if (Math.abs(rounded) <= epsilon) continue

    result.push(
      rounded > 0
        ? { fromMemberId: from, toMemberId: to, amount: rounded }
        : { fromMemberId: to, toMemberId: from, amount: -rounded },
    )
  }

  return result.sort((left, right) =>
    left.fromMemberId === right.fromMemberId
      ? left.toMemberId < right.toMemberId
        ? -1
        : 1
      : left.fromMemberId < right.fromMemberId
        ? -1
        : 1,
  )
}
