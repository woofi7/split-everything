/**
 * The date a new expense starts on.
 *
 * Today is right for the expense you are typing as you leave the shop, and wrong
 * for the six receipts from last weekend somebody is catching up on: every one of
 * them needed the date field opened and the same day picked again. So the date
 * used last is remembered and offered as the starting point, and the form says
 * plainly when that is not today.
 *
 * A device preference rather than account state, like the main group: it is about
 * the typing happening on this phone right now, and it has to survive a reload
 * without waiting on the network. Remembered only when an expense is added, never
 * when one is edited - correcting the date of something from March says nothing
 * about where the next expense belongs.
 */

const KEY = 'split-everything.last-expense-date'

/** A calendar date as the date input wants it, in the device's own timezone. */
export function isoDate(when: Date): string {
  const month = String(when.getMonth() + 1).padStart(2, '0')
  const day = String(when.getDate()).padStart(2, '0')

  return `${when.getFullYear()}-${month}-${day}`
}

/**
 * Today, locally.
 *
 * Not the UTC date: an evening west of Greenwich is already tomorrow there, so a
 * form filled in at nine at night would open on a day that has not happened yet,
 * and the expense would land in the wrong week of every total on the stats screen.
 */
export function today(): string {
  return isoDate(new Date())
}

/** The date last used to add an expense on this device, if there is one. */
export function lastExpenseDate(): string | null {
  try {
    const stored = localStorage.getItem(KEY)

    // Anything that is not a calendar date is ignored rather than trusted: it
    // would come from a hand-edited value or an older shape, and a date input
    // given nonsense shows blank with no way to tell why.
    return stored && /^\d{4}-\d{2}-\d{2}$/.test(stored) ? stored : null
  } catch {
    // Storage can be unavailable outright in a locked-down browser. A form that
    // opens on today is the whole loss.
    return null
  }
}

export function rememberExpenseDate(date: string): void {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) return

  try {
    localStorage.setItem(KEY, date)
  } catch {
    // Same again: the next form opens on today, and nothing else is affected.
  }
}
