/**
 * Client-side vector clock, mirroring SplitEverything.Domain.Sync.VectorClock.
 *
 * The client needs its own copy so it can decide locally whether a pending edit
 * is still based on the newest revision it knows about. Without that, every
 * offline edit would be pushed blind and the user would only learn about a
 * conflict after a round trip.
 */

export type VectorClock = Record<string, number>

export type ClockOrdering = 'equal' | 'after' | 'before' | 'concurrent'

export function emptyClock(): VectorClock {
  return {}
}

export function normalizeClock(clock: VectorClock | null | undefined): VectorClock {
  if (!clock) return {}

  const result: VectorClock = {}
  for (const [device, value] of Object.entries(clock)) {
    if (!device.trim() || !Number.isFinite(value) || value <= 0) continue
    result[device] = value
  }
  return result
}

export function tickClock(clock: VectorClock, deviceId: string): VectorClock {
  if (!deviceId || !deviceId.trim()) {
    throw new Error('A device id is required to tick a vector clock.')
  }

  return { ...normalizeClock(clock), [deviceId]: (clock[deviceId] ?? 0) + 1 }
}

/** Pointwise maximum: the join applied after a successful sync. */
export function mergeClocks(left: VectorClock, right: VectorClock): VectorClock {
  const merged = { ...normalizeClock(left) }

  for (const [device, value] of Object.entries(normalizeClock(right))) {
    if (!(device in merged) || value > merged[device]) merged[device] = value
  }

  return merged
}

export function compareClocks(left: VectorClock, right: VectorClock): ClockOrdering {
  const devices = new Set([...Object.keys(left), ...Object.keys(right)])

  let leftAhead = false
  let rightAhead = false

  for (const device of devices) {
    const mine = left[device] ?? 0
    const theirs = right[device] ?? 0
    if (mine > theirs) leftAhead = true
    else if (mine < theirs) rightAhead = true
    if (leftAhead && rightAhead) return 'concurrent'
  }

  if (leftAhead) return 'after'
  if (rightAhead) return 'before'
  return 'equal'
}

export function dominates(left: VectorClock, right: VectorClock): boolean {
  const ordering = compareClocks(left, right)
  return ordering === 'after' || ordering === 'equal'
}

export function hasUnseenEvents(mine: VectorClock, theirs: VectorClock): boolean {
  return Object.entries(theirs).some(([device, value]) => value > (mine[device] ?? 0))
}
