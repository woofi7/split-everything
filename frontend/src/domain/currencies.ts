const REMEMBERED_KEY = 'split-everything.last-expense-currency'
const RECENT_KEY = 'split-everything.recent-currencies'
const RECENT_LIMIT = 5

export const SUPPORTED_CURRENCIES = [
  'AED', 'AFN', 'ALL', 'AMD', 'ANG', 'AOA', 'ARS', 'AUD', 'AWG', 'AZN', 'BAM', 'BBD',
  'BDT', 'BHD', 'BIF', 'BMD', 'BND', 'BOB', 'BRL', 'BSD', 'BTN', 'BWP', 'BYN', 'BZD',
  'CAD', 'CDF', 'CHF', 'CLP', 'CNY', 'COP', 'CRC', 'CUP', 'CVE', 'CZK', 'DJF', 'DKK',
  'DOP', 'DZD', 'EGP', 'ERN', 'ETB', 'EUR', 'FJD', 'FKP', 'GBP', 'GEL', 'GHS', 'GIP',
  'GMD', 'GNF', 'GTQ', 'GYD', 'HKD', 'HNL', 'HTG', 'HUF', 'IDR', 'ILS', 'INR', 'IQD',
  'IRR', 'ISK', 'JMD', 'JOD', 'JPY', 'KES', 'KGS', 'KHR', 'KMF', 'KPW', 'KRW', 'KWD',
  'KYD', 'KZT', 'LAK', 'LBP', 'LKR', 'LRD', 'LSL', 'LYD', 'MAD', 'MDL', 'MGA', 'MKD',
  'MMK', 'MNT', 'MOP', 'MRU', 'MUR', 'MVR', 'MWK', 'MXN', 'MYR', 'MZN', 'NAD', 'NGN',
  'NIO', 'NOK', 'NPR', 'NZD', 'OMR', 'PAB', 'PEN', 'PGK', 'PHP', 'PKR', 'PLN', 'PYG',
  'QAR', 'RON', 'RSD', 'RUB', 'RWF', 'SAR', 'SBD', 'SCR', 'SDG', 'SEK', 'SGD', 'SHP',
  'SLE', 'SOS', 'SRD', 'SSP', 'STN', 'SVC', 'SYP', 'SZL', 'THB', 'TJS', 'TMT', 'TND',
  'TOP', 'TRY', 'TTD', 'TWD', 'TZS', 'UAH', 'UGX', 'USD', 'UYU', 'UZS', 'VES', 'VND',
  'VUV', 'WST', 'XAF', 'XCD', 'XCG', 'XOF', 'XPF', 'YER', 'ZAR', 'ZMW', 'ZWG',
] as const

const MISSPELLINGS: Record<string, readonly string[]> = {
  THB: ['bath'],
}

function displayName(type: 'currency' | 'region', code: string, locale: string): string | null {
  try {
    const name = new Intl.DisplayNames([locale], { type, fallback: 'none' }).of(code)
    return name && name !== code ? name : null
  } catch {
    return null
  }
}

export function currencyName(code: string, locale: string): string {
  return displayName('currency', code, locale) ?? code
}

export function currencyKeywords(code: string, locale: string): string[] {
  const region = code.startsWith('X') ? null : code.slice(0, 2)

  return [
    displayName('currency', code, locale),
    displayName('currency', code, 'en'),
    region && displayName('region', region, locale),
    region && displayName('region', region, 'en'),
    ...(MISSPELLINGS[code] ?? []),
  ].filter((word, index, all): word is string => Boolean(word) && all.indexOf(word) === index)
}

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
