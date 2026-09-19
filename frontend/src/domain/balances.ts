import { minorUnit, roundMoney } from './money'

export interface MemberBalance {
  memberId: string
  net: number
}

export interface BalanceExpense {
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

export function netBalances(
  memberIds: string[],
  expenses: BalanceExpense[],
  settlements: BalanceSettlement[],
  currency = 'CAD',
): MemberBalance[] {
  const net = new Map<string, number>()
  for (const memberId of new Set(memberIds)) net.set(memberId, 0)

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

export function pairwiseDebts(
  expenses: BalanceExpense[],
  settlements: BalanceSettlement[],
  currency = 'CAD',
): Transfer[] {
  const ledger = new Map<string, number>()
  const key = (from: string, to: string) => `${from}>${to}`

  const add = (from: string, to: string, amount: number) => {
    if (from === to || amount === 0) return

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
