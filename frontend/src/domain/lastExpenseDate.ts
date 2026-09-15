/**
 * The date a new expense starts on.
 *
 * Today is right for the expense you are typing as you leave the shop, and wrong
 * for the six receipts from last weekend somebody is catching up on: every one of
 * them needed the date field opened and the same day picked again. So the date
 * used last is remembered and offered as the starting point, and the form says
 * plainly when that is not today.
 *
 * Remembered for the rest of the day and no longer. That bound is the whole
 * lesson of the first version, which kept the date for ever: somebody spent an
 * evening entering a stack of receipts from the fourth of August, came back the
 * next day, and every expense they added went quietly into August. They were
 * looking at September, where a dashboard opens, so each one looked as though it
 * had not been added at all - and the app was right, and unhelpful, and gave no
 * sign of it. A batch is one sitting; a new day is a new question.
 *
 * A device preference rather than account state, like the main group: it is about
 * the typing happening on this phone right now, and it has to survive a reload
 * without waiting on the network. Recorded only when an expense is added, never
 * when one is edited - correcting the date of something from March says nothing
 * about where the next expense belongs.
 */

const KEY = 'split-everything.last-expense-date'

const CALENDAR_DATE = /^\d{4}-\d{2}-\d{2}$/

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

/**
 * The date last used to add an expense on this device today, if there is one.
 *
 * Two dates are stored: the one that was used, and the day it was used on. Only
 * the second decides whether the first is still worth offering, and anything else
 * in there - an older shape, a hand-edited value - counts as nothing, so a device
 * that met the version with no expiry simply starts from today.
 */
export function lastExpenseDate(): string | null {
  try {
    const stored = localStorage.getItem(KEY)
    if (!stored) return null

    const { date, usedOn } = JSON.parse(stored) as { date?: unknown; usedOn?: unknown }
    if (typeof date !== 'string' || !CALENDAR_DATE.test(date)) return null
    if (usedOn !== today()) return null

    return date
  } catch {
    // Unreadable, or storage unavailable outright in a locked-down browser. A form
    // that opens on today is the whole loss.
    return null
  }
}

export function rememberExpenseDate(date: string): void {
  if (!CALENDAR_DATE.test(date)) return

  try {
    localStorage.setItem(KEY, JSON.stringify({ date, usedOn: today() }))
  } catch {
    // Same again: the next form opens on today, and nothing else is affected.
  }
}
