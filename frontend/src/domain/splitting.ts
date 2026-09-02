import { minorUnit, roundShare, shareDecimals } from './money'

export type SplitType = 'Equal' | 'Percentage' | 'Shares' | 'ExactAmount' | 'Itemized'

export interface SplitInput {
  memberId: string
  value: number | null
}

export interface SplitShare {
  memberId: string
  amount: number
  inputValue: number | null
}

export interface ItemizedLine {
  amount: number
  quantity: number
  memberIds: string[]
}

/**
 * The same split arithmetic the server runs.
 *
 * Duplicated on purpose: an expense created offline has to show the person the
 * exact amounts that will end up stored, and it has to survive being replayed to
 * the server without being recalculated into something different. That means both
 * sides must agree to the last minor unit, which is why this uses the same
 * largest-remainder distribution with the same member-id tie-break rather than
 * anything more convenient.
 */
export function calculateSplit(
  total: number,
  currency: string,
  splitType: SplitType,
  inputs: SplitInput[],
): SplitShare[] {
  if (inputs.length === 0) {
    throw new Error('An expense needs at least one participant.')
  }

  const seen = new Set<string>()
  for (const input of inputs) {
    if (seen.has(input.memberId)) {
      throw new Error('A participant cannot appear twice in a split.')
    }
    seen.add(input.memberId)
  }

  switch (splitType) {
    case 'Equal':
      return byWeight(
        total,
        currency,
        inputs.map((i) => ({ memberId: i.memberId, value: 1 })),
        false,
      )

    case 'Percentage': {
      const sum = inputs.reduce((acc, i) => acc + (i.value ?? 0), 0)
      if (Math.abs(sum - 100) > 0.01) {
        throw new Error(`Percentages must add up to 100, got ${sum}.`)
      }
      const shares = byWeight(total, currency, inputs, false)
      return shares.map((share) => ({
        ...share,
        inputValue: inputs.find((i) => i.memberId === share.memberId)?.value ?? null,
      }))
    }

    case 'Shares':
      return byWeight(total, currency, inputs, true)

    case 'ExactAmount': {
      const sum = inputs.reduce((acc, i) => acc + (i.value ?? 0), 0)
      if (Math.abs(sum - total) > minorUnit(currency) / 2) {
        throw new Error(`Exact amounts must add up to ${total}, got ${sum}.`)
      }
      return reconcile(
        total,
        currency,
        inputs.map((i) => ({
          memberId: i.memberId,
          amount: roundShare(i.value ?? 0, currency),
          inputValue: i.value ?? null,
        })),
      )
    }

    case 'Itemized':
      throw new Error('Itemized splits are computed from items; call calculateItemizedSplit.')

    default:
      throw new Error(`Unknown split type: ${splitType}`)
  }
}

/**
 * Itemized: each line is shared equally by whoever is on it, then anything the
 * lines do not cover (tax, tip, service) is spread over the participants in
 * proportion to what they already owe.
 */
export function calculateItemizedSplit(
  total: number,
  currency: string,
  lines: ItemizedLine[],
  fallbackMemberIds: string[],
): SplitShare[] {
  if (lines.length === 0) {
    if (fallbackMemberIds.length === 0) {
      throw new Error('An itemized expense needs items or participants.')
    }
    return calculateSplit(
      total,
      currency,
      'Equal',
      fallbackMemberIds.map((memberId) => ({ memberId, value: null })),
    )
  }

  const raw = new Map<string, number>()
  let itemisedTotal = 0

  for (const line of lines) {
    const participants = line.memberIds.length > 0 ? line.memberIds : fallbackMemberIds
    if (participants.length === 0) {
      throw new Error('An item has no participants and no group fallback.')
    }

    const lineTotal = line.amount * Math.max(1, line.quantity)
    itemisedTotal += lineTotal

    const lineShares = byWeight(
      lineTotal,
      currency,
      participants.map((memberId) => ({ memberId, value: 1 })),
      false,
    )

    for (const share of lineShares) {
      raw.set(share.memberId, (raw.get(share.memberId) ?? 0) + share.amount)
    }
  }

  const remainder = total - itemisedTotal
  if (Math.abs(remainder) > 1e-9) {
    const weights = [...raw.entries()].map(([memberId, amount]) => ({
      memberId,
      value: amount > 0 ? amount : 1,
    }))

    for (const share of byWeight(remainder, currency, weights, false)) {
      raw.set(share.memberId, (raw.get(share.memberId) ?? 0) + share.amount)
    }
  }

  return reconcile(
    total,
    currency,
    [...raw.entries()].map(([memberId, amount]) => ({ memberId, amount, inputValue: null })),
  )
}

/**
 * Weighted split with largest-remainder rounding, computed in whole minor units
 * so no floating point error can survive into a stored amount. Ties break on
 * member id, which is what makes two devices agree offline.
 */
function byWeight(
  total: number,
  currency: string,
  inputs: SplitInput[],
  keepInput: boolean,
): SplitShare[] {
  const weights = inputs.map((i) => i.value ?? 0)
  const weightSum = weights.reduce((acc, w) => acc + w, 0)
  if (weightSum <= 0) {
    throw new Error('Split weights must add up to more than zero.')
  }

  // Share precision, not the currency's: an even split of an odd number of cents
  // is then actually even, rather than a cent handed to whoever wins the tie-break.
  const decimals = shareDecimals(currency)
  const factor = 10 ** decimals
  const sign = total < 0 ? -1 : 1
  const totalUnits = Math.round(Math.abs(total) * factor)

  const baseUnits: number[] = []
  const fractions: number[] = []
  let assigned = 0

  for (let i = 0; i < inputs.length; i++) {
    const exact = (totalUnits * weights[i]) / weightSum
    const floor = Math.floor(exact)
    baseUnits.push(floor)
    fractions.push(exact - floor)
    assigned += floor
  }

  const leftover = totalUnits - assigned
  const order = inputs
    .map((_, index) => index)
    .sort((a, b) => {
      const byFraction = fractions[b] - fractions[a]
      if (Math.abs(byFraction) > 1e-9) return byFraction
      return inputs[a].memberId < inputs[b].memberId ? -1 : 1
    })

  for (let k = 0; k < leftover; k++) {
    baseUnits[order[k % order.length]] += 1
  }

  return inputs.map((input, index) => ({
    memberId: input.memberId,
    amount: Number(((sign * baseUnits[index]) / factor).toFixed(decimals)),
    inputValue: keepInput ? input.value ?? null : null,
  }))
}

/** Pushes any residue onto the largest share, for paths that build amounts additively. */
function reconcile(total: number, currency: string, shares: SplitShare[]): SplitShare[] {
  const rounded = shares.map((share) => ({
    ...share,
    amount: roundShare(share.amount, currency),
  }))

  const residue = roundShare(
    total - rounded.reduce((sum, share) => sum + share.amount, 0),
    currency,
  )
  if (residue === 0) return rounded

  let targetIndex = 0
  for (let i = 1; i < rounded.length; i++) {
    const bigger = Math.abs(rounded[i].amount) - Math.abs(rounded[targetIndex].amount)
    if (bigger > 1e-9 || (Math.abs(bigger) < 1e-9 && rounded[i].memberId < rounded[targetIndex].memberId)) {
      targetIndex = i
    }
  }

  rounded[targetIndex] = {
    ...rounded[targetIndex],
    amount: roundShare(rounded[targetIndex].amount + residue, currency),
  }

  return rounded
}

/**
 * The values that describe an existing division under a different split type.
 *
 * Switching type is usually the start of an adjustment, not a reset. Emptying the
 * form meant the split was invalid until every box had been typed again, and the
 * division someone had already agreed was gone.
 *
 * Percentages are reconciled to exactly 100, because rounding each one to two
 * places can leave three equal parts at 99.99, which the calculator refuses.
 * Shares are seeded with the amounts themselves: they are a ratio, and the amounts
 * are the one ratio guaranteed to reproduce the same division exactly.
 */
export function splitValuesFor(
  target: SplitType,
  shares: readonly { memberId: string; amount: number }[],
  total: number,
): Record<string, number> {
  if (target === 'Equal' || target === 'Itemized') return {}
  if (shares.length === 0 || total <= 0) return {}

  if (target === 'ExactAmount' || target === 'Shares') {
    return Object.fromEntries(shares.map((share) => [share.memberId, share.amount]))
  }

  const percentages = shares.map((share) => ({
    memberId: share.memberId,
    value: Math.round((share.amount / total) * 10000) / 100,
  }))

  // The largest share absorbs the rounding, as it does everywhere else here.
  const residue = Math.round((100 - percentages.reduce((sum, p) => sum + p.value, 0)) * 100) / 100

  if (residue !== 0) {
    const largest = percentages.reduce(
      (best, candidate) => (candidate.value > best.value ? candidate : best),
      percentages[0],
    )
    largest.value = Math.round((largest.value + residue) * 100) / 100
  }

  return Object.fromEntries(percentages.map((p) => [p.memberId, p.value]))
}
