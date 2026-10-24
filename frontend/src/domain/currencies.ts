const REMEMBERED_KEY = 'split-everything.last-expense-currency'
const RECENT_KEY = 'split-everything.recent-currencies'
const RECENT_LIMIT = 5

export const SUPPORTED_CURRENCIES = [
  'AUD', 'BGN', 'BRL', 'CAD', 'CHF', 'CNY', 'CZK', 'DKK', 'EUR', 'GBP',
  'HKD', 'HUF', 'IDR', 'ILS', 'INR', 'ISK', 'JPY', 'KRW', 'MXN', 'MYR',
  'NOK', 'NZD', 'PHP', 'PLN', 'RON', 'SEK', 'SGD', 'THB', 'TRY', 'USD', 'ZAR',
] as const

function readJson<T>(key: string, fallback: T): T {
  try {
    const parsed = JSON.parse(localStorage.getItem(key) ?? 'null') as unknown
    return parsed && typeof parsed === 'object' ? (parsed as T) : fallback
  } catch {
    return fallback
  }
}

function writeJson(key: string, value: unknown): void {
  try {
    localStorage.setItem(key, JSON.stringify(value))
  } catch {
  }
}

export function lastExpenseCurrency(groupId: string): string | null {
  const stored = readJson<Record<string, unknown>>(REMEMBERED_KEY, {})[groupId]
  return typeof stored === 'string' && /^[A-Z]{3}$/.test(stored) ? stored : null
}

export function rememberExpenseCurrency(groupId: string, currency: string): void {
  if (!groupId || !/^[A-Z]{3}$/.test(currency)) return

  writeJson(REMEMBERED_KEY, { ...readJson(REMEMBERED_KEY, {}), [groupId]: currency })

  const recent = readJson<string[]>(RECENT_KEY, []).filter((code) => code !== currency)
  writeJson(RECENT_KEY, [currency, ...recent].slice(0, RECENT_LIMIT))
}

export function currencyChoices(baseCurrency: string, selected: string): string[] {
  const recent = readJson<string[]>(RECENT_KEY, []).filter((code) => typeof code === 'string')
  const first = [baseCurrency, ...recent, selected].filter(
    (code, index, all) => code && all.indexOf(code) === index,
  )
  const rest = SUPPORTED_CURRENCIES.filter((code) => !first.includes(code))

  return [...first, ...rest]
}
