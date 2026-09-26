import { today } from '@/domain/lastExpenseDate'

const KEY = 'split-everything.last-expense-payer'

type Remembered = Record<string, { memberId: string; usedOn: string }>

function read(): Remembered {
  try {
    const parsed = JSON.parse(localStorage.getItem(KEY) ?? '{}') as unknown
    return parsed && typeof parsed === 'object' ? (parsed as Remembered) : {}
  } catch {
    return {}
  }
}

export function lastExpensePayer(groupId: string): string | null {
  const entry = read()[groupId]
  if (!entry || typeof entry.memberId !== 'string' || entry.usedOn !== today()) return null

  return entry.memberId
}

export function rememberExpensePayer(groupId: string, memberId: string): void {
  if (!groupId || !memberId) return

  try {
    localStorage.setItem(KEY, JSON.stringify({ ...read(), [groupId]: { memberId, usedOn: today() } }))
  } catch {
  }
}
