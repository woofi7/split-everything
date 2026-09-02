import { describe, expect, it } from 'vitest'
import { netBalances, pairwiseDebts, simplifyDebts } from '@/domain/balances'

const a = 'aaaaaaaa-0000-0000-0000-000000000001'
const b = 'bbbbbbbb-0000-0000-0000-000000000002'
const c = 'cccccccc-0000-0000-0000-000000000003'
const d = 'dddddddd-0000-0000-0000-000000000004'

const expense = (payer: string, amount: number, splits: Array<[string, number]>) => ({
  payers: [{ memberId: payer, amount }],
  splits: splits.map(([memberId, share]) => ({ memberId, amount: share })),
})

/** An expense several people paid for at once. */
const sharedExpense = (payers: Array<[string, number]>, splits: Array<[string, number]>) => ({
  payers: payers.map(([memberId, amount]) => ({ memberId, amount })),
  splits: splits.map(([memberId, share]) => ({ memberId, amount: share })),
})

describe('net balances offline', () => {
  it('is all zeros with no activity', () => {
    expect(netBalances([a, b], [], [])).toEqual([
      { memberId: a, net: 0 },
      { memberId: b, net: 0 },
    ])
  })

  it('credits the payer their outlay less their own share', () => {
    const balances = netBalances([a, b], [expense(a, 100, [[a, 50], [b, 50]])], [])

    expect(balances.find((x) => x.memberId === a)!.net).toBe(50)
    expect(balances.find((x) => x.memberId === b)!.net).toBe(-50)
  })

  it('always sums to zero', () => {
    const balances = netBalances(
      [a, b, c],
      [expense(a, 90, [[a, 30], [b, 30], [c, 30]]), expense(b, 45, [[a, 15], [b, 15], [c, 15]])],
      [],
    )

    expect(balances.reduce((sum, x) => sum + x.net, 0)).toBe(0)
  })

  it('applies a settlement', () => {
    const balances = netBalances(
      [a, b],
      [expense(a, 100, [[a, 50], [b, 50]])],
      [{ fromMemberId: b, toMemberId: a, amount: 50 }],
    )

    expect(balances.every((x) => x.net === 0)).toBe(true)
  })

  it('flips the debt when a settlement overpays', () => {
    const balances = netBalances(
      [a, b],
      [expense(a, 100, [[a, 50], [b, 50]])],
      [{ fromMemberId: b, toMemberId: a, amount: 70 }],
    )

    expect(balances.find((x) => x.memberId === b)!.net).toBe(20)
  })

  it('keeps the history of a member who left the roster', () => {
    const balances = netBalances([a, b], [expense(c, 60, [[a, 30], [b, 30]])], [])

    expect(balances.find((x) => x.memberId === c)!.net).toBe(60)
    expect(balances.reduce((sum, x) => sum + x.net, 0)).toBe(0)
  })
})

describe('simplified debts offline', () => {
  it('needs no transfers when everyone is square', () => {
    expect(simplifyDebts([{ memberId: a, net: 0 }, { memberId: b, net: 0 }])).toEqual([])
  })

  it('turns a single debt into a single transfer', () => {
    const transfers = simplifyDebts([{ memberId: a, net: -25 }, { memberId: b, net: 25 }])

    expect(transfers).toEqual([{ fromMemberId: a, toMemberId: b, amount: 25 }])
  })

  it('collapses a chain', () => {
    const transfers = simplifyDebts([
      { memberId: a, net: -30 },
      { memberId: b, net: 0 },
      { memberId: c, net: 30 },
    ])

    expect(transfers).toHaveLength(1)
    expect(transfers[0]).toEqual({ fromMemberId: a, toMemberId: c, amount: 30 })
  })

  it('settles four people in at most three transfers', () => {
    const transfers = simplifyDebts([
      { memberId: a, net: -40 },
      { memberId: b, net: -20 },
      { memberId: c, net: 35 },
      { memberId: d, net: 25 },
    ])

    expect(transfers.length).toBeLessThanOrEqual(3)
    expect(transfers.reduce((sum, t) => sum + t.amount, 0)).toBe(60)
  })

  it('leaves everyone at zero once applied', () => {
    const balances = [
      { memberId: a, net: -73.21 },
      { memberId: b, net: 12.5 },
      { memberId: c, net: 45.71 },
      { memberId: d, net: 15 },
    ]

    const net = Object.fromEntries(balances.map((x) => [x.memberId, x.net]))
    for (const transfer of simplifyDebts(balances)) {
      net[transfer.fromMemberId] += transfer.amount
      net[transfer.toMemberId] -= transfer.amount
    }

    expect(Object.values(net).every((v) => Math.abs(v) < 0.01)).toBe(true)
  })

  it('never has one person both paying and receiving', () => {
    const transfers = simplifyDebts([
      { memberId: a, net: -50 },
      { memberId: b, net: -30 },
      { memberId: c, net: 80 },
    ])

    const payers = new Set(transfers.map((t) => t.fromMemberId))
    const receivers = new Set(transfers.map((t) => t.toMemberId))

    expect([...payers].filter((p) => receivers.has(p))).toEqual([])
  })

  it('ignores sub-cent noise', () => {
    expect(simplifyDebts([{ memberId: a, net: -0.004 }, { memberId: b, net: 0.004 }])).toEqual([])
  })

  it('is deterministic across input orderings, so two devices agree', () => {
    const balances = [
      { memberId: a, net: -40 },
      { memberId: b, net: -20 },
      { memberId: c, net: 35 },
      { memberId: d, net: 25 },
    ]

    expect(simplifyDebts(balances)).toEqual(simplifyDebts([...balances].reverse()))
  })

  it('breaks ties by member id, matching the server', () => {
    const balances = [
      { memberId: a, net: -20 },
      { memberId: b, net: 10 },
      { memberId: c, net: 10 },
    ]

    expect(simplifyDebts(balances)).toEqual(simplifyDebts([...balances].reverse()))
  })

  it('settles a zero-decimal currency in whole units', () => {
    const transfers = simplifyDebts(
      [{ memberId: a, net: -1000 }, { memberId: b, net: 1000 }],
      'JPY',
    )

    expect(transfers[0].amount).toBe(1000)
  })

  it('yields nothing when everyone is a creditor', () => {
    expect(simplifyDebts([{ memberId: a, net: 10 }, { memberId: b, net: 20 }])).toEqual([])
  })

  it('yields nothing for an empty group', () => {
    expect(simplifyDebts([])).toEqual([])
  })
})

describe('expenses several people paid for', () => {
  it('credits each payer what they put in', () => {
    // The frying pans: 40 from one, 25 from the other, split evenly down the middle.
    const balances = netBalances(
      [a, b],
      [sharedExpense([[a, 40], [b, 25]], [[a, 32.5], [b, 32.5]])],
      [],
    )

    expect(balances.find((balance) => balance.memberId === a)?.net).toBe(7.5)
    expect(balances.find((balance) => balance.memberId === b)?.net).toBe(-7.5)
  })

  it('owes a share to each payer in the proportion they paid', () => {
    const debts = pairwiseDebts([sharedExpense([[a, 60], [b, 40]], [[c, 100]])], [])

    expect(debts.find((debt) => debt.toMemberId === a)?.amount).toBe(60)
    expect(debts.find((debt) => debt.toMemberId === b)?.amount).toBe(40)
    expect(debts.every((debt) => debt.fromMemberId === c)).toBe(true)
  })

  it('still sums to zero', () => {
    const balances = netBalances(
      [a, b, c],
      [sharedExpense([[a, 33.33], [b, 66.67]], [[a, 33.33], [b, 33.33], [c, 33.34]])],
      [],
    )

    expect(balances.reduce((sum, balance) => sum + balance.net, 0)).toBe(0)
  })
})

describe('pairwise debts offline', () => {
  it('shows who owes whom before simplification', () => {
    const debts = pairwiseDebts([expense(a, 100, [[a, 50], [b, 50]])], [])

    expect(debts).toEqual([{ fromMemberId: b, toMemberId: a, amount: 50 }])
  })

  it('nets opposite debts into one direction', () => {
    const debts = pairwiseDebts([expense(a, 100, [[b, 100]]), expense(b, 40, [[a, 40]])], [])

    expect(debts).toEqual([{ fromMemberId: b, toMemberId: a, amount: 60 }])
  })

  it('drops a fully repaid pair', () => {
    const debts = pairwiseDebts(
      [expense(a, 50, [[b, 50]])],
      [{ fromMemberId: b, toMemberId: a, amount: 50 }],
    )

    expect(debts).toEqual([])
  })

  it('creates no self-debt for the payer own share', () => {
    expect(pairwiseDebts([expense(a, 50, [[a, 50]])], [])).toEqual([])
  })

  it('agrees with the net view on each member total', () => {
    const expenses = [
      expense(a, 90, [[a, 30], [b, 30], [c, 30]]),
      expense(b, 60, [[a, 20], [b, 20], [c, 20]]),
      expense(c, 30, [[a, 10], [b, 10], [c, 10]]),
    ]
    const settlements = [{ fromMemberId: c, toMemberId: a, amount: 15 }]

    const net = Object.fromEntries(
      netBalances([a, b, c], expenses, settlements).map((x) => [x.memberId, x.net]),
    )

    const fromPairwise: Record<string, number> = { [a]: 0, [b]: 0, [c]: 0 }
    for (const debt of pairwiseDebts(expenses, settlements)) {
      fromPairwise[debt.fromMemberId] -= debt.amount
      fromPairwise[debt.toMemberId] += debt.amount
    }

    for (const [member, expected] of Object.entries(net)) {
      expect(fromPairwise[member]).toBeCloseTo(expected, 2)
    }
  })
})
