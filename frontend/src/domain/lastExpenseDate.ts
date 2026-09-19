
const KEY = 'split-everything.last-expense-date'

const CALENDAR_DATE = /^\d{4}-\d{2}-\d{2}$/

export function isoDate(when: Date): string {
  const month = String(when.getMonth() + 1).padStart(2, '0')
  const day = String(when.getDate()).padStart(2, '0')

  return `${when.getFullYear()}-${month}-${day}`
}

export function today(): string {
  return isoDate(new Date())
}

export function lastExpenseDate(): string | null {
  try {
    const stored = localStorage.getItem(KEY)
    if (!stored) return null

    const { date, usedOn } = JSON.parse(stored) as { date?: unknown; usedOn?: unknown }
    if (typeof date !== 'string' || !CALENDAR_DATE.test(date)) return null
    if (usedOn !== today()) return null

    return date
  } catch {
    return null
  }
}

export function rememberExpenseDate(date: string): void {
  if (!CALENDAR_DATE.test(date)) return

  try {
    localStorage.setItem(KEY, JSON.stringify({ date, usedOn: today() }))
  } catch {
  }
}
