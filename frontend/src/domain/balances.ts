import { minorUnit, roundMoney } from './money'

export interface MemberBalance {
  memberId: string
  net: number
}

export interface BalanceExpense {
  /**
   * Who put money in, and how much. A list because an expense can be paid by more
   * than one person at once, and no single-payer stand-in gets the balances right:
   * two people paying 40 and 25 of a 65 bill split evenly are 7.50 apart, not 32.50.
   */
  payers: Array<{ memberId: string; amount: number }>
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
    for (const payer of expense.payers) bump(payer.memberId, payer.amount)
    for (const split of expense.splits) bump(split.memberId, -split.amount)
  }

  for (const settlement of settlements) {
    bump(settlement.fromMemberId, settlement.amount)
    bump(settlement.toMemberId, -settlement.amount)
  }

  const rounded = [...net.entries()]
    .map(([memberId, value]) => ({ memberId, net: roundMoney(value, currency) }))
    .sort((left, right) => (left.memberId < right.memberId ? -1 : 1))

  /*
   * Balances have to sum to zero, and rounding each of them to a payable cent can
   * leave them a cent short of it: shares are worked out finer than the currency,
   * so a net position can be a fraction of a cent either way.
   *
   * The residue goes to the largest balance, as everywhere else. Left in, it is a
   * cent nobody can pay off - the settle-up plan moves whole cents, so it would
   * hand somebody a debt of one that survives being paid.
   */
  const residue = roundMoney(
    -rounded.reduce((sum, balance) => sum + balance.net, 0),
    currency,
  )
  if (residue === 0 || rounded.length === 0) return rounded

  let index = 0
  for (let i = 1; i < rounded.length; i++) {
    const bigger = Math.abs(rounded[i].net) - Math.abs(rounded[index].net)
    if (bigger > 1e-9 || (Math.abs(bigger) < 1e-9 && rounded[i].memberId < rounded[index].memberId)) {
      index = i
    }
  }

  rounded[index] = { ...rounded[index], net: roundMoney(rounded[index].net + residue, currency) }
  return rounded
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
    const paid = expense.payers.reduce((sum, payer) => sum + payer.amount, 0)
    if (paid === 0) continue

    for (const split of expense.splits) {
      // Owed to whoever put the money in, in the proportion each of them did: of a
      // bill two people covered 40/25, a share is owed 40/65 to one and 25/65 to
      // the other. Rounded once at the end, so the proportions keep their cents.
      for (const payer of expense.payers) {
        add(split.memberId, payer.memberId, (split.amount * payer.amount) / paid)
      }
    }
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
